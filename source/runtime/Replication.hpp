#pragma once

// Gameplay wire data has no engine pointers, resource paths, entity IDs, or
// executable commands. Decode and validate before exposing it to the game.
#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>

namespace nl::replication {
constexpr uint8_t version = 2;
constexpr unsigned tickMilliseconds = 50;
constexpr uint64_t staleMilliseconds = 1000;
constexpr uint64_t interpolationMilliseconds = 100;
constexpr size_t headerBytes = 16;
enum class Kind : uint8_t { hello = 1, pose = 2, profile = 3 };
struct Pose {
    uint32_t generation = 0;
    bool active = false;
    uint64_t time = 0;
    std::array<float, 3> position{};
    std::array<float, 4> rotation{0, 0, 0, 1};
};
inline void put32(std::string& data, uint32_t value) {
    for (int shift = 24; shift >= 0; shift -= 8) data += static_cast<char>(value >> shift);
}
inline uint32_t get32(const std::string& data, size_t offset) {
    uint32_t value = 0;
    for (unsigned i = 0; i < 4; ++i) value = (value << 8) | static_cast<unsigned char>(data[offset + i]);
    return value;
}
inline bool valid(const Pose& pose) {
    for (float value : pose.position) if (!std::isfinite(value) || std::abs(value) > 100000.0f) return false;
    float length = 0;
    for (float value : pose.rotation) { if (!std::isfinite(value)) return false; length += value * value; }
    return std::isfinite(length) && length >= 0.81f && length <= 1.21f && (!pose.active || pose.generation != 0);
}
inline void normalize(Pose& pose) {
    float length = 0;
    for (float value : pose.rotation) length += value * value;
    length = std::sqrt(length);
    for (float& value : pose.rotation) value /= length;
}
inline std::string header(Kind kind, uint32_t sequence, uint32_t bytes) {
    std::string data("NLRP", 4);
    data += static_cast<char>(version); data += static_cast<char>(kind);
    data += '\0'; data += '\0'; put32(data, sequence); put32(data, bytes);
    return data;
}
inline bool isFrame(const std::string& data, Kind kind, size_t bytes) {
    return data.size() == headerBytes + bytes && data.compare(0, 4, "NLRP") == 0 &&
        static_cast<uint8_t>(data[4]) == version && static_cast<uint8_t>(data[5]) == static_cast<uint8_t>(kind) &&
        data[6] == 0 && data[7] == 0 && get32(data, 12) == bytes;
}
inline std::string hello() {
    auto data = header(Kind::hello, 0, 8);
    put32(data, 3); put32(data, 1000 / tickMilliseconds);
    return data;
}
inline bool acceptHello(const std::string& data) {
    return isFrame(data, Kind::hello, 8) && get32(data, 8) == 0 && get32(data, 16) == 3 && get32(data, 20) == 20;
}
inline std::string encode(const Pose& pose, uint32_t sequence) {
    if (!valid(pose) || sequence == 0) return {};
    auto data = header(Kind::pose, sequence, 44);
    put32(data, pose.generation); put32(data, pose.active ? 1 : 0);
    put32(data, static_cast<uint32_t>(pose.time >> 32)); put32(data, static_cast<uint32_t>(pose.time));
    for (float value : pose.position) put32(data, std::bit_cast<uint32_t>(value));
    for (float value : pose.rotation) put32(data, std::bit_cast<uint32_t>(value));
    return data;
}
inline bool decode(const std::string& data, Pose& result, uint32_t& sequence) {
    if (!isFrame(data, Kind::pose, 44) || get32(data, 8) == 0 || get32(data, 20) > 1) return false;
    Pose pose;
    pose.generation = get32(data, 16); pose.active = get32(data, 20) == 1;
    pose.time = (static_cast<uint64_t>(get32(data, 24)) << 32) | get32(data, 28);
    for (unsigned i = 0; i < 3; ++i) pose.position[i] = std::bit_cast<float>(get32(data, 32 + i * 4));
    for (unsigned i = 0; i < 4; ++i) pose.rotation[i] = std::bit_cast<float>(get32(data, 44 + i * 4));
    if (!valid(pose)) return false;
    normalize(pose); result = pose; sequence = get32(data, 8); return true;
}

// A bounded mailbox is the only crossing between networking and gameplay.
// Network threads never call engine APIs. New sessions invalidate old samples.
class Mailbox {
    struct Sample { Pose pose; uint64_t arrival; };
    mutable std::mutex mutex;
    Pose local;
    uint64_t localUpdated = 0;
    uint32_t generation = 0, remoteSequence = 0;
    uint64_t remoteTime = 0, remoteArrival = 0;
    bool connected = false;
    std::deque<Sample> remote;
public:
    bool publish(Pose pose, uint64_t now) {
        pose.active = true; pose.generation = 1;
        if (!valid(pose)) return false;
        normalize(pose);
        std::lock_guard lock(mutex);
        if (!local.active || now < localUpdated || now - localUpdated > staleMilliseconds) { if (++generation == 0) ++generation; }
        pose.generation = generation; pose.time = now;
        local = pose; localUpdated = now; return true;
    }
    void clearLocal() { std::lock_guard lock(mutex); local.active = false; }
    Pose outgoing(uint64_t now) const {
        std::lock_guard lock(mutex);
        Pose pose = local;
        pose.time = now;
        if (now < localUpdated || now - localUpdated > staleMilliseconds) pose.active = false;
        return pose;
    }
    void begin() { std::lock_guard lock(mutex); connected = true; remote.clear(); remoteSequence = 0; remoteTime = remoteArrival = 0; }
    void end() { std::lock_guard lock(mutex); connected = false; remote.clear(); remoteSequence = 0; remoteTime = remoteArrival = 0; }
    bool receive(const Pose& pose, uint32_t sequence, uint64_t now) {
        if (!valid(pose) || sequence == 0) return false;
        std::lock_guard lock(mutex);
        if (!connected || sequence <= remoteSequence) return false;
        if (pose.time < remoteTime || now < remoteArrival) return false;
        remoteSequence = sequence; remoteTime = pose.time; remoteArrival = now;
        if (!pose.active) { remote.clear(); return true; }
        if (!remote.empty()) {
            const auto& previous = remote.back().pose;
            float distance = 0;
            for (unsigned i = 0; i < 3; ++i) distance += std::pow(previous.position[i] - pose.position[i], 2);
            if (previous.generation != pose.generation || distance > 30 * 30 || now - remote.back().arrival > staleMilliseconds) remote.clear();
        }
        remote.push_back({pose, now});
        while (remote.size() > 32) remote.pop_front();
        return true;
    }
    bool sample(uint64_t now, Pose& result) const {
        std::lock_guard lock(mutex);
        if (!connected || remote.empty() || now < remote.back().arrival || now - remote.back().arrival > staleMilliseconds) return false;
        const uint64_t target = now > interpolationMilliseconds ? now - interpolationMilliseconds : 0;
        const Sample* before = &remote.front();
        const Sample* after = before;
        for (const auto& sample : remote) {
            after = &sample;
            if (sample.arrival >= target) break;
            before = &sample;
        }
        result = after->pose;
        if (before == after || after->arrival <= before->arrival) return true;
        float amount = std::clamp(static_cast<float>(target - std::min(target, before->arrival)) /
            static_cast<float>(after->arrival - before->arrival), 0.0f, 1.0f);
        for (unsigned i = 0; i < 3; ++i) result.position[i] = before->pose.position[i] + amount * (after->pose.position[i] - before->pose.position[i]);
        float dot = 0;
        for (unsigned i = 0; i < 4; ++i) dot += before->pose.rotation[i] * after->pose.rotation[i];
        for (unsigned i = 0; i < 4; ++i) result.rotation[i] = before->pose.rotation[i] + amount * ((dot < 0 ? -1 : 1) * after->pose.rotation[i] - before->pose.rotation[i]);
        normalize(result); return true;
    }
};
} // namespace nl::replication
