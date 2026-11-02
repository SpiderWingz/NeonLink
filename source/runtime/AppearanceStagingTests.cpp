#include "AppearanceStaging.hpp"
#include "AppearanceCatalogBuilder.hpp"
#include <cstdlib>
#include <iostream>

using namespace nl::replication;
int checks = 0;
void check(bool result, const char* message) {
    ++checks; if (!result) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}
class FixtureResolver final : public LocalAssetResolver {
public:
    bool resolve(uint64_t hash, ResolvedLocalResource& out) const override {
        if (hash != 22) return false;
        out = {22, "base\\characters\\body\\test.mesh", LocalResourceType::mesh, LocalLoadState::loaded};
        return true;
    }
};
int main() {
    Appearance value; value.generation = 4; value.revision = 1;
    AppearanceMesh mesh; mesh.resource = 22; value.meshes.push_back(mesh);
    LocalAppearanceEntry entry; entry.body = 0; entry.kind = 1; entry.resource = 22;
    LocalAppearanceSnapshot snapshot; snapshot.epoch = 7; snapshot.meshes.push_back(entry);
    BuiltAppearanceCatalog catalog; FixtureResolver resolver;
    check(buildLocalAppearanceCatalog(snapshot, resolver, catalog), "build trusted local catalog");
    ApprovedAppearance approved, empty;
    check(catalog.approve(7, value, approved), "approve initial appearance");
    const auto previousApproval = approved;
    AppearanceStaging staging; AppearanceTicket first, newer;
    check(!staging.stage(approved, 7, first), "unbound proxy rejects request");
    staging.bind(100, 4);
    check(!staging.stage(empty, 7, first), "unapproved request rejected");
    check(!staging.stage(approved, 8, first), "approval cannot be relabeled as a different catalog epoch");
    check(staging.stage(approved, 7, first), "matching approved request staged");
    check(!staging.canApply(first, 100, 4, 7, false), "pending assets cannot apply");
    check(!staging.canApply(first, 101, 4, 7, true), "different proxy rejected");
    check(!staging.canApply(first, 100, 5, 7, true), "different world generation rejected");
    check(!staging.canApply(first, 100, 4, 8, true), "changed catalog epoch rejected");
    check(staging.canApply(first, 100, 4, 7, true), "ready matching proxy accepted");
    check(!staging.finish(first, 100, 4, 7, true), "success without resource authorization rejected");
    check(!staging.authorize(first, 100, 4, 7, false), "unready assets cannot authorize");
    check(!staging.stage(approved, 7, newer), "duplicate pending revision rejected");
    value.revision = 2; check(catalog.approve(7, value, approved), "approve newer appearance");
    check(staging.stage(approved, 7, newer), "newer request staged");
    check(!staging.canApply(first, 100, 4, 7, true), "superseded callback rejected");
    check(!staging.cancel(first), "superseded callback cannot cancel newer request");
    check(!staging.finish(first, 100, 4, 7, true), "superseded completion rejected");
    check(staging.finish(newer, 100, 4, 7, false), "failed attempt clears request");
    check(!staging.canApply(newer, 100, 4, 7, true), "failed request cannot apply later");
    check(!staging.stage(previousApproval, 7, first), "older revision cannot replace failed newer request");
    check(staging.stage(approved, 7, newer), "failed revision can be retried");
    check(staging.authorize(newer, 100, 4, 7, true), "ready request authorizes game-thread application");
    check(staging.finish(newer, 100, 4, 7, true), "successful revision committed");
    check(!staging.stage(approved, 7, first), "committed revision cannot replay");
    value.revision = 3; check(catalog.approve(7, value, approved), "approve revision three");
    check(staging.stage(approved, 7, first), "third revision staged");
    staging.bind(200, 4);
    check(!staging.canApply(first, 100, 4, 7, true), "proxy replacement invalidates callback");
    check(!staging.canApply(first, 200, 4, 7, true), "old ticket cannot target new proxy");
    check(staging.stage(approved, 7, newer), "new proxy can stage appearance");
    check(!staging.canApply(newer, 200, 4, 8, true), "catalog rebuild invalidates pending request");
    check(staging.cancel(newer), "stale catalog request can be cancelled");
    check(!staging.stage(approved, 8, newer), "old approval cannot use new epoch");
    snapshot.epoch = 8; check(buildLocalAppearanceCatalog(snapshot, resolver, catalog), "rebuild local catalog");
    check(catalog.approve(8, value, approved), "approve under fresh epoch");
    check(staging.stage(approved, 8, newer), "cancelled revision can be restaged under a fresh catalog");
    staging.clear();
    check(!staging.canApply(newer, 200, 4, 8, true), "disconnect invalidates callback");
    staging.bind(200, 5);
    check(!staging.stage(approved, 8, first), "old world appearance rejected");
    staging.bind(0, 4);
    check(!staging.stage(approved, 8, first), "zero proxy refused");
    std::cout << checks << " appearance staging checks passed.\n";
}
