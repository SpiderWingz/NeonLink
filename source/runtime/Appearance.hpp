#pragma once

#include "Replication.hpp"
#include <vector>

namespace nl::replication {
// Values only. The engine adapter must additionally resolve assets locally,
// check their resource types and character paths, and target its own proxy ID.
// Never serialize engine objects or accept a template/class name from a peer.
constexpr size_t maxAppearanceMeshes = 96;
constexpr size_t maxAppearanceMorphs = 128;
constexpr size_t appearanceMeshBytes = 80;
constexpr size_t appearanceMorphBytes = 20;
constexpr size_t maxAppearanceBytes = headerBytes + 20 +
    maxAppearanceMeshes * appearanceMeshBytes + maxAppearanceMorphs * appearanceMorphBytes;
constexpr Kind appearanceKind = static_cast<Kind>(4);

struct AppearanceMesh {
    uint32_t kind = 1; // 1: ordinary skinned mesh; 2: morph-target mesh.
    uint64_t resource = 0, material = 0, skinning = 0, parent = 0, slot = 0, chunks = 0;
    // Local translation in 1/10,000 metres, then quaternion in 1/1,000,000.
    std::array<int32_t, 7> transform{0, 0, 0, 0, 0, 0, 1000000};
    bool operator==(const AppearanceMesh&) const = default;
};
struct AppearanceMorph {
    uint64_t target = 0, region = 0;
    int32_t weight = 0; // 1/10,000; no untrusted floating-point bit patterns.
    bool operator==(const AppearanceMorph&) const = default;
};
struct Appearance {
    uint32_t generation = 0, revision = 0, body = 0; // Body: 0 male / 1 female.
    std::vector<AppearanceMesh> meshes;
    std::vector<AppearanceMorph> morphs;
    bool operator==(const Appearance&) const = default;
};
inline void put64(std::string& data, uint64_t value) {
    put32(data, static_cast<uint32_t>(value >> 32)); put32(data, static_cast<uint32_t>(value));
}
inline uint64_t get64(const std::string& data, size_t offset) {
    return (static_cast<uint64_t>(get32(data, offset)) << 32) | get32(data, offset + 4);
}
inline bool valid(const Appearance& value) {
    if (!value.generation || !value.revision || value.body > 1 || value.meshes.empty() ||
        value.meshes.size() > maxAppearanceMeshes || value.morphs.size() > maxAppearanceMorphs) return false;
    for (size_t i = 0; i < value.meshes.size(); ++i) {
        const auto& mesh = value.meshes[i];
        if ((mesh.kind != 1 && mesh.kind != 2) || !mesh.resource) return false;
        for (size_t n = 0; n < 3; ++n) if (mesh.transform[n] < -100000 || mesh.transform[n] > 100000) return false;
        int64_t length = 0;
        for (size_t n = 3; n < 7; ++n) {
            const int64_t part = mesh.transform[n];
            if (part < -1100000 || part > 1100000) return false;
            length += part * part;
        }
        if (length < 810000000000LL || length > 1210000000000LL) return false;
        for (size_t n = 0; n < i; ++n) if (value.meshes[n] == mesh) return false;
    }
    for (size_t i = 0; i < value.morphs.size(); ++i) {
        const auto& morph = value.morphs[i];
        if (!morph.target || !morph.region || morph.weight < -10000 || morph.weight > 10000) return false;
        for (size_t n = 0; n < i; ++n)
            if (value.morphs[n].target == morph.target && value.morphs[n].region == morph.region) return false;
    }
    return true;
}
inline std::string encode(const Appearance& value, uint32_t sequence) {
    if (!sequence || !valid(value)) return {};
    const auto bytes = 20 + value.meshes.size() * appearanceMeshBytes + value.morphs.size() * appearanceMorphBytes;
    auto data = header(appearanceKind, sequence, static_cast<uint32_t>(bytes));
    data.reserve(headerBytes + bytes);
    put32(data, value.generation); put32(data, value.revision); put32(data, value.body);
    put32(data, static_cast<uint32_t>(value.meshes.size())); put32(data, static_cast<uint32_t>(value.morphs.size()));
    for (const auto& mesh : value.meshes) {
        put32(data, mesh.kind);
        for (uint64_t part : {mesh.resource, mesh.material, mesh.skinning, mesh.parent, mesh.slot, mesh.chunks}) put64(data, part);
        for (int32_t part : mesh.transform) put32(data, std::bit_cast<uint32_t>(part));
    }
    for (const auto& morph : value.morphs) {
        put64(data, morph.target); put64(data, morph.region); put32(data, std::bit_cast<uint32_t>(morph.weight));
    }
    return data;
}
inline bool decode(const std::string& data, Appearance& result, uint32_t& sequence) {
    // Check all counts and the exact frame length before reserving or reading.
    if (data.size() < headerBytes + 20 || data.size() > maxAppearanceBytes) return false;
    const size_t meshCount = get32(data, headerBytes + 12), morphCount = get32(data, headerBytes + 16);
    if (!meshCount || meshCount > maxAppearanceMeshes || morphCount > maxAppearanceMorphs) return false;
    const auto bytes = 20 + meshCount * appearanceMeshBytes + morphCount * appearanceMorphBytes;
    if (!isFrame(data, appearanceKind, bytes) || !get32(data, 8)) return false;
    Appearance value;
    value.generation = get32(data, 16); value.revision = get32(data, 20); value.body = get32(data, 24);
    value.meshes.reserve(meshCount); value.morphs.reserve(morphCount);
    size_t offset = headerBytes + 20;
    for (size_t i = 0; i < meshCount; ++i) {
        AppearanceMesh mesh;
        mesh.kind = get32(data, offset); offset += 4;
        mesh.resource = get64(data, offset); mesh.material = get64(data, offset + 8);
        mesh.skinning = get64(data, offset + 16); mesh.parent = get64(data, offset + 24);
        mesh.slot = get64(data, offset + 32); mesh.chunks = get64(data, offset + 40); offset += 48;
        for (int32_t& part : mesh.transform) { part = std::bit_cast<int32_t>(get32(data, offset)); offset += 4; }
        value.meshes.push_back(mesh);
    }
    for (size_t i = 0; i < morphCount; ++i) {
        value.morphs.push_back({get64(data, offset), get64(data, offset + 8), std::bit_cast<int32_t>(get32(data, offset + 16))});
        offset += appearanceMorphBytes;
    }
    if (!valid(value)) return false;
    result = std::move(value); sequence = get32(data, 8); return true;
}

// This mailbox is deliberately not enabled in the transport yet. Rendering and
// local resource validation must pass the isolated game trial first.
class Appearances {
    mutable std::mutex mutex;
    bool connected = false;
    uint32_t remoteSequence = 0;
    Appearance remote;
public:
    void begin() { std::lock_guard lock(mutex); connected = true; remoteSequence = 0; remote = {}; }
    void end() { std::lock_guard lock(mutex); connected = false; remoteSequence = 0; remote = {}; }
    bool receive(const Appearance& value, uint32_t sequence) {
        if (!sequence || !valid(value)) return false;
        std::lock_guard lock(mutex);
        if (!connected || sequence <= remoteSequence) return false;
        if (value.generation == remote.generation && value.revision <= remote.revision) return false;
        remote = value; remoteSequence = sequence; return true;
    }
    bool sample(uint32_t generation, Appearance& result) const {
        std::lock_guard lock(mutex);
        if (!connected || !generation || generation != remote.generation || !remote.revision) return false;
        result = remote; return true;
    }
};
} // namespace nl::replication
