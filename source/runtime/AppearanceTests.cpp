#include "Appearance.hpp"
#include <cstdlib>
#include <iostream>
#include <random>

using namespace nl::replication;
int checks = 0;
void check(bool value, const char* message) {
    ++checks;
    if (!value) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}
void replace32(std::string& data, size_t offset, uint32_t value) {
    std::string replacement; put32(replacement, value); data.replace(offset, 4, replacement);
}
int main() {
    Appearance source; source.generation = 7; source.revision = 1;
    AppearanceMesh mesh; mesh.resource = 0xfedcba9876543210ULL; mesh.material = 5;
    mesh.skinning = 6; mesh.parent = 7; mesh.chunks = UINT64_MAX;
    mesh.transform[0] = -12345; source.meshes.push_back(mesh);
    source.morphs.push_back({12, 13, -1200});
    const auto frame = encode(source, 42);
    Appearance decoded; uint32_t sequence = 0;
    check(frame.size() == headerBytes + 20 + 80 + 20, "fixed layout size");
    check(decode(frame, decoded, sequence) && decoded == source && sequence == 42, "round trip signed fields and high-bit hashes");
    check(encode(source, 0).empty(), "zero sequence refused");
    auto invalid = source; invalid.generation = 0; check(!valid(invalid), "zero generation refused");
    invalid = source; invalid.revision = 0; check(!valid(invalid), "zero revision refused");
    invalid = source; invalid.body = 2; check(!valid(invalid), "unknown body refused");
    invalid = source; invalid.meshes.clear(); check(!valid(invalid), "empty meshes refused");
    invalid = source; invalid.meshes[0].kind = 3; check(!valid(invalid), "unknown component kind refused");
    invalid = source; invalid.meshes[0].resource = 0; check(!valid(invalid), "empty resource selector refused");
    invalid = source; invalid.meshes[0].transform[0] = INT32_MIN; check(!valid(invalid), "translation bounded without signed overflow");
    invalid = source; invalid.meshes[0].transform[6] = INT32_MAX; check(!valid(invalid), "quaternion bounded before squaring");
    invalid = source; invalid.meshes[0].transform[6] = 0; check(!valid(invalid), "zero quaternion refused");
    invalid = source; invalid.meshes.push_back(mesh); check(!valid(invalid), "duplicate meshes refused");
    invalid = source; invalid.morphs[0].target = 0; check(!valid(invalid), "zero morph target refused");
    invalid = source; invalid.morphs[0].region = 0; check(!valid(invalid), "zero morph region refused");
    invalid = source; invalid.morphs[0].weight = -10001; check(!valid(invalid), "morph weight lower bound");
    invalid = source; invalid.morphs[0].weight = 10001; check(!valid(invalid), "morph weight upper bound");
    invalid = source; invalid.morphs.push_back({12,13,500}); check(!valid(invalid), "duplicate morph selectors refused");
    bool allTruncationsRejected = true;
    for (size_t length = 0; length < frame.size(); ++length)
        allTruncationsRejected &= !decode(frame.substr(0, length), decoded, sequence);
    check(allTruncationsRejected, "every truncation refused");
    for (const auto [offset, value] : std::vector<std::pair<size_t, uint32_t>>{
        {0,0}, {4,0}, {8,0}, {12,0}, {16,0}, {20,0}, {24,2}, {28,0}, {28,UINT32_MAX}, {32,UINT32_MAX}, {36,3}}) {
        auto broken = frame; replace32(broken, offset, value);
        const auto before = decoded; const auto previousSequence = sequence;
        check(!decode(broken, decoded, sequence) && decoded == before && sequence == previousSequence, "malformed field refused without changing outputs");
    }
    auto trailing = frame + "x"; check(!decode(trailing, decoded, sequence), "trailing bytes refused");
    Appearance maximum; maximum.generation = maximum.revision = 1; maximum.body = 1;
    for (size_t i = 0; i < maxAppearanceMeshes; ++i) { mesh.resource = i + 1; maximum.meshes.push_back(mesh); }
    for (size_t i = 0; i < maxAppearanceMorphs; ++i) maximum.morphs.push_back({i + 1, i + 2, 10000});
    const auto largest = encode(maximum, 1);
    check(largest.size() == maxAppearanceBytes && decode(largest, decoded, sequence) && decoded == maximum, "maximum valid descriptor round trip");
    maximum.meshes.push_back(mesh); check(encode(maximum,1).empty(), "mesh count cap"); maximum.meshes.pop_back();
    maximum.morphs.push_back({1,2,0}); check(encode(maximum,1).empty(), "morph count cap");
    std::mt19937 random(0x524c4150); bool fuzzValid = true;
    for (int i = 0; i < 10000; ++i) {
        std::string input(random() % (maxAppearanceBytes + 129), '\0');
        for (char& byte : input) byte = static_cast<char>(random());
        if (decode(input, decoded, sequence)) fuzzValid &= valid(decoded) && input.size() <= maxAppearanceBytes;
    }
    check(fuzzValid, "ten thousand random frames stay within descriptor invariants");
    Appearances mailbox; Appearance sampled;
    check(!mailbox.receive(source,1), "disconnected receive refused"); mailbox.begin();
    check(mailbox.receive(source,1), "connected descriptor accepted");
    check(mailbox.sample(7,sampled) && sampled == source, "matching player generation sampled");
    check(!mailbox.sample(8,sampled), "another player generation cannot consume appearance");
    check(!mailbox.receive(source,2), "same revision cannot trigger another application");
    source.revision = 2; check(!mailbox.receive(source,1), "old packet sequence refused");
    check(mailbox.receive(source,2), "updated revision accepted");
    source.generation = 8; source.revision = 1; check(mailbox.receive(source,3), "new character generation accepted");
    check(!mailbox.sample(7,sampled), "old generation invalidated"); mailbox.end();
    check(!mailbox.sample(8,sampled), "disconnect clears appearance"); mailbox.begin();
    check(!mailbox.sample(8,sampled), "reconnect cannot reuse prior appearance");
    std::cout << checks << " appearance protocol/mailbox checks passed.\n";
}
