#pragma once
#include "Replication.hpp"

namespace nl::replication {
// Display metadata only. A peer can never write another player's inventory,
// development system, save, or customization state through this protocol.
constexpr size_t buildFields = 16;
struct Profile {
    std::string name = "V";
    uint32_t generation = 0;
    // Level, Street Cred, Body, Reflexes, Tech, Intelligence, Cool, unspent
    // perk/attribute/relic points, five skill levels, body gender (0/1).
    std::array<uint32_t, buildFields> build{};
    bool operator==(const Profile&) const = default;
};
inline bool validPlayerName(const std::string& name) {
    if (name.empty() || name.size() > 96 || name.front() == ' ' || name.back() == ' ') return false;
    size_t count = 0;
    for (size_t i = 0; i < name.size();) {
        uint32_t c = static_cast<unsigned char>(name[i++]);
        unsigned continuation = 0; uint32_t minimum = 0;
        if (c >= 0xc2 && c <= 0xdf) { c &= 31; continuation = 1; minimum = 0x80; }
        else if (c >= 0xe0 && c <= 0xef) { c &= 15; continuation = 2; minimum = 0x800; }
        else if (c >= 0xf0 && c <= 0xf4) { c &= 7; continuation = 3; minimum = 0x10000; }
        else if (c >= 0x80) return false;
        if (i + continuation > name.size()) return false;
        for (unsigned j = 0; j < continuation; ++j) {
            const auto b = static_cast<unsigned char>(name[i++]);
            if ((b & 0xc0) != 0x80) return false;
            c = (c << 6) | (b & 63);
        }
        if (c < minimum || c > 0x10ffff || (c >= 0xd800 && c <= 0xdfff) || c < 32 ||
            (c >= 0x7f && c <= 0x9f) || c == '<' || c == '>' || c == '&' || c == '"' || c == '\\' ||
            c == '[' || c == ']' || c == '{' || c == '}' ||
            (c >= 0x200b && c <= 0x200f) || (c >= 0x2028 && c <= 0x202e) ||
            (c >= 0x2060 && c <= 0x206f) || c == 0xfeff || ++count > 24) return false;
    }
    return count != 0;
}
inline bool valid(const Profile& profile) {
    if (!validPlayerName(profile.name) || profile.build.back() > 1) return false;
    for (auto value : profile.build) if (value > 100000 || (profile.generation == 0 && value != 0)) return false;
    return true;
}
inline std::string encode(const Profile& profile, uint32_t sequence) {
    if (!valid(profile) || sequence == 0) return {};
    auto data = header(Kind::profile, sequence, static_cast<uint32_t>(8 + buildFields * 4 + profile.name.size()));
    put32(data, profile.generation); put32(data, static_cast<uint32_t>(profile.name.size()));
    for (auto value : profile.build) put32(data, value);
    return data + profile.name;
}
inline bool decode(const std::string& data, Profile& result, uint32_t& sequence) {
    constexpr size_t fixed = 8 + buildFields * 4;
    if (data.size() < headerBytes + fixed || data.size() > headerBytes + fixed + 96 ||
        !isFrame(data, Kind::profile, data.size() - headerBytes) || get32(data, 8) == 0 ||
        get32(data, 20) != data.size() - headerBytes - fixed) return false;
    Profile profile; profile.generation = get32(data, 16);
    for (size_t i = 0; i < buildFields; ++i) profile.build[i] = get32(data, 24 + i * 4);
    profile.name = data.substr(headerBytes + fixed);
    if (!valid(profile)) return false;
    result = profile; sequence = get32(data, 8); return true;
}
class Profiles {
    mutable std::mutex mutex;
    Profile local, remote;
    uint64_t updated = 0;
    uint32_t remoteSequence = 0;
    bool connected = false;
public:
    bool setName(const std::string& name) {
        if (!validPlayerName(name)) return false;
        std::lock_guard lock(mutex); local.name = name; return true;
    }
    bool publishBuild(uint32_t generation, const std::array<uint32_t, buildFields>& build, uint64_t now) {
        Profile value; value.generation = generation; value.build = build;
        if (generation == 0 || !valid(value)) return false;
        std::lock_guard lock(mutex); local.generation = generation; local.build = build; updated = now; return true;
    }
    void clearBuild() { std::lock_guard lock(mutex); local.generation = 0; local.build = {}; updated = 0; }
    Profile outgoing(uint64_t now) const {
        std::lock_guard lock(mutex); auto value = local;
        if (now < updated || now - updated > 3000) { value.generation = 0; value.build = {}; }
        return value;
    }
    void begin() { std::lock_guard lock(mutex); connected = true; remote = {}; remoteSequence = 0; }
    void end() { std::lock_guard lock(mutex); connected = false; remote = {}; remoteSequence = 0; }
    bool receive(const Profile& value, uint32_t sequence) {
        if (!valid(value) || sequence == 0) return false;
        std::lock_guard lock(mutex);
        if (!connected || sequence <= remoteSequence) return false;
        remote = value; remoteSequence = sequence; return true;
    }
    bool sample(Profile& value) const {
        std::lock_guard lock(mutex);
        if (!connected || remoteSequence == 0) return false;
        value = remote; return true;
    }
};
} // namespace nl::replication
