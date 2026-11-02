#pragma once

// Experimental value-only format. Not negotiated, sent, received by Runtime,
// or approved by the existing appearance catalog. A valid frame is NOT safe to
// apply to game objects: selectors, types, roles and bindings require a future
// independently populated local policy and game-thread adapter.
#include "Appearance.hpp"

namespace nl::appearance_v2 {
using replication::AppearanceMorph;
constexpr uint32_t schemaVersion = 2;
constexpr auto kind = static_cast<replication::Kind>(5);
constexpr size_t maxMeshes = 96, maxMorphs = 128;
constexpr size_t meshBytes = 92, morphBytes = 20, prefixBytes = 24;
constexpr size_t maxFrameBytes = replication::headerBytes + prefixBytes + maxMeshes * meshBytes + maxMorphs * morphBytes;

// Role values are provisional protocol identifiers, not engine types or names.
enum class MeshKind : uint32_t { skinned = 1, morph = 2, garment = 3 };
enum class MeshRole : uint32_t { body = 1, head = 2, hair = 3, clothing = 4, cyberware = 5, other = 6 };

struct Mesh {
    uint32_t kind = 1, role = 6;
    uint64_t component = 0, resource = 0, material = 0, skinning = 0, parent = 0, slot = 0, chunks = 0;
    std::array<int32_t, 7> transform{0, 0, 0, 0, 0, 0, 1000000};
    bool operator==(const Mesh&) const = default;
};
struct Appearance {
    uint32_t generation = 0, revision = 0, body = 0;
    std::vector<Mesh> meshes;
    std::vector<AppearanceMorph> morphs;
    bool operator==(const Appearance&) const = default;
};

inline bool valid(const Appearance& value) {
    if (!value.generation || !value.revision || value.body > 1 || value.meshes.empty() ||
        value.meshes.size() > maxMeshes || value.morphs.size() > maxMorphs) return false;
    for (size_t i = 0; i < value.meshes.size(); ++i) {
        const auto& mesh = value.meshes[i];
        if (mesh.kind < 1 || mesh.kind > 3 || mesh.role < 1 || mesh.role > 6 || !mesh.component || !mesh.resource) return false;
        for (size_t j = 0; j < 3; ++j) if (mesh.transform[j] < -100000 || mesh.transform[j] > 100000) return false;
        int64_t length = 0;
        for (size_t j = 3; j < 7; ++j) {
            int64_t part = mesh.transform[j];
            if (part < -1100000 || part > 1100000) return false;
            length += part * part;
        }
        if (length < 810000000000LL || length > 1210000000000LL) return false;
        // Identical component names can occur legitimately. Reject only an
        // identical full descriptor, not a matching component selector alone.
        for (size_t j = 0; j < i; ++j) if (value.meshes[j] == mesh) return false;
    }
    for (size_t i = 0; i < value.morphs.size(); ++i) {
        const auto& morph = value.morphs[i];
        if (!morph.target || !morph.region || morph.weight < -10000 || morph.weight > 10000) return false;
        for (size_t j = 0; j < i; ++j)
            if (value.morphs[j].target == morph.target && value.morphs[j].region == morph.region) return false;
    }
    return true;
}

inline std::string encode(const Appearance& value, uint32_t sequence) {
    using namespace replication;
    if (!sequence || !valid(value)) return {};
    const size_t bytes = prefixBytes + value.meshes.size() * meshBytes + value.morphs.size() * morphBytes;
    auto frame = header(kind, sequence, static_cast<uint32_t>(bytes));
    frame.reserve(headerBytes + bytes);
    put32(frame, schemaVersion); put32(frame, value.generation); put32(frame, value.revision); put32(frame, value.body);
    put32(frame, static_cast<uint32_t>(value.meshes.size())); put32(frame, static_cast<uint32_t>(value.morphs.size()));
    for (const auto& mesh : value.meshes) {
        put32(frame, mesh.kind); put32(frame, mesh.role);
        for (uint64_t item : {mesh.component, mesh.resource, mesh.material, mesh.skinning, mesh.parent, mesh.slot, mesh.chunks}) put64(frame, item);
        for (int32_t item : mesh.transform) put32(frame, std::bit_cast<uint32_t>(item));
    }
    for (const auto& morph : value.morphs) {
        put64(frame, morph.target); put64(frame, morph.region); put32(frame, std::bit_cast<uint32_t>(morph.weight));
    }
    return frame;
}

inline bool decode(const std::string& frame, Appearance& result, uint32_t& sequence) {
    using namespace replication;
    if (frame.size() < headerBytes + prefixBytes || frame.size() > maxFrameBytes) return false;
    const size_t meshCount = get32(frame, headerBytes + 16), morphCount = get32(frame, headerBytes + 20);
    if (!meshCount || meshCount > maxMeshes || morphCount > maxMorphs) return false;
    const size_t bytes = prefixBytes + meshCount * meshBytes + morphCount * morphBytes;
    if (!isFrame(frame, kind, bytes) || !get32(frame, 8) || get32(frame, headerBytes) != schemaVersion) return false;
    Appearance value;
    value.generation = get32(frame, 20); value.revision = get32(frame, 24); value.body = get32(frame, 28);
    value.meshes.reserve(meshCount); value.morphs.reserve(morphCount);
    size_t offset = headerBytes + prefixBytes;
    for (size_t i = 0; i < meshCount; ++i) {
        Mesh mesh; mesh.kind = get32(frame, offset); mesh.role = get32(frame, offset + 4); offset += 8;
        mesh.component = get64(frame, offset); mesh.resource = get64(frame, offset + 8);
        mesh.material = get64(frame, offset + 16); mesh.skinning = get64(frame, offset + 24);
        mesh.parent = get64(frame, offset + 32); mesh.slot = get64(frame, offset + 40);
        mesh.chunks = get64(frame, offset + 48); offset += 56;
        for (int32_t& item : mesh.transform) { item = std::bit_cast<int32_t>(get32(frame, offset)); offset += 4; }
        value.meshes.push_back(mesh);
    }
    for (size_t i = 0; i < morphCount; ++i) {
        value.morphs.push_back({get64(frame, offset), get64(frame, offset + 8), std::bit_cast<int32_t>(get32(frame, offset + 16))});
        offset += morphBytes;
    }
    if (!valid(value)) return false;
    result = std::move(value); sequence = get32(frame, 8); return true;
}
} // namespace nl::appearance_v2
