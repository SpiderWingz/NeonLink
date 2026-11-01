NEONLINK CLIENT 0.7.0 — DIRECT HOSTING DEVELOPMENT BUILD

This build follows the selected direct-hosting approach. The host's PC runs
the native listener, and guests connect to its public IPv4 address or DNS
name. No rented server, relay, platform account, or paid networking service
is required. The host's IP address IS visible to peers.

QUICK START
1. Run NeonLink.exe. Find Cyberpunk 2077 and click Set up RED4ext + NeonLink.
2. Launch Cyberpunk and load a save. Open Host & join in the launcher.
3. Leave Listen address at 0.0.0.0 unless you need a specific local interface.
4. Enter your actual public IPv4 address or DNS name and public TCP port.
5. Forward that TCP port on your router to this PC's local TCP port (default
   47890), or try the optional Map port button if your router supports UPnP.
6. Click Start host and share the COMPLETE nlink://... invite privately.
7. Your guest installs the same build, launches their game, pastes the invite
   under Join your host, and clicks Join host.

The advertised public address is separate from the local listen address.
Do not try to bind your PC directly to a public address assigned only to your
router. For a LAN test, advertise this PC's LAN address. 127.0.0.1 tests only
the same computer. Windows Firewall must allow the game's inbound TCP port.

ROUTER SETUP
Map port requests an optional UPnP TCP mapping and fills the public address
returned by the router. It does nothing until you click it. Existing mappings
are checked before adding; a recorded NeonLink mapping can be removed from
the same screen. Mappings stay until you remove them. Remove the mapping when
finished hosting. Manual port forwarding is the fallback if UPnP is disabled
or unsupported. The automatic mapper has been verified with a fake router,
not with your actual hardware.

Carrier-grade NAT, double NAT, an ISP blocking inbound traffic, or an
unreachable public address can prevent direct internet connections. A local
listener or successful router mapping does not prove internet reachability.
Internet reachability still needs verification between separate networks.

INVITES AND ENCRYPTION
For now invites include the host address, port, and a randomly generated
256-bit key. Six-character code lookup and the IP-private relay route are
deferred. The existing authenticated encrypted transport is retained; this
update does not add another handshake or require account setup. Share the
whole invite only with your intended guest. Start host again to rotate it.

CURRENT MOD STATUS
The native DLL and desktop launcher are compiled development components.
Two-player transport tests exchange encrypted heartbeats, including stop and
rejoin behavior. Avatars, movement, NPCs, combat, vehicles, quests, progression,
and save replication are NOT implemented. This is not playable co-op.
The intended model keeps the save owner as the game host, but its game
replication adapter is unfinished. Play NeonLink remains disabled.

ALL IN ONE
The EXE embeds official RED4ext 1.31.0, NeonLink.dll 0.7.0, all six pictures,
both silent video backdrops, documentation, and component licenses.
No adjacent assets/payload folders or separate dependency downloads are
required. Setup checks hashes, repairs missing/damaged files, preserves
configuration and unrelated mods, and backs up files it replaces.
This build targets owned Cyberpunk 2077 patch 2.5, Windows x64.
Close the game before installing or repairing.

OTHER FEATURES
Random full-window startup artwork, five-minute rotation, silent videos,
preferences, save backups, diagnostic exports, local crash reports, and an
independent watcher that continues if the launcher closes. Direct hosting
lives in the game process and may also continue while the launcher is closed.
Return to the launcher to stop hosting and remove optional router mappings.

No automatic telemetry/uploads or online updater. Shared log text redacts
invite keys, code-shaped identifiers, fingerprints, and user/game paths.
Save files and memory dumps are not included in crash reports.

DATA AND REQUIREMENTS
%LOCALAPPDATA%\NeonLink contains preferences, logs, crashes, backups, the
verified versioned cache, and an optional router-mapping ownership record.
Windows 10/11 x64 with .NET Framework 4.7.2 or later.
The launcher is WPF and the native mod is C++. No browser/server process is
required for the interface. source\Build.ps1 rebuilds the player executable;
source\runtime\Build-Runtime.ps1 rebuilds NeonLink.dll.

The previous private relay/join-code prototype is preserved separately in
the 0.6.0 project. It is not needed for this direct-hosting build.
Cyberpunk artwork copyright CD PROJEKT RED; see CREDITS.txt and licenses.
NeonLink is an independent fan project and uses no NightCoop code.
