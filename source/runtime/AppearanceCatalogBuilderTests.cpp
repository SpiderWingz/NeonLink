#include "AppearanceCatalogBuilder.hpp"
#include <cstdlib>
#include <iostream>
#include <map>

using namespace nl::replication;
static int checks = 0;
static void check(bool value, const char* message) {
    ++checks;
    if (!value) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}

class FixtureResolver final : public LocalAssetResolver {
public:
    std::map<uint64_t, ResolvedLocalResource> assets;
    bool resolve(uint64_t hash, ResolvedLocalResource& out) const override {
        auto it = assets.find(hash);
        if (it == assets.end()) return false;
        out = it->second; return true;
    }
};

int main() {
    FixtureResolver resolver;
    resolver.assets[10] = {10, "base\\characters\\body\\v.mesh", LocalResourceType::mesh, LocalLoadState::loaded};
    resolver.assets[30] = {30, "ep1\\characters\\head\\v.morphtarget", LocalResourceType::morphTarget, LocalLoadState::loaded};
    LocalAppearanceEntry first{0, 1, 10, 11, 12, 13, 14, 7};
    LocalAppearanceEntry second{0, 2, 30, 31, 32, 33, 34, 15};
    LocalAppearanceSnapshot snapshot{55, {first, second}, {{0, 20, 21}}};
    Appearance candidate; candidate.generation = 7; candidate.revision = 3;
    AppearanceMesh mesh; mesh.resource = 10; mesh.material = 11; mesh.skinning = 12; mesh.parent = 13; mesh.slot = 14; mesh.chunks = 5;
    candidate.meshes.push_back(mesh); candidate.morphs.push_back({20, 21, 100});
    BuiltAppearanceCatalog built; ApprovedAppearance approved;
    check(!built.ready(55) && !built.approve(55, candidate, approved), "empty build cannot approve");
    check(buildLocalAppearanceCatalog(snapshot, resolver, built) && built.ready(55), "verified local snapshot builds");
    check(built.approve(55, candidate, approved) && approved.ready(), "catalog approves matching selectors");
    check(!built.ready(0) && !built.ready(56), "zero or stale local epoch rejected");
    check(!built.approve(56, candidate, approved) && !approved.ready(), "stale epoch clears previous approval");
    check(built.approve(55, candidate, approved), "valid epoch approves after prior failure");

    auto bad = snapshot; bad.epoch = 0;
    check(!buildLocalAppearanceCatalog(bad, resolver, built) && !built.ready(55), "zero epoch invalidates old build");
    check(!buildLocalAppearanceCatalog(snapshot, resolver, built), "previously successful epoch cannot be reused after failure");
    BuiltAppearanceCatalog scratch;
    bad = snapshot; bad.meshes.clear(); check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "empty mesh snapshot rejected");
    bad = snapshot; bad.meshes.resize(maxLocalAppearanceEntries + 1); check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "mesh count bounded before resolver");
    bad = snapshot; bad.morphs.resize(maxLocalMorphEntries + 1); check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "morph count bounded before resolver");
    bad = snapshot; bad.meshes[1].resource = 999; check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "missing asset rejected");
    for (auto state : {LocalLoadState::missing, LocalLoadState::pending, LocalLoadState::failed}) {
        auto incomplete = resolver; incomplete.assets[30].state = state;
        check(!buildLocalAppearanceCatalog(snapshot, incomplete, scratch), "asset must be loaded before approval");
    }
    auto wrong = resolver; wrong.assets[30].hash = 31;
    check(!buildLocalAppearanceCatalog(snapshot, wrong, scratch), "resolved hash must match request");
    wrong = resolver; wrong.assets[30].type = LocalResourceType::mesh;
    check(!buildLocalAppearanceCatalog(snapshot, wrong, scratch), "actual loaded class must match mesh kind");
    wrong = resolver; wrong.assets[30].path = "base\\other\\v.morphtarget";
    check(!buildLocalAppearanceCatalog(snapshot, wrong, scratch), "local path outside allowed character roots rejected");
    bad = snapshot; bad.meshes.push_back(first);
    check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "duplicate local entry rejects entire build");
    bad = snapshot; bad.morphs.push_back(bad.morphs[0]);
    check(!buildLocalAppearanceCatalog(bad, resolver, scratch), "duplicate morph selector rejects entire build");
    snapshot.epoch = 56;
    check(buildLocalAppearanceCatalog(snapshot, resolver, built), "newer valid snapshot rebuilds after failure");
    auto modified = candidate; modified.meshes[0].material = 999;
    check(!built.approve(56, modified, approved) && !approved.ready(), "unlisted peer selector is rejected");
    bad = snapshot; bad.epoch = 57;
    check(buildLocalAppearanceCatalog(bad, resolver, built) && !built.ready(56) && built.ready(57), "replacement invalidates old epoch");
    check(!built.approve(56, candidate, approved) && built.approve(57, candidate, approved), "new epoch required after refresh");
    check(!buildLocalAppearanceCatalog(bad, resolver, built) && !built.ready(57), "same epoch rebuild invalidates catalog");
    bad.epoch = 56;
    check(!buildLocalAppearanceCatalog(bad, resolver, built), "older epoch cannot return after invalidation");
    std::cout << checks << " appearance catalog builder checks passed.\n";
}
