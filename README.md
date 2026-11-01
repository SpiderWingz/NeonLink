# NeonLink

A Windows launcher and native RED4ext transport prototype for **Cyberpunk 2077 patch 2.5**.

**Development status:** the launcher and two-peer connection tests work. Player movement, avatars, combat, vehicles, quests, progression, and save replication are not implemented. This is **not playable co-op yet**.

## Current build: 0.7.0

- One executable embeds RED4ext 1.31.0, NeonLink.dll, artwork, video backdrops, and licenses.
- Steam/GOG/Epic game detection, installation, checksum verification, repair, and backups.
- Direct hosting from the player's PC, with separate local and advertised public endpoints.
- IPv4/DNS invites, existing authenticated encryption, and stop/rejoin connection tests.
- Optional UPnP router mapping, local crash reports, and redacted diagnostic exports.

Direct hosting exposes the host's public IP. A reachable inbound TCP port is required; carrier-grade NAT can prevent hosting. Private six-character codes and additional relay/handshake work are deferred.

## Run a packaged build

Download the 0.7.0 executable or ZIP from this repository's Releases. Run `NeonLink.exe`, use **Setup & repair** to install the bundled runtime, launch Cyberpunk, then open **Host & join**. See [the player guide](README.txt) for port forwarding and connection-test instructions.

## Build the launcher

Use Windows 10/11 x64 with .NET Framework 4.7.2 or later. From the repository root:

```powershell
.\source\Build.ps1 -BuildDirectory .\.build\launcher
```

The script uses the Windows .NET Framework C# compiler and writes `NeonLink.exe` at the repository root. The versioned `assets`, `licenses`, and `payload` directories are build inputs; they are embedded in the generated executable. No dependency download is needed for this launcher rebuild.

Package the built player executable and documentation:

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
.\source\Package.ps1 -Destination .\artifacts\NeonLink-0.7.0.zip
```

## Rebuild the native DLL

The current compiled DLL is included as a packaging input. To rebuild it, obtain portable [Zig 0.15.2](https://ziglang.org/download/0.15.2/) and [libsodium 1.0.22-stable MinGW64](https://download.libsodium.org/libsodium/releases/libsodium-1.0.22-stable-mingw.tar.gz), then run:

```powershell
.\source\runtime\Build-Runtime.ps1 `
  -Zig C:\Tools\zig\zig.exe `
  -SodiumRoot C:\Tools\libsodium-win64 `
  -CacheDirectory .\.build\native
.\source\Build.ps1 -BuildDirectory .\.build\launcher
```

The native build updates `payload/red4ext/plugins/NeonLink/NeonLink.dll`. See [native runtime notes](RUNTIME-README.txt).

## Verification and project layout

The 0.7.0 build passed **206 local checks** covering direct transport, native loading, launcher logic, bundle integrity, installation/repair, and crash reporting. UPnP used a fake router; cross-network internet reachability and behavior inside Cyberpunk remain unverified. See [test results](TEST-RESULTS.txt).

| Path | Purpose |
| --- | --- |
| `source/` | WPF launcher, installer, diagnostics, and test harnesses |
| `source/runtime/` | Native C++ plugin, RED4ext API headers, build and integration harness |
| `assets/` | Artwork and backdrop packaging inputs |
| `payload/` | Versioned RED4ext and NeonLink binaries embedded by the launcher build |
| `licenses/` | Third-party component license texts |

Local caches, personal settings, saves, crash logs, compiler dependencies, and generated player executables are not part of the source repository.

## Credits

NeonLink is an independent fan project. Cyberpunk artwork is copyright CD PROJEKT RED; see [credits](CREDITS.txt). Third-party license texts are included in `licenses/` and `payload/red4ext/`. No NightCoop code or binaries are included.
