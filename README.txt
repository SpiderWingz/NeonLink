REDLINE CLIENT 0.8.0 — MOVEMENT AND LOCAL PARTNER PREVIEW

Redline is the new application name. Your existing settings, backups, crash
reports, native plugin paths, and nlink:// invites remain compatible.

This build follows the selected direct-hosting approach. The host's PC runs
the native listener, and guests connect to its public IPv4 address or DNS
name. No rented server, relay, platform account, or paid networking service
is required. The host's IP address IS visible to peers.

QUICK START
1. Run Redline.exe. Find Cyberpunk 2077 and click Set up RED4ext + Redline.
2. Launch Cyberpunk and load a save. Open Host & join in the launcher.
3. Choose Create session. Automatic setup tries UPnP to find your public
   address and map the default TCP port. It reuses only a confirmed mapping
   already owned by Redline. It never replaces another application's mapping.
4. If automatic setup is unavailable, Connection details opens. Enter your
   public IP or DNS name and port, and forward that TCP port on your router.
5. A fresh random 256-bit key is generated for each new host session. You
   never type a key or password; it is included in the COMPLETE invite.
6. Click Copy session invite once the host is listening and share it privately.
7. Your guest installs the same build, launches and loads a save, copies your
   invite, then chooses Paste invite & join. Manual pasting is also available.

The launcher automatically listens on all local network adapters. Advanced
settings lets you choose a specific listen address or a different local port
for unusual router setups. Existing custom port mappings are preserved.
For a LAN test, advertise this PC's LAN address. 127.0.0.1 tests only
the same computer. Windows Firewall must allow the game's inbound TCP port.

ROUTER SETUP
Create session with automatic setup enabled requests a UPnP TCP mapping and
fills the public address returned by the router. Map port under Router details
also supports manual setup. Neither runs on startup. Existing mappings
are checked before adding; a recorded Redline mapping can be removed from
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
whole invite only with your intended guest. Create a new session to rotate it.

CURRENT MOD STATUS
The native DLL and desktop launcher are compiled development components.
Matching preview builds exchange player positions and orientations. A local
temporary NPC stands in for the other player's character; appearance and
walking animations are placeholders. The launcher reports connection and
actual character-spawn progress separately. Both players must load saves and
be within 150 metres of one another to see the proxy.
Shared host saves, NPCs, combat, vehicles, quests, rewards and progression
are unfinished. This is an experimental movement test, not campaign co-op.

ONE-PC TEST
1. Use Setup & repair, then launch Cyberpunk and load a save.
2. Stand in an open area. In Host & join, choose Start local partner test.
3. A simulated partner should appear nearby and move around you. The launcher
   must say Simulated partner spawned / local test after the script confirms it.
4. Stop local test removes the test partner. Returning to the menu removes it
   too. The test automatically ends after five minutes.
This mode opens no listening port and connects no other player. It tests the
game adapter; it does not prove that a second player joined your world.
Connection notifications also appear in the game. The proxy is nonpersistent.

ALL IN ONE
The EXE embeds official RED4ext 1.31.0, Codeware 1.21.2, redscript 0.5.32,
the Redline movement DLL and script, all six pictures, both silent video
backdrops, documentation, and component licenses.
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
Redline is an independent fan project and uses no NightCoop code.
