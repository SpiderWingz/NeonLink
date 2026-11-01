# Redline

A Windows launcher and native RED4ext transport prototype for **Cyberpunk 2077 patch 2.5**.

The application is now named **Redline** and uses the project owner's red-and-white RL logo for its desktop, taskbar, and window icons. Existing NeonLink settings, plugin paths, and invite formats are retained for compatibility.

**Development status:** the published launcher and two-peer connection tests work. Development source now includes a movement protocol and a compiled game adapter for a temporary partner character. In-game behavior is unverified; combat, vehicles, quests, progression, and save replication are not implemented. This is **not playable co-op yet**.

## Co-op development: 0.8.0-dev.1

The first movement prototype exchanges positions and orientations at 20 Hz, interpolates remote movement, and clears remote state after world exits, stale updates, or disconnects. Its RED4ext/redscript adapter creates a temporary, nonpersistent Codeware character proxy. The script compiles against Cyberpunk 2.5, but the proxy still needs a two-PC in-game test.

The prototype passed **108 automated checks** across movement, native transport, and direct hosting. See [movement development notes and build instructions](source/runtime/MOVEMENT.md). Development builds go to `artifacts/movement`; the published launcher and installer payload remain at their existing versions until gameplay validation is complete.

## Current launcher: 0.7.2

The main host form now asks only for **Host IP address** and **Port**. The launcher listens on all adapters and uses the same port locally by default. A different bind address or local port is available under **Advanced settings**. The native runtime remains version 0.7.0.

- One executable embeds RED4ext 1.31.0, NeonLink.dll, artwork, video backdrops, and licenses.
- Steam/GOG/Epic game detection, installation, checksum verification, repair, and backups.
- Direct hosting from the player's PC, with separate local and advertised public endpoints.
- IPv4/DNS invites, existing authenticated encryption, and stop/rejoin connection tests.
- Optional UPnP router mapping, local crash reports, and redacted diagnostic exports.

Direct hosting exposes the host's public IP. A reachable inbound TCP port is required; carrier-grade NAT can prevent hosting. Private six-character codes and additional relay/handshake work are deferred.

## Run a packaged build

Run `Redline.exe`, use **Setup & repair** to install the bundled runtime, launch Cyberpunk, then open **Host & join**. See [the player guide](README.txt) for port forwarding and connection-test instructions. Earlier NeonLink releases retain their original filenames.

## Build the launcher

Use Windows 10/11 x64 with .NET Framework 4.7.2 or later. From the repository root:

```powershell
.\source\Build.ps1 -BuildDirectory .\.build\launcher
```

The script uses the Windows .NET Framework C# compiler and writes `Redline.exe` at the repository root. It generates a multi-resolution Windows icon from `assets/redline-logo.png`. The versioned `assets`, `licenses`, and `payload` directories are build inputs; they are embedded in the generated executable. No dependency download is needed for this launcher rebuild.

To create a desktop shortcut with the Redline logo, run `source/Create-DesktopShortcut.ps1 -Executable <path-to-Redline.exe>` after placing the executable in its permanent location.

Package the built player executable and documentation:

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
.\source\Package.ps1 -Destination .\artifacts\Redline-0.7.2.zip
```

## Rebuild the native DLL

The stable compiled DLL is included as a packaging input. To build the development runtime, obtain portable [Zig 0.15.2](https://ziglang.org/download/0.15.2/), [libsodium 1.0.22-stable MinGW64](https://download.libsodium.org/libsodium/releases/libsodium-1.0.22-stable-mingw.tar.gz), and the pinned RED4ext SDK listed in the movement notes, then run:

```powershell
.\source\runtime\Build-Runtime.ps1 `
  -Zig C:\Tools\zig\zig.exe `
  -SodiumRoot C:\Tools\libsodium-win64 `
  -SdkRoot C:\Tools\RED4ext.SDK `
  -CacheDirectory .\.build\native
```

The native build writes `artifacts/movement/NeonLink.dll`. The stable `payload` is unchanged. See [movement development notes](source/runtime/MOVEMENT.md) for script dependencies and the repeatable test suite; [native runtime notes](RUNTIME-README.txt) describe the published transport build.

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

Redline is an independent fan project. Cyberpunk artwork is copyright CD PROJEKT RED; see [credits](CREDITS.txt). Third-party license texts are included in `licenses/` and `payload/red4ext/`. No NightCoop code or binaries are included.
