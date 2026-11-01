NEONLINK.DLL 0.7.0 — DIRECT CONNECTION DEVELOPMENT BUILD

INSTALL
Open NeonLink.exe, find your game, and choose Set up RED4ext + NeonLink.
The official RED4ext 1.31.0 loader and compiled DLL are embedded.
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
direct host/join; encrypted heartbeat frames; incorrect-key rejection;
stop/rejoin; capped breadcrumb logs and clean/unclean session markers.
The DLL runs no listener until a host command is requested.

WHAT IS UNFINISHED
No player/avatar spawning, movement, world, NPC, combat, vehicle, quest,
progression, or save synchronization. This is not playable co-op.
No game memory patching or save sharing occurs. In-game behavior has not
been verified inside Cyberpunk; tests load the real DLL into fixtures.

BUILD
runtime\Build-Runtime.ps1 uses portable Zig 0.15.2 and libsodium 1.0.22-stable
MinGW64 static libraries. RED4ext API 1.0.0 headers are vendored.
runtime\Harness.cs tests the actual DLL without running Cyberpunk.
The client --test-direct harness covers public/local endpoint separation,
native direct transport, invite parsing, and router-mapping ownership logic.
Router tests use a fake implementation; no actual router settings are changed.

DEPENDENCIES
https://github.com/WopsS/RED4ext/releases/tag/v1.31.0
https://github.com/WopsS/RED4ext.SDK/tree/1.0.0
https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip
https://download.libsodium.org/libsodium/releases/libsodium-1.0.22-stable-mingw.tar.gz

Included component licenses are accessible from About NeonLink.
No NightCoop code or binaries are used.
