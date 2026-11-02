#include "Profiles.hpp"
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
        auto bad = hello(); bad[4] = 1;
        check(!acceptHello(bad), "Unknown protocol versions reject");
        bad = hello(); bad[23] = 10;
        check(!acceptHello(bad), "Incompatible send rates reject");
        bad = hello(); bad[19] = 1;
        check(!acceptHello(bad), "Unknown capabilities reject");
        Profile owner; owner.name = "Guest V"; owner.generation = 9;
        owner.build = {60, 50, 20, 15, 20, 9, 17, 4, 2, 1, 50, 30, 40, 60, 20, 1};
        Profile profileResult; uint32_t profileSequence = 0;
        auto profileBytes = encode(owner, 12);
        check(decode(profileBytes, profileResult, profileSequence) && profileResult == owner && profileSequence == 12, "Character summary and chosen name round trip");
        bool truncatedProfilesReject = true;
        for (size_t i = 0; i < profileBytes.size(); ++i) if (decode(profileBytes.substr(0, i), profileResult, profileSequence)) truncatedProfilesReject = false;
        check(truncatedProfilesReject && !decode(profileBytes + "x", profileResult, profileSequence), "Truncated and extended profiles reject");
        check(validPlayerName("V") && validPlayerName("\xc3\x89" "cho") && validPlayerName("\xe5\xa4\x9c\xe5\x9f\x8e"), "International player names accept valid UTF-8");
        check(!validPlayerName("<b>V</b>") && !validPlayerName("V\nHost") && !validPlayerName("\xe2\x80\xaeV") && !validPlayerName(std::string(25, 'V')) && !validPlayerName(" V"), "Markup, controls, hidden directions and long names reject");
        check(!validPlayerName(std::string("\xc0\xaf", 2)) && !validPlayerName(std::string("\xed\xa0\x80", 3)) && !validPlayerName(std::string("\xf4\x90\x80\x80", 4)), "Invalid, surrogate and overlong UTF-8 reject");
        auto badProfile = profileBytes; badProfile[23] = 96;
        check(!decode(badProfile, profileResult, profileSequence), "Untrusted profile name lengths cannot allocate memory");
        auto invalidProfile = owner; invalidProfile.build[15] = 2;
        check(encode(invalidProfile, 1).empty(), "Invalid body metadata rejects");
        invalidProfile = owner; invalidProfile.build[0] = 100001;
        check(encode(invalidProfile, 1).empty(), "Out-of-range character values reject");
        invalidProfile = owner; invalidProfile.generation = 0;
        check(encode(invalidProfile, 1).empty(), "Build data must belong to a loaded character generation");
        Profiles profiles;
        check(!profiles.receive(owner, 1), "Disconnected profile mailbox rejects stale peer data");
        profiles.begin();
        check(profiles.receive(owner, 1) && profiles.sample(profileResult) && profileResult == owner && !profiles.receive(owner, 1), "Remote identity is bounded and revisions cannot replay");
        profiles.end(); check(!profiles.sample(profileResult), "Disconnect clears peer name and character summary");
        profiles.begin(); check(profiles.receive(owner, 1), "Reconnect resets profile revisions");
        check(profiles.setName("Local V") && profiles.publishBuild(9, owner.build, 1000) && profiles.outgoing(1001).build == owner.build, "Owner publishes their own read-only character summary");
        check(profiles.outgoing(4001).generation == 0 && profiles.outgoing(4001).build[0] == 0, "Stale local character summaries expire");
        profiles.clearBuild(); check(profiles.outgoing(1001).generation == 0 && profiles.outgoing(1001).name == "Local V", "Leaving a save clears its build while retaining the chosen name");
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
            if (decode(noise, result, sequence) || decode(noise, profileResult, profileSequence)) fuzzOk = false;
        }
        check(fuzzOk, "20000 malformed random movement and profile frames handled without untrusted allocations");
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
