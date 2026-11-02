#pragma once

#include "Appearance.hpp"
#include <string_view>

namespace nl::replication {
// This is an experimental game-adapter boundary, not a network capability.
// The catalog MUST be populated from locally inspected assets and the actual
// resource type; accepting a peer-supplied catalog would defeat the boundary.
// No path, class, catalog entry or engine handle comes from the appearance frame.
enum class LocalResourceType { mesh, morphTarget };
constexpr size_t maxLocalAppearanceEntries = 4096;
constexpr size_t maxLocalMorphEntries = 2048;
constexpr size_t maxLocalResourcePath = 512;

struct LocalAppearanceEntry {
    uint32_t body = 0, kind = 0;
    uint64_t resource = 0, material = 0, skinning = 0, parent = 0, slot = 0;
    uint64_t allowedChunks = 0;
    std::array<int32_t, 7> transform{0, 0, 0, 0, 0, 0, 1000000};
};
struct LocalMorphEntry { uint32_t body = 0; uint64_t target = 0, region = 0; };

// Resolve and type-check locally first. These path checks are an additional
// conservative policy; they are not a substitute for an engine type check.
// Other mod roots require explicit future local policy and are rejected here.
inline bool canonicalCharacterPath(std::string_view path, LocalResourceType type, std::string& canonical) {
    if (path.empty() || path.size() > maxLocalResourcePath) return false;
    std::string out; out.reserve(path.size());
    for (unsigned char c : path) {
        if (c < 0x20 || c >= 0x7f || c == '/' || c == ':' || c == '*' || c == '?' ||
            c == '"' || c == '<' || c == '>' || c == '|') return false;
        out.push_back(c >= 'A' && c <= 'Z' ? static_cast<char>(c + ('a' - 'A')) : static_cast<char>(c));
    }
    if (!(out.starts_with("base\\characters\\") || out.starts_with("ep1\\characters\\"))) return false;
    if (!out.ends_with(type == LocalResourceType::mesh ? ".mesh" : ".morphtarget")) return false;
    size_t begin = 0;
    while (begin < out.size()) {
        const auto end = out.find('\\', begin);
        const auto segment = std::string_view(out).substr(begin, end == std::string::npos ? out.size() - begin : end - begin);
        if (segment.empty() || segment == "." || segment == "..") return false;
        if (end == std::string::npos) break;
        begin = end + 1;
    }
    canonical = std::move(out); return true;
}

class ApprovedAppearance {
    Appearance value_;
    bool ready_ = false;
    uint64_t catalogEpoch_ = 0;
    friend class LocalAppearanceCatalog;
    friend class BuiltAppearanceCatalog;
public:
    // A policy match only: resources still need to be loaded, checked for
    // failures, and tied to the currently owned proxy before any engine write.
    bool ready() const { return ready_; }
    uint64_t catalogEpoch() const { return catalogEpoch_; }
    const Appearance& value() const { return value_; }
};

class LocalAppearanceCatalog {
    struct MeshRecord { LocalAppearanceEntry entry; std::string canonicalPath; LocalResourceType type; };
    std::vector<MeshRecord> meshes_;
    std::vector<LocalMorphEntry> morphs_;
public:
    // Call only for data derived from locally verified resources and a trusted
    // local descriptor. The resource type is supplied by the local resolver.
    bool addMesh(const LocalAppearanceEntry& entry, std::string_view path, LocalResourceType resolvedType) {
        if (meshes_.size() >= maxLocalAppearanceEntries || entry.body > 1 || !entry.resource ||
            (entry.kind != 1 && entry.kind != 2) ||
            (entry.kind == 1 && resolvedType != LocalResourceType::mesh) ||
            (entry.kind == 2 && resolvedType != LocalResourceType::morphTarget)) return false;
        std::string canonical;
        if (!canonicalCharacterPath(path, resolvedType, canonical)) return false;
        for (const auto& current : meshes_) {
            // Hash collisions or inconsistent local resolution fail closed.
            if (current.entry.resource == entry.resource &&
                (current.canonicalPath != canonical || current.type != resolvedType)) return false;
            const auto& old = current.entry;
            if (old.body == entry.body && old.kind == entry.kind && old.resource == entry.resource &&
                old.material == entry.material && old.skinning == entry.skinning && old.parent == entry.parent &&
                old.slot == entry.slot && old.allowedChunks == entry.allowedChunks && old.transform == entry.transform) return false;
        }
        meshes_.push_back({entry, std::move(canonical), resolvedType}); return true;
    }
    bool addMorph(const LocalMorphEntry& entry) {
        if (morphs_.size() >= maxLocalMorphEntries || entry.body > 1 || !entry.target || !entry.region) return false;
        for (const auto& old : morphs_)
            if (old.body == entry.body && old.target == entry.target && old.region == entry.region) return false;
        morphs_.push_back(entry); return true;
    }
    bool approve(const Appearance& candidate, ApprovedAppearance& output) const {
        // An unsuccessful attempt must never leave an earlier approval usable.
        output = {};
        if (!valid(candidate)) return false;
        for (const auto& mesh : candidate.meshes) {
            bool found = false;
            for (const auto& record : meshes_) {
                const auto& local = record.entry;
                if (candidate.body == local.body && mesh.kind == local.kind && mesh.resource == local.resource &&
                    mesh.material == local.material && mesh.skinning == local.skinning && mesh.parent == local.parent &&
                    mesh.slot == local.slot && mesh.transform == local.transform &&
                    (mesh.chunks & ~local.allowedChunks) == 0) { found = true; break; }
            }
            if (!found) return false;
        }
        for (const auto& morph : candidate.morphs) {
            bool found = false;
            for (const auto& local : morphs_)
                if (candidate.body == local.body && morph.target == local.target && morph.region == local.region) { found = true; break; }
            if (!found) return false;
        }
        ApprovedAppearance approved; approved.value_ = candidate; approved.ready_ = true;
        output = std::move(approved); return true;
    }
};
} // namespace nl::replication
