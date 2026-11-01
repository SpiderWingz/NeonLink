#include "Replication.hpp"
#include <atomic>
#include <iostream>
#include <limits>
#include <random>
#include <stdexcept>
#include <thread>
using namespace nl::replication;
unsigned checks = 0;
void check(bool value, const char* name) {
    if (!value) throw std::runtime_error(name);
    ++checks; std::cout << "PASS " << name << '\n';
}
bool near(float a, float b) { return std::abs(a - b) < 0.001f; }
int main() {
    try {
        check(acceptHello(hello()), "Movement capabilities negotiate");
        auto bad = hello(); bad[4] = 2;
        check(!acceptHello(bad), "Unknown protocol versions reject");
        bad = hello(); bad[23] = 10;
        check(!acceptHello(bad), "Incompatible send rates reject");
        bad = hello(); bad[19] = 3;
        check(!acceptHello(bad), "Unknown capabilities reject");
        Pose input; input.active = true; input.generation = 12; input.time = 0x100000001ULL;
        input.position = {-101.25f, 2122.5f, 50.0f}; input.rotation = {0, 0, 0.6f, 0.8f};
        Pose result; uint32_t sequence = 0;
        auto data = encode(input, 91);
        check(data.size() == 60 && decode(data, result, sequence) && sequence == 91 && result.generation == 12 &&
            result.time == input.time && result.position == input.position && near(result.rotation[2], 0.6f), "Pose wire format round trips without host endianness");
        bool rejectsTruncation = true;
        for (size_t size = 0; size < data.size(); ++size) if (decode(data.substr(0, size), result, sequence)) rejectsTruncation = false;
        check(rejectsTruncation, "Every truncated frame rejects");
        check(!decode(data + "x", result, sequence), "Trailing bytes reject");
        bad = data; bad[7] = 1; check(!decode(bad, result, sequence), "Reserved flags reject");
        bad = data; bad[15] = 43; check(!decode(bad, result, sequence), "Incorrect payload length rejects");
        bad = data; bad[23] = 2; check(!decode(bad, result, sequence), "Invalid player state rejects");
        bad = data; for (unsigned i = 8; i < 12; ++i) bad[i] = 0;
        check(!decode(bad, result, sequence), "Zero sequence rejects");
        bad = data; for (unsigned i = 16; i < 20; ++i) bad[i] = 0;
        check(!decode(bad, result, sequence), "Active player requires a world generation");
        bad = data; auto nan = std::bit_cast<uint32_t>(std::numeric_limits<float>::quiet_NaN());
        std::string encodedNan; put32(encodedNan, nan); bad.replace(32, 4, encodedNan);
        check(!decode(bad, result, sequence), "Network NaN rejects before game access");
        bad = data; std::string infinity; put32(infinity, 0x7f800000); bad.replace(44, 4, infinity);
        check(!decode(bad, result, sequence), "Network infinity rejects before game access");
        auto invalid = input; invalid.position[0] = 100001;
        check(encode(invalid, 1).empty(), "Out-of-world coordinates reject");
        invalid = input; invalid.rotation = {0, 0, 0, 0};
        check(encode(invalid, 1).empty(), "Zero quaternion rejects");
        invalid = input; invalid.rotation = {1, 1, 1, 1};
        check(encode(invalid, 1).empty(), "Unnormalized quaternion rejects");
        Mailbox mailbox;
        check(!mailbox.sample(1000, result), "No remote player before connection");
        check(mailbox.publish(input, 1000) && mailbox.outgoing(1000).active, "Main thread publishes local values");
        auto firstGeneration = mailbox.outgoing(1000).generation;
        check(mailbox.outgoing(2001).active == false, "Unrefreshed local pose expires");
        mailbox.clearLocal(); mailbox.publish(input, 2100);
        check(mailbox.outgoing(2100).generation != firstGeneration, "New local world invalidates previous generation");
        check(!mailbox.receive(input, 1, 1000), "Disconnected mailbox rejects late network samples");
        mailbox.begin(); input.time = 100; input.position = {0, 0, 0}; input.rotation = {0, 0, 0, 1};
        check(mailbox.receive(input, 1, 1000) && mailbox.sample(1000, result), "First remote pose becomes available");
        check(!mailbox.receive(input, 1, 1000), "Duplicate sequence rejects");
        auto older = input; older.time = 99;
        check(!mailbox.receive(older, 2, 1001), "Regressing sender clock rejects");
        input.time = 200; input.position[0] = 10; input.rotation = {0, 0, 0, -1};
        check(mailbox.receive(input, 2, 1100) && mailbox.sample(1150, result) && near(result.position[0], 5) &&
            near(std::abs(result.rotation[3]), 1), "Interpolation handles position and equivalent opposite-sign quaternions");
        check(mailbox.sample(1300, result) && near(result.position[0], 10), "No unbounded movement extrapolation");
        check(!mailbox.sample(2101, result), "Stale peer disappears after timeout");
        input.time = 300; input.position[0] = 1000;
        check(mailbox.receive(input, 3, 2200) && mailbox.sample(2200, result) && near(result.position[0], 1000), "Teleport snaps instead of crossing the map");
        input.time = 400; input.generation++; input.position[0] = 1005;
        check(mailbox.receive(input, 4, 2250) && mailbox.sample(2250, result) && near(result.position[0], 1005), "Changed world generation clears interpolation");
        input.active = false; input.time = 500;
        check(mailbox.receive(input, 5, 2300) && !mailbox.sample(2300, result), "Leaving game world removes remote pose immediately");
        older = input; older.active = true; older.time = 499;
        check(!mailbox.receive(older, 6, 2301), "Leaving a world does not reset the replay clock");
        mailbox.end();
        check(!mailbox.sample(2300, result), "Disconnect clears remote state");
        mailbox.begin(); input.active = true; input.time = 1;
        check(mailbox.receive(input, 1, 2400), "Fresh connection resets sequence and clock");
        std::mt19937 random(1234); bool fuzzOk = true;
        for (unsigned i = 0; i < 20000; ++i) {
            std::string noise(random() % 600, '\0'); for (char& c : noise) c = static_cast<char>(random());
            if (decode(noise, result, sequence)) fuzzOk = false;
        }
        check(fuzzOk, "20000 malformed random frames handled without allocations from untrusted length fields");
        std::atomic<bool> done{false}, healthy{true}; Mailbox concurrent; concurrent.begin();
        std::thread producer([&] {
            for (uint32_t i = 1; i <= 10000; ++i) {
                Pose pose; pose.active = true; pose.generation = 1; pose.time = i;
                pose.position[0] = static_cast<float>(i % 100);
                if (!concurrent.receive(pose, i, i + 1000)) healthy = false;
                concurrent.publish(pose, i + 1000);
            }
            done = true;
        });
        do { Pose sample; if (concurrent.sample(11000, sample) && !valid(sample)) healthy = false; concurrent.outgoing(11000); } while (!done);
        producer.join(); check(healthy, "Concurrent game and network access keeps bounded valid state");
        std::cout << checks << " movement checks passed\n"; return 0;
    } catch (const std::exception& error) { std::cerr << "FAILED " << error.what() << '\n'; return 1; }
}
