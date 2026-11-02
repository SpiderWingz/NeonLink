#include "AppearancePolicy.hpp"
#include <cstdlib>
#include <iostream>
#include <random>

using namespace nl::replication;
int checks = 0;
void check(bool value, const char* message) {
    ++checks; if (!value) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}
int main() {
    std::string canonical = "unchanged";
    check(canonicalCharacterPath("BASE\\Characters\\Body\\V.Mesh", LocalResourceType::mesh, canonical) &&
          canonical == "base\\characters\\body\\v.mesh", "case-normalized local character resource");
    for (const std::string& path : std::vector<std::string>{"base\\characters\\..\\secret.mesh", "base\\characters\\.\\v.mesh",
             "base\\characters\\\\v.mesh", "base/characters/v.mesh", "C:\\base\\characters\\v.mesh",
             "base\\characters\\v.mesh:stream", "base\\other\\v.mesh", "base\\characters\\v.morphtarget",
             "base\\characters\\bad?.mesh", "base\\characters\\v.mesh\\", std::string(513,'a')}) {
        canonical = "unchanged";
        check(!canonicalCharacterPath(path, LocalResourceType::mesh, canonical) && canonical == "unchanged", "unsafe local path rejected without changing output");
    }
    check(canonicalCharacterPath("ep1\\characters\\head\\v.morphtarget", LocalResourceType::morphTarget, canonical), "EP1 morph path accepted");

    Appearance appearance; appearance.generation = appearance.revision = 1;
    AppearanceMesh mesh; mesh.kind = 1; mesh.resource = 10; mesh.material = 11;
    mesh.skinning = 12; mesh.parent = 13; mesh.slot = 14; mesh.chunks = 0x5;
    appearance.meshes.push_back(mesh); appearance.morphs.push_back({20, 21, 300});
    LocalAppearanceEntry entry{0, 1, 10, 11, 12, 13, 14, 0x7};
    LocalAppearanceCatalog catalog; ApprovedAppearance approved;
    check(!catalog.approve(appearance, approved) && !approved.ready(), "empty catalog rejects a valid frame");
    check(!catalog.addMesh(entry, "base\\characters\\body\\v.mesh", LocalResourceType::morphTarget), "resource type mismatch rejected");
    check(!catalog.addMesh(entry, "base\\other\\v.mesh", LocalResourceType::mesh), "outside character root rejected");
    check(catalog.addMesh(entry, "base\\characters\\body\\v.mesh", LocalResourceType::mesh), "trusted local mesh registered");
    check(!catalog.addMesh(entry, "BASE\\CHARACTERS\\BODY\\V.MESH", LocalResourceType::mesh), "duplicate local tuple rejected");
    auto collision = entry; collision.material = 15;
    check(!catalog.addMesh(collision, "base\\characters\\body\\different.mesh", LocalResourceType::mesh), "ambiguous local resource hash rejected");
    check(!catalog.approve(appearance, approved) && !approved.ready(), "unknown morph prevents all application");
    check(catalog.addMorph({0,20,21}) && !catalog.addMorph({0,20,21}), "only unique local morph pair is admitted");
    check(catalog.approve(appearance, approved) && approved.ready() && approved.value() == appearance, "complete verified appearance approved");

    auto bad = appearance; bad.body = 1;
    check(!catalog.approve(bad, approved) && !approved.ready(), "rejection clears previous approval");
    bad = appearance; bad.meshes[0].kind = 2; check(!catalog.approve(bad,approved), "mesh kind cannot be substituted");
    bad = appearance; bad.meshes[0].resource = 999; check(!catalog.approve(bad,approved), "unknown resource rejected");
    bad = appearance; bad.meshes[0].material = 99; check(!catalog.approve(bad,approved), "unknown material rejected");
    bad = appearance; bad.meshes[0].skinning = 99; check(!catalog.approve(bad,approved), "unknown skin binding rejected");
    bad = appearance; bad.meshes[0].parent = 99; check(!catalog.approve(bad,approved), "unknown parent binding rejected");
    bad = appearance; bad.meshes[0].slot = 99; check(!catalog.approve(bad,approved), "unknown slot rejected");
    bad = appearance; bad.meshes[0].chunks = 8; check(!catalog.approve(bad,approved), "unapproved chunk rejected");
    bad = appearance; bad.meshes[0].transform[0] = 1; check(!catalog.approve(bad,approved), "unapproved local position rejected");
    bad = appearance; bad.meshes[0].transform[6] = 999999; check(!catalog.approve(bad,approved), "unapproved local orientation rejected");
    bad = appearance; bad.morphs[0].region = 22; check(!catalog.approve(bad,approved), "unknown morph region rejected");
    bad = appearance; bad.morphs[0].target = 22; check(!catalog.approve(bad,approved), "unknown morph target rejected");
    bad = appearance; bad.meshes[0].transform[0] = INT32_MAX; check(!catalog.approve(bad,approved), "wire bounds checked before catalog");
    bad = appearance; bad.meshes.push_back(mesh); bad.meshes.back().resource = 99;
    check(!catalog.approve(bad,approved) && !approved.ready(), "one unknown mesh prevents partial approval");

    std::mt19937_64 random(0x5245444c494e45ULL); bool allRejected = true;
    for (int i = 0; i < 10000; ++i) {
        bad = appearance; bad.meshes[0].resource = random() | (uint64_t{1} << 63);
        allRejected &= !catalog.approve(bad, approved);
    }
    check(allRejected && !approved.ready(), "ten thousand unknown resource selectors fail closed");
    std::cout << checks << " appearance local-policy checks passed.\n";
}
