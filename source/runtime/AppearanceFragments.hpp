#pragma once

// Experimental plaintext framing only: NOT enabled or negotiated in Runtime.
// Authentication/encryption must happen before receive(), and a decoded V2
// appearance still requires independent local policy and proxy validation.
#include "AppearanceV2.hpp"
#include <bitset>

namespace nl::appearance_fragments {
constexpr size_t maxCiphertext = 512;
// libsodium secretstream_xchacha20poly1305_ABYTES is 17. The future transport
// integration MUST static_assert this against its installed sodium header.
constexpr size_t secretstreamOverhead = 17;
constexpr size_t maxPlaintext = maxCiphertext - secretstreamOverhead;
constexpr size_t prefixBytes = 16; // transfer ID, total bytes, count, index
constexpr size_t chunkBytes = maxPlaintext - replication::headerBytes - prefixBytes;
constexpr size_t maxChunks = (appearance_v2::maxFrameBytes + chunkBytes - 1) / chunkBytes;
constexpr size_t minFrameBytes = replication::headerBytes + appearance_v2::prefixBytes + appearance_v2::meshBytes;
constexpr uint64_t timeoutMilliseconds = 5000;
constexpr auto chunkKind = static_cast<replication::Kind>(6);
static_assert(chunkBytes == 463 && maxChunks == 25);

// A transfer ID is nonzero and strictly increasing within one encrypted
// session; reconnect must reset both sender and receiver. Wrap is not allowed.
// Changes to the transfer ID or inner frame sequence must be session-local.
inline bool fragment(const std::string& frame, uint32_t transferId, std::vector<std::string>& output) {
    appearance_v2::Appearance value; uint32_t sequence = 0;
    if (!transferId || !appearance_v2::decode(frame, value, sequence)) return false;
    const size_t count = (frame.size() + chunkBytes - 1) / chunkBytes;
    if (!count || count > maxChunks) return false;
    std::vector<std::string> parts; parts.reserve(count);
    for (size_t i = 0; i < count; ++i) {
        const auto offset = i * chunkBytes;
        const auto length = (std::min)(chunkBytes, frame.size() - offset);
        auto part = replication::header(chunkKind, static_cast<uint32_t>(i + 1), static_cast<uint32_t>(prefixBytes + length));
        replication::put32(part, transferId); replication::put32(part, static_cast<uint32_t>(frame.size()));
        replication::put32(part, static_cast<uint32_t>(count)); replication::put32(part, static_cast<uint32_t>(i));
        part.append(frame.data() + offset, length); parts.push_back(std::move(part));
    }
    output = std::move(parts); return true;
}

enum class Result { rejected, incomplete, complete };

// One bounded in-flight transfer per authenticated peer. The deadline is
// absolute from the first valid chunk; duplicate or rejected packets do not
// extend it. The completed output remains unchanged until full V2 validation.
// Call reset() on disconnect. This object is single-thread-owned.
class Receiver {
    uint32_t lastSeen_ = 0, active_ = 0;
    uint64_t started_ = 0;
    size_t total_ = 0, count_ = 0, received_ = 0;
    std::bitset<maxChunks> seen_;
    std::string buffer_;
    void clearActive() { active_ = 0; started_ = 0; total_ = count_ = received_ = 0; seen_.reset(); buffer_.clear(); }
public:
    void reset() { clearActive(); lastSeen_ = 0; }
    Result receive(const std::string& part, uint64_t now, std::string& completed) {
        using namespace replication;
        if (part.size() < headerBytes + prefixBytes + 1 || part.size() > maxPlaintext) return Result::rejected;
        const size_t payload = part.size() - headerBytes;
        if (!isFrame(part, chunkKind, payload)) return Result::rejected;
        const uint32_t id = get32(part, headerBytes), total = get32(part, headerBytes + 4);
        const uint32_t count = get32(part, headerBytes + 8), index = get32(part, headerBytes + 12);
        if (!id || total < minFrameBytes || total > appearance_v2::maxFrameBytes || !count ||
            count != (total + chunkBytes - 1) / chunkBytes || count > maxChunks || index >= count ||
            get32(part, 8) != index + 1) return Result::rejected;
        const size_t offset = static_cast<size_t>(index) * chunkBytes;
        const size_t expected = (std::min)(chunkBytes, static_cast<size_t>(total) - offset);
        if (part.size() != headerBytes + prefixBytes + expected) return Result::rejected;

        if (active_ && (now < started_ || now - started_ > timeoutMilliseconds)) clearActive();
        if (id != active_) {
            if (id <= lastSeen_) return Result::rejected;
            clearActive(); active_ = id; lastSeen_ = id; started_ = now; total_ = total; count_ = count;
            buffer_.resize(total_);
        }
        if (total_ != total || count_ != count || seen_.test(index)) return Result::rejected;
        buffer_.replace(offset, expected, part, headerBytes + prefixBytes, expected);
        seen_.set(index); ++received_;
        if (received_ != count_) return Result::incomplete;

        appearance_v2::Appearance value; uint32_t sequence = 0;
        if (!appearance_v2::decode(buffer_, value, sequence)) { clearActive(); return Result::rejected; }
        completed = std::move(buffer_); clearActive(); return Result::complete;
    }
};
} // namespace nl::appearance_fragments
