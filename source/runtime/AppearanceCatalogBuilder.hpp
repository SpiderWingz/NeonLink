#pragma once

#include "AppearancePolicy.hpp"

namespace nl::replication {
// An input to this builder is a snapshot of locally observed, trusted
// character-asset metadata. It must never be populated from a network frame,
// peer-provided path, or a caller's guess about resource type or bindings.
// This interface deliberately has no RED4ext implementation yet.
struct LocalAppearanceSnapshot {
    uint64_t epoch = 0; // Monotonically changed by the local owner on invalidation.
    std::vector<LocalAppearanceEntry> meshes;
    std::vector<LocalMorphEntry> morphs;
};

enum class LocalLoadState { missing, pending, failed, loaded };
struct ResolvedLocalResource {
    uint64_t hash = 0;
    std::string path;
    LocalResourceType type = LocalResourceType::mesh;
    LocalLoadState state = LocalLoadState::missing;
};

// Implementations must use an independently trusted LOCAL engine depot to
// resolve the hash, inspect the loaded object's actual class, and return its
// canonical local depot path. Never derive these facts from a peer descriptor.
class LocalAssetResolver {
public:
    virtual ~LocalAssetResolver() = default;
    virtual bool resolve(uint64_t hash, ResolvedLocalResource& output) const = 0;
};

class BuiltAppearanceCatalog {
    LocalAppearanceCatalog catalog_;
    uint64_t epoch_ = 0;
    friend bool buildLocalAppearanceCatalog(const LocalAppearanceSnapshot&, const LocalAssetResolver&, BuiltAppearanceCatalog&);
public:
    bool ready(uint64_t expectedEpoch) const { return expectedEpoch && expectedEpoch == epoch_; }
    bool approve(uint64_t expectedEpoch, const Appearance& appearance, ApprovedAppearance& output) const {
        if (!ready(expectedEpoch)) { output = {}; return false; }
        if (!catalog_.approve(appearance, output)) return false;
        output.catalogEpoch_ = epoch_;
        return true;
    }
};

// Transactional construction: the old result is invalidated immediately, and
// the new catalog becomes available only if every resource and selector in the
// bounded, trusted local snapshot passes. This does not load assets or mutate a
// game entity. A future game-thread adapter must re-check the epoch, peer's
// character generation, and locally owned proxy identity at application time.
inline bool buildLocalAppearanceCatalog(const LocalAppearanceSnapshot& snapshot,
                                        const LocalAssetResolver& resolver,
                                        BuiltAppearanceCatalog& output) {
    output = {};
    if (!snapshot.epoch || snapshot.meshes.empty() ||
        snapshot.meshes.size() > maxLocalAppearanceEntries || snapshot.morphs.size() > maxLocalMorphEntries) return false;
    LocalAppearanceCatalog building;
    for (const auto& mesh : snapshot.meshes) {
        ResolvedLocalResource resolved;
        if (!resolver.resolve(mesh.resource, resolved) || resolved.state != LocalLoadState::loaded ||
            resolved.hash != mesh.resource || !building.addMesh(mesh, resolved.path, resolved.type)) return false;
    }
    for (const auto& morph : snapshot.morphs) if (!building.addMorph(morph)) return false;
    BuiltAppearanceCatalog completed;
    completed.catalog_ = std::move(building);
    completed.epoch_ = snapshot.epoch;
    output = std::move(completed);
    return true;
}
} // namespace nl::replication
