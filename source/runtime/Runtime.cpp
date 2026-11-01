// Native transport and movement replication. The game adapter accesses engine
// objects only from script callbacks; networking operates on validated values.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define SODIUM_STATIC
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <sddl.h>
#include <sodium.h>
#include <RED4ext/Api/v1/PluginInfo.hpp>
#include <RED4ext/Api/v1/Sdk.hpp>
#include <RED4ext/Api/v1/EMainReason.hpp>
#include <atomic>
#include <array>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include "Diagnostics.hpp"
#include "Replication.hpp"

#define EXPORT extern "C" __declspec(dllexport)

// libsodium 1.0.22's MinGW build uses the C23 explicit-memory-wipe symbol.
// Zig's bundled MinGW CRT predates that export. Volatile stores implement
// the specified operation without permitting dead-store elimination.
extern "C" void* memset_explicit(void* destination, int value, size_t length) {
    volatile unsigned char* cursor = static_cast<volatile unsigned char*>(destination);
    while (length--) *cursor++ = static_cast<unsigned char>(value);
    return destination;
}

namespace nl {
#ifdef NEONLINK_ENGINE_BRIDGE
namespace bridge { bool initialize(RED4ext::v1::PluginHandle, const RED4ext::v1::Sdk*); }
#endif
using Key = std::array<unsigned char, crypto_secretstream_xchacha20poly1305_KEYBYTES>;
constexpr unsigned protocol = 1;
constexpr size_t maxCiphertext = 512;
struct Request { int mode = 0; std::string ip, advertised; unsigned short port = 0, publicPort = 0; Key key{}, localPublic{}, localSecret{}; bool kx = false; unsigned epoch = 0; };
struct Status {
    std::string phase = "idle", detail = "Movement development runtime loaded; in-game validation is pending.";
    std::string invite;
    unsigned peers = 0;
    unsigned localPort = 0;
    unsigned long long sent = 0, received = 0;
    unsigned long long movementSent = 0, movementReceived = 0;
    unsigned long long previewSamples = 0;
};
replication::Mailbox movement;
std::atomic<bool> movementBridge{false};
std::atomic<bool> previewMode{false}, peerConnected{false};
std::atomic<int> avatarStage{0};
std::atomic<ULONGLONG> avatarUpdated{0};
std::mutex mutex;
Request desired;
Status status;
Key identityPublic{}, identitySecret{};
std::atomic<unsigned> epoch{0};
std::atomic<bool> engineRunning{false};
HANDLE stopEvent = nullptr, networkThread = nullptr, pipeThread = nullptr;
RED4ext::v1::PluginHandle pluginHandle{};
const RED4ext::v1::Sdk* sdk = nullptr;
bool initialized = false;

struct Socket {
    SOCKET value = INVALID_SOCKET;
    Socket() = default;
    explicit Socket(SOCKET s) : value(s) {}
    ~Socket() { if (value != INVALID_SOCKET) closesocket(value); }
    Socket(const Socket&) = delete;
    Socket& operator=(const Socket&) = delete;
};
bool cancelled(unsigned current) { return epoch.load() != current || WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0; }
void setStatus(const char* phase, const char* detail, unsigned peers = 0, unsigned expectedEpoch = 0) {
    std::lock_guard lock(mutex);
    if (expectedEpoch != 0 && epoch.load() != expectedEpoch) return;
    if (status.phase != phase || status.detail != detail) diagnostics::write(std::string("State: ") + phase + "; " + detail);
    status.phase = phase; status.detail = detail; status.peers = peers;
}
std::string escape(const std::string& value) {
    std::string result;
    for (unsigned char c : value) {
        if (c == '"' || c == '\\') { result += '\\'; result += static_cast<char>(c); }
        else if (c >= 32 && c < 127) result += static_cast<char>(c);
    }
    return result;
}
std::string statusJson() {
    std::lock_guard lock(mutex);
    std::ostringstream out;
    std::array<char, 65> publicText{}; sodium_bin2hex(publicText.data(), publicText.size(), identityPublic.data(), identityPublic.size());
    replication::Pose remote;
    const auto now = GetTickCount64();
    out << "{\"version\":\"0.8.0\",\"build\":\"dev.2\",\"protocol\":1,\"identityKey\":\"" << publicText.data() << "\",\"pid\":" << GetCurrentProcessId()
        << ",\"phase\":\"" << escape(status.phase) << "\",\"detail\":\"" << escape(status.detail)
        << "\",\"invite\":\"" << escape(status.invite) << "\",\"peers\":" << status.peers
        << ",\"localPort\":" << status.localPort << ",\"sent\":" << status.sent << ",\"received\":" << status.received
        << ",\"engineRunning\":" << (engineRunning ? "true" : "false")
        << ",\"movementProtocol\":1,\"movementBridge\":" << (movementBridge ? "true" : "false")
        << ",\"localPlayerReady\":" << (movement.outgoing(now).active ? "true" : "false")
        << ",\"remotePlayerReady\":" << (movement.sample(now, remote) ? "true" : "false")
        << ",\"movementSent\":" << status.movementSent << ",\"movementReceived\":" << status.movementReceived
        << ",\"preview\":" << (previewMode ? "true" : "false") << ",\"previewSamples\":" << status.previewSamples
        << ",\"avatarStage\":" << avatarStage.load() << ",\"avatarReportFresh\":" << (now - avatarUpdated.load() < 1000 ? "true" : "false")
        << ",\"gameplayReplication\":false}";
    return out.str();
}
bool validIp(const std::string& ip) {
    in_addr address{}; return ip.size() <= 15 && InetPtonA(AF_INET, ip.c_str(), &address) == 1;
}
bool validName(const std::string& name) {
    if (name.empty() || name.size() > 253) return false;
    bool numeric = true; size_t start = 0;
    for (size_t i = 0; i <= name.size(); ++i) {
        if (i == name.size() || name[i] == '.') {
            size_t length = i - start;
            if (length == 0 || length > 63 || name[start] == '-' || name[i - 1] == '-') return false;
            start = i + 1;
        } else {
            unsigned char c = name[i];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')) return false;
            if (c < '0' || c > '9') numeric = false;
        }
    }
    return (!numeric || validIp(name)) && name != "0.0.0.0" && name != "255.255.255.255";
}
bool parsePort(const std::string& text, unsigned short& value) {
    if (text.empty() || text.size() > 5) return false;
    unsigned number = 0;
    for (char c : text) { if (c < '0' || c > '9') return false; number = number * 10 + static_cast<unsigned>(c - '0'); }
    if (number == 0 || number > 65535) return false;
    value = static_cast<unsigned short>(number); return true;
}
bool parseKey(const std::string& text, Key& key) {
    size_t length = 0;
    return text.size() == key.size() * 2 && sodium_hex2bin(key.data(), key.size(), text.data(), text.size(), nullptr, &length, nullptr) == 0 && length == key.size();
}
std::string hexKey(const Key& key) {
    std::array<char, 65> text{}; sodium_bin2hex(text.data(), text.size(), key.data(), key.size()); return text.data();
}
bool parseCommand(const std::string& message, Request& request) {
    std::istringstream input(message); std::string command, port, token, extra;
    input >> command;
    if (command == "STOP") { if (input >> extra) return false; request = Request{}; return true; }
    if (command == "PREVIEW") { if (input >> extra) return false; request = Request{}; request.mode = 3; return true; }
    if (command != "HOST" && command != "JOIN" && command != "HOSTKX" && command != "JOINKX" && command != "HOSTDIRECT" && command != "JOINDIRECT") return false;
    // Legacy aliases are isolated to the test harness. Normal game loading
    // requires an explicit DIRECT or loopback KX connection command.
    if ((command == "HOST" || command == "JOIN") && !diagnostics::testMode) return false;
    if (!(input >> request.ip >> port) || !validIp(request.ip) || port.empty() || port.size() > 5) return false;
    unsigned number = 0;
    for (char c : port) { if (c < '0' || c > '9') return false; number = number * 10 + static_cast<unsigned>(c - '0'); }
    if ((number == 0 && command != "HOSTKX" && command != "HOSTDIRECT") || number > 65535 || (request.ip == "0.0.0.0" && command != "HOSTDIRECT") || request.ip == "255.255.255.255") return false;
    request.port = static_cast<unsigned short>(number);
    request.advertised = request.ip; request.publicPort = request.port;
    if (command == "HOSTKX" || command == "JOINKX") {
        if (request.ip != "127.0.0.1" || !(input >> token) || !parseKey(token, request.key) || sodium_is_zero(request.key.data(), request.key.size())) return false;
        request.mode = command == "HOSTKX" ? 1 : 2; request.kx = true;
    }
    else if (command == "JOIN" || command == "JOINDIRECT") { if (!(input >> token) || !parseKey(token, request.key)) return false; request.mode = 2; }
    else {
        if (command == "HOSTDIRECT" && (!(input >> request.advertised >> port) || !validName(request.advertised) || !parsePort(port, request.publicPort))) return false;
        request.mode = 1; randombytes_buf(request.key.data(), request.key.size());
    }
    return !(input >> extra);
}
std::string command(const std::string& message) {
    if (message == "STATUS") return statusJson();
    if (message == "NEWKEY") {
        {
            std::lock_guard lock(mutex);
            if (desired.mode != 0) return "{\"error\":\"Stop the connection before creating a fresh identity.\"}";
            sodium_memzero(identitySecret.data(), identitySecret.size());
            if (crypto_kx_keypair(identityPublic.data(), identitySecret.data()) != 0) return "{\"error\":\"Key generation failed.\"}";
        }
        return statusJson();
    }
    Request next;
    if (!parseCommand(message, next)) return "{\"error\":\"Invalid runtime command.\"}";
    diagnostics::write(next.mode == 1 ? "Host transport requested." : next.mode == 2 ? "Guest transport requested." : next.mode == 3 ? "Local simulated partner requested." : "Connection stop requested.");
    {
        std::lock_guard lock(mutex);
        if (next.mode == 3 && desired.mode != 0 && desired.mode != 3) return "{\"error\":\"Stop the host or guest connection before starting the local partner test.\"}";
        next.epoch = epoch.load() + 1;
        if (next.kx) { next.localPublic = identityPublic; next.localSecret = identitySecret; }
        sodium_memzero(desired.key.data(), desired.key.size()); sodium_memzero(desired.localSecret.data(), desired.localSecret.size()); desired = next;
        movement.end();
        previewMode = next.mode == 3; peerConnected = false; avatarStage = 0; avatarUpdated = 0;
        status.peers = 0; status.localPort = 0; status.sent = 0; status.received = 0;
        status.movementSent = status.movementReceived = 0;
        status.previewSamples = 0;
        status.invite = next.mode == 1 && !next.kx ? "nlink://" + next.advertised + ":" + std::to_string(next.publicPort) + "/" + hexKey(next.key) : "";
        status.phase = next.mode == 0 ? "idle" : "starting";
        status.detail = next.mode == 0 ? "Connection test stopped." : "Preparing encrypted transport test. Gameplay is not synchronized.";
        epoch.store(next.epoch);
    }
    sodium_memzero(next.key.data(), next.key.size());
    sodium_memzero(next.localSecret.data(), next.localSecret.size());
    return statusJson();
}

void nonblocking(SOCKET socket) {
    u_long value = 1; if (ioctlsocket(socket, FIONBIO, &value) != 0) throw std::runtime_error("socket mode");
}
bool waitSocket(SOCKET socket, bool write, unsigned current, ULONGLONG deadline) {
    while (!cancelled(current) && GetTickCount64() < deadline) {
        fd_set set; FD_ZERO(&set); FD_SET(socket, &set); timeval timeout{0, 10000};
        int result = select(0, write ? nullptr : &set, write ? &set : nullptr, nullptr, &timeout);
        if (result > 0) return true;
        if (result < 0) return false;
    }
    return false;
}
bool transfer(SOCKET socket, unsigned char* bytes, size_t count, bool write, unsigned current) {
    auto deadline = GetTickCount64() + 5000;
    size_t offset = 0;
    while (offset < count && !cancelled(current)) {
        if (!waitSocket(socket, write, current, deadline)) return false;
        int got = write ? send(socket, reinterpret_cast<const char*>(bytes + offset), static_cast<int>(count - offset), 0)
                        : recv(socket, reinterpret_cast<char*>(bytes + offset), static_cast<int>(count - offset), 0);
        if (got == SOCKET_ERROR && WSAGetLastError() == WSAEWOULDBLOCK) continue;
        if (got <= 0) return false;
        offset += static_cast<size_t>(got);
    }
    return offset == count;
}
struct Channel {
    crypto_secretstream_xchacha20poly1305_state outgoing{}, incoming{};
    ~Channel() { sodium_memzero(this, sizeof(*this)); }
};
bool sendMessage(SOCKET socket, Channel& channel, const std::string& text, unsigned current) {
    if (text.empty() || text.size() + crypto_secretstream_xchacha20poly1305_ABYTES > maxCiphertext) return false;
    std::array<unsigned char, maxCiphertext + 4> frame{}; unsigned long long length = 0;
    if (crypto_secretstream_xchacha20poly1305_push(&channel.outgoing, frame.data() + 4, &length,
            reinterpret_cast<const unsigned char*>(text.data()), text.size(), nullptr, 0, 0) != 0) return false;
    auto networkLength = htonl(static_cast<u_long>(length)); memcpy(frame.data(), &networkLength, 4);
    if (!transfer(socket, frame.data(), static_cast<size_t>(length) + 4, true, current)) return false;
    { std::lock_guard lock(mutex); if (epoch.load() == current) ++status.sent; }
    return true;
}
bool receiveMessage(SOCKET socket, Channel& channel, std::string& text, unsigned current) {
    u_long length = 0;
    if (!transfer(socket, reinterpret_cast<unsigned char*>(&length), 4, false, current)) return false;
    length = ntohl(length);
    if (length < crypto_secretstream_xchacha20poly1305_ABYTES || length > maxCiphertext) return false;
    std::array<unsigned char, maxCiphertext> cipher{}, plain{};
    if (!transfer(socket, cipher.data(), length, false, current)) return false;
    unsigned long long plainLength = 0; unsigned char tag = 0;
    if (crypto_secretstream_xchacha20poly1305_pull(&channel.incoming, plain.data(), &plainLength, &tag, cipher.data(), length, nullptr, 0) != 0 || tag != 0) return false;
    text.assign(reinterpret_cast<char*>(plain.data()), static_cast<size_t>(plainLength));
    { std::lock_guard lock(mutex); if (epoch.load() == current) ++status.received; }
    return true;
}
bool authenticate(SOCKET socket, const Request& request, Channel& channel, bool server) {
    if (request.kx) {
        struct Secrets { Key rx{}, tx{}; ~Secrets() { sodium_memzero(this, sizeof(*this)); } } secrets;
        std::array<unsigned char, 40> local{}, remote{};
        const unsigned char magic[8] = {'N','L','I','N','K',0,0,2}; memcpy(local.data(), magic, 8); memcpy(local.data() + 8, request.localPublic.data(), 32);
        if (server) {
            if (!transfer(socket, local.data(), local.size(), true, request.epoch) || !transfer(socket, remote.data(), remote.size(), false, request.epoch)) return false;
        } else if (!transfer(socket, remote.data(), remote.size(), false, request.epoch) || !transfer(socket, local.data(), local.size(), true, request.epoch)) return false;
        if (sodium_memcmp(remote.data(), magic, 8) != 0 || sodium_memcmp(remote.data() + 8, request.key.data(), 32) != 0) return false;
        int result = server ? crypto_kx_server_session_keys(secrets.rx.data(), secrets.tx.data(), request.localPublic.data(), request.localSecret.data(), request.key.data())
            : crypto_kx_client_session_keys(secrets.rx.data(), secrets.tx.data(), request.localPublic.data(), request.localSecret.data(), request.key.data());
        if (result != 0) return false;
        std::array<unsigned char, crypto_secretstream_xchacha20poly1305_HEADERBYTES> mine{}, theirs{};
        if (crypto_secretstream_xchacha20poly1305_init_push(&channel.outgoing, mine.data(), secrets.tx.data()) != 0) return false;
        if (server) {
            if (!transfer(socket, mine.data(), mine.size(), true, request.epoch) || !transfer(socket, theirs.data(), theirs.size(), false, request.epoch)) return false;
        } else if (!transfer(socket, theirs.data(), theirs.size(), false, request.epoch) || !transfer(socket, mine.data(), mine.size(), true, request.epoch)) return false;
        if (crypto_secretstream_xchacha20poly1305_init_pull(&channel.incoming, theirs.data(), secrets.rx.data()) != 0) return false;
        if (!sendMessage(socket, channel, server ? "NEONLINK/2/host" : "NEONLINK/2/guest", request.epoch)) return false;
        std::string proof;
        return receiveMessage(socket, channel, proof, request.epoch) && proof == (server ? "NEONLINK/2/guest" : "NEONLINK/2/host");
    }
    constexpr size_t headerBytes = crypto_secretstream_xchacha20poly1305_HEADERBYTES;
    std::array<unsigned char, headerBytes + 8> local{}, remote{};
    const unsigned char magic[8] = {'N','L','I','N','K',0,0,1}; memcpy(local.data(), magic, 8);
    if (crypto_secretstream_xchacha20poly1305_init_push(&channel.outgoing, local.data() + 8, request.key.data()) != 0) return false;
    if (server) {
        if (!transfer(socket, local.data(), local.size(), true, request.epoch) || !transfer(socket, remote.data(), remote.size(), false, request.epoch)) return false;
    } else {
        if (!transfer(socket, remote.data(), remote.size(), false, request.epoch) || !transfer(socket, local.data(), local.size(), true, request.epoch)) return false;
    }
    if (sodium_memcmp(remote.data(), magic, 8) != 0 || crypto_secretstream_xchacha20poly1305_init_pull(&channel.incoming, remote.data() + 8, request.key.data()) != 0) return false;
    const std::string mine = server ? "NEONLINK/1/0.4/transport/host" : "NEONLINK/1/0.4/transport/guest";
    const std::string expected = server ? "NEONLINK/1/0.4/transport/guest" : "NEONLINK/1/0.4/transport/host";
    if (!sendMessage(socket, channel, mine, request.epoch)) return false;
    std::string theirs;
    return receiveMessage(socket, channel, theirs, request.epoch) && theirs == expected;
}
bool connectedLoop(SOCKET socket, const Request& request) {
    Channel channel;
    if (!authenticate(socket, request, channel, request.mode == 1)) return false;
    if (!sendMessage(socket, channel, replication::hello(), request.epoch)) return false;
    std::string hello;
    if (!receiveMessage(socket, channel, hello, request.epoch) || !replication::acceptHello(hello)) return false;
    {
        std::lock_guard lock(mutex);
        if (cancelled(request.epoch)) return true;
        movement.begin();
    }
    peerConnected = true;
    struct Cleanup { ~Cleanup() { peerConnected = false; movement.end(); } } cleanup;
    int noDelay = 1; setsockopt(socket, IPPROTO_TCP, TCP_NODELAY, reinterpret_cast<const char*>(&noDelay), sizeof(noDelay));
    setStatus("connected", "Movement protocol connected. In-game character validation is pending; quests and combat are not synchronized.", 1, request.epoch);
    ULONGLONG lastSend = 0, lastReceive = GetTickCount64();
    uint32_t sequence = 0;
    while (!cancelled(request.epoch)) {
        ULONGLONG now = GetTickCount64();
        if (now - lastSend >= replication::tickMilliseconds) {
            if (++sequence == 0 || !sendMessage(socket, channel, replication::encode(movement.outgoing(now), sequence), request.epoch)) break;
            lastSend = now;
            std::lock_guard lock(mutex); if (epoch.load() == request.epoch) ++status.movementSent;
        }
        if (waitSocket(socket, false, request.epoch, now + 10)) {
            std::string text;
            replication::Pose pose; uint32_t receivedSequence = 0;
            if (!receiveMessage(socket, channel, text, request.epoch) || !replication::decode(text, pose, receivedSequence) ||
                !movement.receive(pose, receivedSequence, GetTickCount64())) break;
            lastReceive = GetTickCount64();
            std::lock_guard lock(mutex); if (epoch.load() == request.epoch) ++status.movementReceived;
        }
        if (GetTickCount64() - lastReceive > 6000) break;
    }
    return true;
}
sockaddr_in endpoint(const Request& request) {
    sockaddr_in address{}; address.sin_family = AF_INET; address.sin_port = htons(request.port);
    InetPtonA(AF_INET, request.ip.c_str(), &address.sin_addr); return address;
}
void host(const Request& request) {
    Socket listener(socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
    if (listener.value == INVALID_SOCKET) throw std::runtime_error("create listener");
    BOOL exclusive = TRUE; setsockopt(listener.value, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, reinterpret_cast<const char*>(&exclusive), sizeof(exclusive));
    nonblocking(listener.value); auto address = endpoint(request);
    if (bind(listener.value, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0 || listen(listener.value, 1) != 0) {
        setStatus("error", "Cannot listen at this IPv4 address/port. It may be in use or not assigned to this PC.", 0, request.epoch); return;
    }
    int addressLength = sizeof(address);
    if (getsockname(listener.value, reinterpret_cast<sockaddr*>(&address), &addressLength) != 0) throw std::runtime_error("listener address");
    { std::lock_guard lock(mutex); if (epoch.load() == request.epoch) status.localPort = ntohs(address.sin_port); }
    setStatus("hosting", "Waiting for one authenticated transport-test guest. Gameplay is not synchronized.", 0, request.epoch);
    while (!cancelled(request.epoch)) {
        if (!waitSocket(listener.value, false, request.epoch, GetTickCount64() + 100)) continue;
        Socket peer(accept(listener.value, nullptr, nullptr)); if (peer.value == INVALID_SOCKET) continue;
        nonblocking(peer.value);
        bool accepted = connectedLoop(peer.value, request);
        if (!cancelled(request.epoch)) {
            setStatus("hosting", accepted ? "Guest disconnected. Waiting for reconnection." : "Unverified guest rejected. Waiting for an authenticated guest.", 0, request.epoch);
            WaitForSingleObject(stopEvent, 150);
        }
    }
}
void join(const Request& request) {
    auto deadline = GetTickCount64() + 45000; unsigned backoff = 1;
    while (!cancelled(request.epoch) && GetTickCount64() < deadline) {
        setStatus("connecting", "Connecting to the transport-test host; retry window is 45 seconds.", 0, request.epoch);
        Socket peer(socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
        if (peer.value == INVALID_SOCKET) throw std::runtime_error("create peer");
        nonblocking(peer.value); auto address = endpoint(request);
        int result = connect(peer.value, reinterpret_cast<sockaddr*>(&address), sizeof(address));
        int error = WSAGetLastError();
        bool ready = result == 0 || ((error == WSAEWOULDBLOCK || error == WSAEINPROGRESS) && waitSocket(peer.value, true, request.epoch, GetTickCount64() + 3000));
        int socketError = 0, errorLength = sizeof(socketError);
        if (ready && (getsockopt(peer.value, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&socketError), &errorLength) != 0 || socketError != 0)) ready = false;
        if (ready) {
            if (connectedLoop(peer.value, request)) { deadline = GetTickCount64() + 45000; backoff = 1; }
            else if (!cancelled(request.epoch)) { setStatus("rejected", "Authentication or protocol mismatch. Check the complete invite code.", 0, request.epoch); return; }
        }
        if (cancelled(request.epoch)) return;
        setStatus("recovering", "Connection interrupted/unavailable. Retrying within the 45-second grace period.", 0, request.epoch);
        ULONGLONG retryAt = GetTickCount64() + backoff * 1000;
        while (!cancelled(request.epoch) && GetTickCount64() < retryAt && GetTickCount64() < deadline) WaitForSingleObject(stopEvent, 100);
        if (backoff < 8) backoff *= 2;
    }
    if (!cancelled(request.epoch)) setStatus("error", "Connection recovery window expired. Start a new connection test.", 0, request.epoch);
}
void preview(const Request& request) {
    {
        std::lock_guard lock(mutex);
        if (cancelled(request.epoch)) return;
        movement.begin();
    }
    struct Cleanup { ~Cleanup() { movement.end(); } } cleanup;
    setStatus("preview", "Local simulated partner test. No network peer is connected. Stops automatically after five minutes.", 0, request.epoch);
    const auto started = GetTickCount64();
    uint32_t sequence = 0;
    while (!cancelled(request.epoch) && GetTickCount64() - started < 300000) {
        const auto now = GetTickCount64();
        auto pose = movement.outgoing(now);
        if (pose.active) {
            const float angle = static_cast<float>(now - started) * 0.001f;
            pose.position[0] += 4.0f + std::cos(angle);
            pose.position[1] += std::sin(angle);
        }
        if (!movement.receive(pose, ++sequence, now)) break;
        { std::lock_guard lock(mutex); if (epoch.load() == request.epoch) ++status.previewSamples; }
        WaitForSingleObject(stopEvent, replication::tickMilliseconds);
    }
    std::lock_guard lock(mutex);
    if (epoch.load() == request.epoch) {
        desired.mode = 0; previewMode = false; avatarStage = 0; avatarUpdated = 0;
        status.phase = "idle"; status.detail = "Local partner test ended.";
    }
}
DWORD WINAPI networkMain(void*) {
    unsigned handled = 0;
    while (WaitForSingleObject(stopEvent, 100) != WAIT_OBJECT_0) {
        Request request;
        { std::lock_guard lock(mutex); request = desired; }
        if (request.epoch == handled || request.mode == 0) { sodium_memzero(request.key.data(), request.key.size()); sodium_memzero(request.localSecret.data(), request.localSecret.size()); continue; }
        handled = request.epoch;
        try { if (request.mode == 1) host(request); else if (request.mode == 3) preview(request); else join(request); }
        catch (...) { setStatus("error", "Transport worker failed. Stop the test before retrying.", 0, request.epoch); }
        sodium_memzero(request.key.data(), request.key.size());
        sodium_memzero(request.localSecret.data(), request.localSecret.size());
    }
    return 0;
}

// IPC is restricted to this Windows user and rejects remote pipe clients.
PSECURITY_DESCRIPTOR pipeSecurity() {
    HANDLE token{}; if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return nullptr;
    DWORD bytes = 0; GetTokenInformation(token, TokenUser, nullptr, 0, &bytes);
    std::vector<unsigned char> buffer(bytes); bool ok = GetTokenInformation(token, TokenUser, buffer.data(), bytes, &bytes) != 0; CloseHandle(token);
    if (!ok) return nullptr;
    LPWSTR sid{}; if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid, &sid)) return nullptr;
    std::wstring sddl = L"D:P(A;;GA;;;" + std::wstring(sid) + L")"; LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor{};
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return nullptr;
    return descriptor;
}
bool pipeWait(HANDLE pipe, OVERLAPPED& operation, DWORD timeout, DWORD& count) {
    HANDLE events[]{stopEvent, operation.hEvent};
    DWORD waited = WaitForMultipleObjects(2, events, FALSE, timeout);
    if (waited != WAIT_OBJECT_0 + 1) { CancelIoEx(pipe, &operation); GetOverlappedResult(pipe, &operation, &count, TRUE); return false; }
    return GetOverlappedResult(pipe, &operation, &count, FALSE) != 0;
}
DWORD WINAPI pipeMain(void*) {
    auto descriptor = pipeSecurity(); if (!descriptor) { setStatus("error", "Cannot secure the local launcher control pipe."); return 1; }
    SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
    wchar_t name[96]{}; swprintf(name, 96, L"\\\\.\\pipe\\NeonLink.Runtime.%lu", GetCurrentProcessId());
    HANDLE pipe = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 1024, 0, &attributes);
    LocalFree(descriptor);
    if (pipe == INVALID_HANDLE_VALUE) { setStatus("error", "Cannot create the local launcher control pipe."); return 1; }
    HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!event) { CloseHandle(pipe); return 1; }
    while (WaitForSingleObject(stopEvent, 0) != WAIT_OBJECT_0) {
        OVERLAPPED operation{}; operation.hEvent = event; ResetEvent(event); DWORD count = 0;
        BOOL connected = ConnectNamedPipe(pipe, &operation); DWORD error = GetLastError();
        if (!connected && error == ERROR_IO_PENDING) connected = pipeWait(pipe, operation, INFINITE, count);
        else if (!connected && error == ERROR_PIPE_CONNECTED) connected = TRUE;
        if (!connected) { DisconnectNamedPipe(pipe); continue; }
        std::array<char, 1024> buffer{}; operation = {}; operation.hEvent = event; ResetEvent(event); count = 0;
        BOOL read = ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size() - 1), &count, &operation);
        if (!read && GetLastError() == ERROR_IO_PENDING) read = pipeWait(pipe, operation, 2000, count);
        if (read && count > 0 && count < buffer.size()) {
            try {
                std::string input(buffer.data(), count);
                while (!input.empty() && (input.back() == '\r' || input.back() == '\n')) input.pop_back();
                std::string response = command(input);
                operation = {}; operation.hEvent = event; ResetEvent(event); count = 0;
                BOOL written = WriteFile(pipe, response.data(), static_cast<DWORD>(response.size()), &count, &operation);
                if (!written && GetLastError() == ERROR_IO_PENDING) pipeWait(pipe, operation, 2000, count);
                // Keep the message alive until the launcher has read it. A
                // bounded ACK replaces FlushFileBuffers, which could block
                // plugin unload forever if a local client stopped reading.
                operation = {}; operation.hEvent = event; ResetEvent(event); count = 0;
                char ack{};
                BOOL acknowledged = ReadFile(pipe, &ack, 1, &count, &operation);
                if (!acknowledged && GetLastError() == ERROR_IO_PENDING) pipeWait(pipe, operation, 1500, count);
            } catch (...) { /* Bad input must never unwind into the game. */ }
        }
        DisconnectNamedPipe(pipe);
    }
    CloseHandle(event); CloseHandle(pipe); return 0;
}
bool start() {
    if (initialized) return true;
    if (sodium_init() < 0) return false;
    WSADATA data{}; if (WSAStartup(MAKEWORD(2, 2), &data) != 0) return false;
    stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr); if (!stopEvent) { WSACleanup(); return false; }
    desired = {}; status = {}; epoch = 0;
    crypto_kx_keypair(identityPublic.data(), identitySecret.data());
    diagnostics::start();
    networkThread = CreateThread(nullptr, 0, networkMain, nullptr, 0, nullptr);
    pipeThread = CreateThread(nullptr, 0, pipeMain, nullptr, 0, nullptr);
    if (!networkThread || !pipeThread) {
        SetEvent(stopEvent);
        if (networkThread) { WaitForSingleObject(networkThread, INFINITE); CloseHandle(networkThread); }
        if (pipeThread) { WaitForSingleObject(pipeThread, INFINITE); CloseHandle(pipeThread); }
        CloseHandle(stopEvent); stopEvent = nullptr; WSACleanup(); diagnostics::close(); return false;
    }
    initialized = true; return true;
}
void stop() {
    if (!initialized) return;
    SetEvent(stopEvent); ++epoch;
    WaitForSingleObject(networkThread, INFINITE); WaitForSingleObject(pipeThread, INFINITE);
    CloseHandle(networkThread); CloseHandle(pipeThread); CloseHandle(stopEvent);
    networkThread = pipeThread = stopEvent = nullptr; WSACleanup();
    { std::lock_guard lock(mutex); sodium_memzero(desired.key.data(), desired.key.size()); sodium_memzero(desired.localSecret.data(), desired.localSecret.size()); sodium_memzero(identitySecret.data(), identitySecret.size()); desired = {}; status = {}; }
    movement.end(); movement.clearLocal();
    initialized = false; engineRunning = false; previewMode = false; peerConnected = false; avatarStage = 0; avatarUpdated = 0;
    diagnostics::close();
}
bool onEnter(RED4ext::CGameApplication*) { engineRunning = true; diagnostics::write("RED4ext engine-running state entered."); return true; }
bool onExit(RED4ext::CGameApplication*) { engineRunning = false; movement.clearLocal(); diagnostics::write("RED4ext engine-running state exited."); return true; }
} // namespace nl

