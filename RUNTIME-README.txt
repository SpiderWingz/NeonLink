REDLINE NATIVE RUNTIME 0.8.1 — PLAYER PREVIEW

Player protocol 2 adds validated UTF-8 names and a fixed 16-value character
summary. Only the owner publishes this metadata; receivers never apply it
to local inventory, development, customization or saves. The adapter uses
the game's existing marker projection/widget and a separate position-based
map marker, so a distant peer does not require an NPC in an unloaded sector.
Both peers need matching protocol 2 builds. Character appearance/equipment
and gameplay effects are not replicated yet.

INSTALL
Open Redline.exe, find your game, and choose Set up RED4ext + Redline.
The official RED4ext 1.31.0 loader, Codeware 1.21.2, redscript 0.5.32,
compiled DLL and game adapter scripts are embedded.
Installed path:
<Cyberpunk 2077>\red4ext\plugins\NeonLink\NeonLink.dll
Targets Cyberpunk 2077 2.5, Windows x64.

DIRECT HOSTING
The player host's game process runs a TCP listener. The launcher separately
configures its local listen endpoint and the public endpoint advertised to
guests. This supports ordinary router port forwarding; a public router IP
does not need to be assigned to the PC's local network interface.

HOSTDIRECT/JOINDIRECT explicitly select this IP-revealing route. Native
commands validate addresses, ports, invite keys, and advertised host names.
The launcher accepts an IPv4 or DNS invite and resolves DNS without blocking
the UI. The existing libsodium XChaCha20-Poly1305 transport and random
256-bit invite key are preserved. The host IP is intentionally visible.
No relay, platform service, account, or rented server is required.

Six-character code lookup and further IP-private handshake/relay work are
deferred. The 0.6.0 private prototype remains a separate checkpoint.
Loopback X25519 test functionality remains in the native source for later;
the 0.7.0 launcher defaults to direct connections.

NETWORK LIMITS
The host must have a reachable incoming TCP port. The optional Windows UPnP
mapper can request a router mapping but depends on router support. Manual
port forwarding and a Windows Firewall allowance may be needed.
Carrier-grade NAT or double NAT may prevent direct hosting. A successful
local test is not verification of internet reachability.
Supports one host and one guest; reconnect retry is bounded to 45 seconds.

WHAT WORKS
RED4ext Main/Query/Supports; restricted owner-user launcher IPC; explicit
direct host/join; encrypted 20 Hz movement frames; incorrect-key rejection;
stop/rejoin; capped breadcrumb logs and clean/unclean session markers;
game-thread partner proxy spawning and interpolation; native progress reports.
The DLL runs no listener until a host command is requested.
PREVIEW starts an explicitly simulated partner without a network connection.
It cannot replace an existing host/guest session and stops after five minutes.

WHAT IS UNFINISHED
Avatar appearance and locomotion animation are placeholders. Shared world,
NPC, combat, vehicle, quest, progression and save synchronization are unfinished.
This is not campaign co-op. No save sharing occurs. See TEST-RESULTS.txt for
the exact automated and in-game checks performed on this build.

BUILD
runtime\Build-Runtime.ps1 uses portable Zig 0.15.2 and libsodium 1.0.22-stable
MinGW64 static libraries. Engine builds require the pinned full SDK with the
Cyberpunk 2.5 callback ABI update; source/runtime/dependencies.json records it.
runtime\Harness.cs tests the actual DLL without running Cyberpunk.
The client --test-direct harness covers public/local endpoint separation,
native direct transport, invite parsing, and router-mapping ownership logic.
Router tests use a fake implementation; no actual router settings are changed.

DEPENDENCIES
https://github.com/WopsS/RED4ext/releases/tag/v1.31.0
https://github.com/WopsS/RED4ext.SDK/tree/5246e659d783e5834cc08a90e56c8932c9ed3449
https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip
https://download.libsodium.org/libsodium/releases/libsodium-1.0.22-stable-mingw.tar.gz

Included component licenses are accessible from About Redline.
No NightCoop code or binaries are used.
