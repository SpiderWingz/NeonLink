#include "AppearanceFragments.hpp"
#include <sodium.h>
#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <random>

namespace chunks = nl::appearance_fragments;
namespace v2 = nl::appearance_v2;
namespace wire = nl::replication;
static_assert(crypto_secretstream_xchacha20poly1305_ABYTES == chunks::secretstreamOverhead);
int checks = 0;
void check(bool ok, const char* description) {
    ++checks; if (!ok) { std::cerr << "FAIL: " << description << '\n'; std::exit(1); }
}
void replace32(std::string& part, size_t offset, uint32_t value) {
    std::string replacement; wire::put32(replacement, value); part.replace(offset, 4, replacement);
}
int main() {
    v2::Appearance value; value.generation = value.revision = 1;
    v2::Mesh mesh; mesh.component = 1; mesh.resource = 1;
    value.meshes.push_back(mesh);
    const auto small = v2::encode(value, 1);
    std::vector<std::string> parts = {"unchanged"};
    check(!chunks::fragment(small, 0, parts) && parts == std::vector<std::string>{"unchanged"}, "zero transfer ID rejected without mutation");
    check(!chunks::fragment("invalid", 1, parts) && parts == std::vector<std::string>{"unchanged"}, "invalid appearance rejected without mutation");
    check(chunks::fragment(small, 1, parts) && parts.size() == 1, "minimum valid appearance becomes one chunk");
    check(parts[0].size() + chunks::secretstreamOverhead <= chunks::maxCiphertext, "ciphertext fits existing 512-byte bound");
    chunks::Receiver receiver; std::string output = "unchanged";
    check(receiver.receive(parts[0], 10, output) == chunks::Result::complete && output == small, "single-chunk valid appearance completes");
    output = "unchanged";
    check(receiver.receive(parts[0], 11, output) == chunks::Result::rejected && output == "unchanged", "completed transfer cannot replay");
    receiver.reset();
    check(receiver.receive(parts[0], 12, output) == chunks::Result::complete && output == small, "reconnect resets transfer sequence");

    value.meshes.clear();
    for (size_t i = 0; i < v2::maxMeshes; ++i) { mesh.component = i + 1; mesh.resource = i + 1; value.meshes.push_back(mesh); }
    for (size_t i = 0; i < v2::maxMorphs; ++i) value.morphs.push_back({i + 1, i + 2, 1});
    auto maximum = v2::encode(value, 9);
    check(maximum.size() == v2::maxFrameBytes && chunks::fragment(maximum, 42, parts) && parts.size() == chunks::maxChunks, "maximum frame is fragmented into at most 25 chunks");
    bool sizes = true;
    for (const auto& part : parts) sizes &= part.size() + chunks::secretstreamOverhead <= chunks::maxCiphertext;
    check(sizes, "every encrypted maximum-frame chunk fits bound");
    receiver.reset(); output = "unchanged";
    check(receiver.receive(parts.back(), 100, output) == chunks::Result::incomplete && output == "unchanged", "last chunk may arrive first without partial output");
    check(receiver.receive(parts.back(), 101, output) == chunks::Result::rejected && output == "unchanged", "duplicate fragment rejected");
    for (size_t i = parts.size() - 1; i-- > 1;) {
        check(receiver.receive(parts[i], 102, output) == chunks::Result::incomplete && output == "unchanged", "out-of-order fragment held without partial output");
    }
    check(receiver.receive(parts[0], 103, output) == chunks::Result::complete && output == maximum, "all out-of-order fragments reassemble exactly");
    check(receiver.receive(parts[0], 104, output) == chunks::Result::rejected, "completed multi-chunk replay rejected");

    for (const auto [offset, bad] : std::vector<std::pair<size_t,uint32_t>>{
         {0,0}, {4,0}, {8,0}, {8,2}, {12,0}, {16,0}, {20,0}, {20,UINT32_MAX}, {24,0}, {24,UINT32_MAX}, {28,UINT32_MAX}}) {
        auto malformed = parts[0]; replace32(malformed, offset, bad);
        chunks::Receiver clean; output = "sentinel";
        check(clean.receive(malformed, 0, output) == chunks::Result::rejected && output == "sentinel", "malformed fragment rejected before allocation/output");
    }
    chunks::Receiver clean; output = "sentinel";
    check(clean.receive(parts[0] + "x", 0, output) == chunks::Result::rejected, "overlong fragment rejected");
    check(clean.receive(parts[0].substr(0, parts[0].size()-1), 0, output) == chunks::Result::rejected, "truncated fragment rejected");
    bool truncations = true;
    for (size_t i = 0; i < parts[0].size(); ++i) {
        chunks::Receiver candidate; std::string saved = "sentinel";
        truncations &= candidate.receive(parts[0].substr(0, i), 0, saved) == chunks::Result::rejected && saved == "sentinel";
    }
    check(truncations, "every truncation rejected without partial output");
    check(clean.receive(std::string(496, 'x'), 0, output) == chunks::Result::rejected, "plaintext exceeding ciphertext budget rejected");
    check(clean.receive(parts[0], 100, output) == chunks::Result::incomplete, "timeout test starts transfer");
    check(clean.receive(parts[1], 5101, output) == chunks::Result::rejected && output == "sentinel", "expired transfer cannot resume or produce output");
    chunks::Receiver boundary;
    check(boundary.receive(parts[0], 100, output) == chunks::Result::incomplete &&
          boundary.receive(parts[1], 5100, output) == chunks::Result::incomplete, "deadline includes exact boundary");
    check(boundary.receive(parts[1], 5100, output) == chunks::Result::rejected &&
          boundary.receive(parts[2], 5101, output) == chunks::Result::rejected, "duplicate and progress never extend deadline");
    std::vector<std::string> newer;
    check(chunks::fragment(maximum, 43, newer), "newer transfer created");
    check(clean.receive(newer[1], 5102, output) == chunks::Result::incomplete, "newer transfer starts after timeout");
    check(clean.receive(parts[1], 5103, output) == chunks::Result::rejected, "older transfer cannot replace newer one");
    check(clean.receive(newer[0], 5101, output) == chunks::Result::rejected, "backward local clock cancels transfer");
    check(clean.receive(newer[0], 5104, output) == chunks::Result::rejected, "cancelled ID cannot replay after clock rollback");
    check(chunks::fragment(maximum, 44, newer), "later transfer created");
    auto corrupt = newer[0];
    // Alter the inner V2 schema while retaining a structurally valid chunk.
    corrupt[wire::headerBytes + chunks::prefixBytes + wire::headerBytes + 3] = 3;
    check(clean.receive(corrupt, 6000, output) == chunks::Result::incomplete, "corrupted payload stored only as an incomplete frame");
    check(clean.receive(newer[1], 6001, output) == chunks::Result::incomplete, "middle data held without output");
    for (size_t i = 2; i < newer.size(); ++i) {
        const auto status = clean.receive(newer[i], 6002, output);
        check(i + 1 == newer.size() ? status == chunks::Result::rejected : status == chunks::Result::incomplete, "invalid inner frame rejected without completion");
    }
    check(output == "sentinel", "invalid inner frame never overwrites output");
    std::vector<std::string> superseding;
    check(chunks::fragment(maximum, 45, superseding), "superseding transfer created");
    chunks::Receiver active;
    check(active.receive(newer[0], 6000, output) == chunks::Result::incomplete &&
          active.receive(superseding[1], 6001, output) == chunks::Result::incomplete &&
          active.receive(newer[1], 6002, output) == chunks::Result::rejected, "newer transfer supersedes old incomplete transfer");
    chunks::Receiver wrap; std::vector<std::string> finalId, firstId;
    check(chunks::fragment(small, UINT32_MAX, finalId) && chunks::fragment(small, 1, firstId), "transfer edge IDs encode");
    check(wrap.receive(finalId[0], 1, output) == chunks::Result::complete &&
          wrap.receive(firstId[0], 2, output) == chunks::Result::rejected, "transfer ID cannot wrap in session");
    wrap.reset(); check(wrap.receive(firstId[0], 3, output) == chunks::Result::complete, "new session resets transfer high-water mark");
    std::mt19937 random(42); bool sane = true;
    for (int i = 0; i < 10000; ++i) {
        std::string data(random() % 550, '\0'); for (auto& byte : data) byte = static_cast<char>(random());
        chunks::Receiver candidate; std::string result = "sentinel";
        sane &= candidate.receive(data, i, result) == chunks::Result::rejected && result == "sentinel";
    }
    check(sane, "ten thousand random fragments rejected safely");
    std::cout << checks << " appearance fragmentation checks passed.\n";
}
