#include "AppearanceV2.hpp"
#include <cstdlib>
#include <iostream>
#include <random>

namespace v2 = nl::appearance_v2;
namespace wire = nl::replication;
int checks = 0;
void check(bool value, const char* message) {
    ++checks; if (!value) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}
void replace32(std::string& data, size_t offset, uint32_t value) {
    std::string replacement; wire::put32(replacement, value); data.replace(offset, 4, replacement);
}
int main() {
    v2::Appearance source; source.generation = 7; source.revision = 9; source.body = 1;
    v2::Mesh skin; skin.component = UINT64_MAX; skin.resource = 0xfedcba9876543210ULL;
    skin.kind = 1; skin.role = 1; skin.material = 2; skin.skinning = 3; skin.parent = 4; skin.slot = 5; skin.chunks = 7;
    skin.transform[0] = -12345; source.meshes.push_back(skin);
    auto garment = skin; garment.kind = 3; garment.role = 4; garment.resource = 10; source.meshes.push_back(garment);
    auto morphMesh = skin; morphMesh.kind = 2; morphMesh.role = 2; morphMesh.resource = 11; source.meshes.push_back(morphMesh);
    source.morphs.push_back({12, 13, -10000});
    auto frame = v2::encode(source, 42); v2::Appearance decoded; uint32_t sequence = 0;
    check(frame.size() == wire::headerBytes + v2::prefixBytes + 3 * v2::meshBytes + v2::morphBytes, "fixed layout length");
    check(v2::decode(frame, decoded, sequence) && decoded == source && sequence == 42, "body, garment, morph and signed fields round trip");
    check(decoded.meshes[0].component == decoded.meshes[1].component, "shared component selector accepted when full entries differ");
    check(v2::encode(source, 0).empty(), "zero sequence rejected");
    auto invalid = source; invalid.generation = 0; check(!v2::valid(invalid), "zero generation rejected");
    invalid = source; invalid.revision = 0; check(!v2::valid(invalid), "zero revision rejected");
    invalid = source; invalid.body = 2; check(!v2::valid(invalid), "unknown body rejected");
    invalid = source; invalid.meshes.clear(); check(!v2::valid(invalid), "empty mesh list rejected");
    invalid = source; invalid.meshes[0].kind = 0; check(!v2::valid(invalid), "zero kind rejected");
    invalid = source; invalid.meshes[0].kind = 4; check(!v2::valid(invalid), "unknown kind rejected");
    invalid = source; invalid.meshes[0].role = 0; check(!v2::valid(invalid), "zero role rejected");
    invalid = source; invalid.meshes[0].role = 7; check(!v2::valid(invalid), "unknown role rejected");
    invalid = source; invalid.meshes[0].component = 0; check(!v2::valid(invalid), "zero component rejected");
    invalid = source; invalid.meshes[0].resource = 0; check(!v2::valid(invalid), "zero resource rejected");
    invalid = source; invalid.meshes[0].transform[0] = INT32_MIN; check(!v2::valid(invalid), "out of range translation rejected");
    invalid = source; invalid.meshes[0].transform[3] = INT32_MIN; check(!v2::valid(invalid), "quaternion bounded before multiplication");
    invalid = source; invalid.meshes[0].transform[6] = 0; check(!v2::valid(invalid), "zero quaternion rejected");
    invalid = source; invalid.meshes.push_back(skin); check(!v2::valid(invalid), "identical full component rejected");
    invalid = source; invalid.morphs.push_back({12,13,0}); check(!v2::valid(invalid), "duplicate morph selector rejected");
    invalid = source; invalid.morphs[0].weight = 10001; check(!v2::valid(invalid), "unverified morph range rejected");

    bool truncations = true;
    for (size_t length = 0; length < frame.size(); ++length) truncations &= !v2::decode(frame.substr(0, length), decoded, sequence);
    check(truncations, "all truncations rejected");
    for (const auto [offset, bad] : std::vector<std::pair<size_t,uint32_t>>{
        {0,0}, {4,0}, {8,0}, {12,0}, {16,0}, {16,3}, {20,0}, {24,0}, {28,2},
        {32,0}, {32,UINT32_MAX}, {36,UINT32_MAX}, {40,4}, {44,7}}) {
        auto broken = frame; replace32(broken, offset, bad); auto old = decoded; auto oldSequence = sequence;
        check(!v2::decode(broken, decoded, sequence) && decoded == old && sequence == oldSequence, "invalid field rejected without output mutation");
    }
    check(!v2::decode(frame + "x", decoded, sequence), "trailing bytes rejected");
    wire::Appearance legacy; legacy.generation = legacy.revision = 1; wire::AppearanceMesh legacyMesh; legacyMesh.resource = 2; legacy.meshes.push_back(legacyMesh);
    auto oldFrame = wire::encode(legacy, 1); check(!v2::decode(oldFrame, decoded, sequence), "legacy format rejected");
    wire::Appearance legacyOutput; uint32_t oldSequence = 0;
    check(!wire::decode(frame, legacyOutput, oldSequence), "legacy parser rejects v2 format");

    v2::Appearance maximum; maximum.generation = maximum.revision = 1;
    for (size_t i = 0; i < v2::maxMeshes; ++i) { skin.component = i + 1; maximum.meshes.push_back(skin); }
    for (size_t i = 0; i < v2::maxMorphs; ++i) maximum.morphs.push_back({i + 1, i + 2, 10000});
    auto largest = v2::encode(maximum, 1);
    check(v2::maxFrameBytes == 11432 && largest.size() == v2::maxFrameBytes && v2::decode(largest, decoded, sequence) && decoded == maximum, "maximum descriptor bounded and round trips");
    maximum.meshes.push_back(skin); check(v2::encode(maximum, 1).empty(), "mesh count cap"); maximum.meshes.pop_back();
    maximum.morphs.push_back({1,2,0}); check(v2::encode(maximum, 1).empty(), "morph count cap");
    std::mt19937 random(0x52324c); bool sane = true;
    for (int i = 0; i < 10000; ++i) {
        std::string input(random() % (v2::maxFrameBytes + 64), '\0');
        for (char& byte : input) byte = static_cast<char>(random());
        if (v2::decode(input, decoded, sequence)) sane &= v2::valid(decoded) && input.size() <= v2::maxFrameBytes;
    }
    check(sane, "ten thousand random frames handled safely");
    std::cout << checks << " appearance v2 checks passed.\n";
}