static_assert(sizeof(RED4ext::v1::SemVer) == 16 && sizeof(RED4ext::v1::PluginInfo) == 56, "RED4ext API v1 x64 ABI mismatch");
EXPORT uint32_t Supports() { return 1; }
EXPORT void Query(RED4ext::v1::PluginInfo* info) {
    if (!info) return;
    info->sdk = {1, 0, 0, {0, 0}};
    info->name = L"NeonLink"; info->author = L"NeonLink fan project";
    info->version = {0, 8, 0, {1, 1}};
    // The engine bridge is tied to 2.5. Transport-only fixtures use loader APIs.
#ifdef NEONLINK_ENGINE_BRIDGE
    info->runtime = {3, 0, 88, 46235}; // Exact Cyberpunk 2.5 engine ABI.
#else
    info->runtime = {0xffff, 0xffff, 0xffff, 0xffff};
#endif
}
EXPORT bool Main(RED4ext::v1::PluginHandle handle, RED4ext::v1::EMainReason reason, const RED4ext::v1::Sdk* sdk) {
    try {
        if (reason == RED4ext::v1::EMainReason::Load) {
            nl::pluginHandle = handle; nl::sdk = sdk;
            diagnostics::testMode = sdk == nullptr;
            if (!nl::start()) return false;
#ifdef NEONLINK_ENGINE_BRIDGE
            if (sdk && !nl::bridge::initialize(handle, sdk)) { nl::stop(); return false; }
#endif
            if (sdk && sdk->logger && sdk->logger->Info) sdk->logger->Info(handle, "NeonLink 0.8.0 movement development build loaded. In-game validation is pending.");
            if (sdk && sdk->gameStates && sdk->gameStates->Add) {
                RED4ext::v1::GameState state{nl::onEnter, nullptr, nl::onExit};
                sdk->gameStates->Add(handle, static_cast<RED4ext::EGameStateType>(2), &state);
            }
        } else if (reason == RED4ext::v1::EMainReason::Unload) nl::stop();
        return true;
    } catch (...) { nl::stop(); return false; }
}
// A narrow smoke-test entry point: starts the same workers outside the game.
// Normal loading always goes through RED4ext's Main/Query/Supports exports.
EXPORT bool NeonLinkTestStart() {
    try {
        // A loaded plugin must never be switched into fixture mode through
        // this legacy smoke-test export.
        if (nl::initialized || nl::sdk || GetModuleHandleW(L"RED4ext.dll")) return false;
        diagnostics::testMode = true; return nl::start();
    } catch (...) { return false; }
}
EXPORT void NeonLinkTestStop() { if (diagnostics::testMode) nl::stop(); }
// Fixture injection is disabled when RED4ext loads the plugin normally.
EXPORT bool NeonLinkTestPublish(const float* values) {
    if (!diagnostics::testMode || !values) return false;
    nl::replication::Pose pose;
    for (unsigned i = 0; i < 3; ++i) pose.position[i] = values[i];
    for (unsigned i = 0; i < 4; ++i) pose.rotation[i] = values[3 + i];
    return nl::movement.publish(pose, GetTickCount64());
}
EXPORT bool NeonLinkTestSample(float* values) {
    if (!diagnostics::testMode || !values) return false;
    nl::replication::Pose pose;
    if (!nl::movement.sample(GetTickCount64(), pose)) return false;
    for (unsigned i = 0; i < 3; ++i) values[i] = pose.position[i];
    for (unsigned i = 0; i < 4; ++i) values[3 + i] = pose.rotation[i];
    return true;
}
EXPORT void NeonLinkTestClear() { if (diagnostics::testMode) nl::movement.clearLocal(); }
