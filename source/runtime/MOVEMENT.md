# Movement prototype: 0.8.0-dev.1

The native source now exchanges player positions and orientations. A RED4ext/redscript adapter samples the local player and creates a temporary partner proxy through Codeware. **The adapter compiles but has not been run inside Cyberpunk. This is not a playable co-op release.** The published launcher still embeds the tested 0.7.0 transport DLL.

## Implemented

- One host and one guest, using the existing direct-IP encrypted connection.
- Explicit movement capability/version negotiation after authentication. Both peers need the development DLL; 0.7.0 is incompatible with movement sessions.
- Fixed 60-byte movement messages, sent at 20 Hz. No game object pointers, entity IDs, record names, file paths, or script commands are accepted from the network.
- Finite, bounded position and normalized orientation validation; malformed lengths, flags, versions, duplicate sequences, and regressing clocks reject the session.
- A 32-sample maximum mailbox, 100 ms interpolation, quaternion short-arc interpolation, and teleport/world-generation resets. Network workers never access engine objects.
- Explicit world-exit messages, one-second stale-sample expiry, disconnect cleanup, and fresh sequence state on reconnect.
- A game-thread adapter that publishes the player pose and updates a locally owned, tagged proxy near the player. Spawning is bounded to one pending entity, with a timeout and retry delay.
- Proxies use `persistSpawn=false` and `persistState=false`. The script removes its proxy on disconnect/world exit and disables its AI, kill rewards, and ragdoll behavior.
- Additional native status fields for bridge registration, local/remote pose availability, movement counters, and movement protocol version.

The placeholder is a local copy of `Character.Judy`, chosen in code rather than supplied by a peer. It does not reproduce the other player's appearance or walking animations. The service only displays it within 150 metres of the local player; it never teleports the local player or loads the host's save.

## Build and verify

Use Zig 0.15.2, libsodium 1.0.22 MinGW64, and the full RED4ext SDK at commit `5246e659d783e5834cc08a90e56c8932c9ed3449`. This SDK includes the Cyberpunk 2.5 callback and RTTI changes. The engine build declares the exact supported executable version, `3.0.88.46235`; transport-only builds remain loader-independent.

Official dependency URLs and archive hashes are recorded in [dependencies.json](dependencies.json). The gameplay script additionally requires Codeware 1.21.2 and redscript 0.5.32. These are development dependencies and have not been added to the published launcher's installer yet.

Run from the repository root, choosing a fresh fixture directory:

```powershell
.\source\runtime\Test-Movement.ps1 `
  -Zig C:\Tools\zig\zig.exe `
  -SodiumRoot C:\Tools\libsodium-win64 `
  -SdkRoot C:\Tools\RED4ext.SDK `
  -CacheDirectory .\.build\native-cache `
  -WorkDirectory .\.build\movement-checks `
  -GameBundle 'D:\Games\Cyberpunk 2077\r6\cache\final.redscripts' `
  -CodewareScripts 'C:\Tools\Codeware\red4ext\plugins\Codeware\Scripts' `
  -RedscriptCli C:\Tools\redscript-cli.exe
```

The optional final three arguments compile the adapter against a read-only game script bundle. Compiler output stays in the fixture directory and must not be redistributed, because that validation bundle contains game scripts. The test script does not install mods, launch Cyberpunk, contact a real router, or change saves.

To build only the native development DLL:

```powershell
.\source\runtime\Build-Runtime.ps1 `
  -Zig C:\Tools\zig\zig.exe `
  -SodiumRoot C:\Tools\libsodium-win64 `
  -SdkRoot C:\Tools\RED4ext.SDK `
  -OutputPath .\artifacts\movement\NeonLink.dll
```

Omitting `SdkRoot` produces a transport/movement test DLL without game bindings. Builds default to `artifacts/movement/NeonLink.dll` so they do not replace the stable installer payload. The script source belongs next to an engine-enabled DLL at `red4ext/plugins/NeonLink/Scripts/NeonLink.reds`; native startup registers that directory with RED4ext.

## Verification on November 1, 2026

**108 automated checks passed:** 36 movement protocol/mailbox checks, 18 real-DLL movement integration checks, 21 existing native transport checks, and 33 direct-hosting checks. Integration runs loaded the actual engine-enabled DLL into separate isolated processes and exercised encrypted bidirectional position delivery, movement changes, world exit/reentry, stale local updates, stop/rejoin, clean unloading, and the normal-load fixture-injection guard. The protocol tests also covered 20,000 random malformed frames and concurrent mailbox access.

The gameplay script compiled successfully with redscript CLI 0.5.32 against the installed game bundle and Codeware 1.21.2 scripts. The compiler reported one existing missing-return warning in Codeware's `IScriptable.Call` helper; no NeonLink script errors were reported. Router planning used a fake router, and all peer connections used loopback.

## Next in-game milestone

Verify two Cyberpunk 2.5 instances on separate PCs, with the matching development DLL, scripts, and framework versions. Start with disposable save copies and both players in the same nearby open-world location. Confirm the service starts, each peer sees exactly one proxy, position and rotation follow movement, and proxies disappear after disconnect, loading another save, death, and returning to the menu. Exercise save/reload to verify no proxy persists. Review the existing local crash reports and runtime logs for failures.

This milestone still needs in-game validation of native registration, service lifecycle, proxy spawning, teleport smoothing, and AI disabling. Avatar customization, locomotion animation, combat/damage authority, NPCs, vehicles, host-save loading, quests, dialogue, rewards, and campaign progression are subsequent work. `gameplayReplication` and the launcher's playable-co-op flag remain false.

## API references

- [RED4ext native redscript registration example](https://github.com/WopsS/RED4ext.SDK/blob/5246e659d783e5834cc08a90e56c8932c9ed3449/examples/native_globals_redscript/Main.cpp)
- [Codeware lifecycle and entity API documentation](https://github.com/psiberx/cp2077-codeware/wiki)
- [Codeware dynamic entity specification](https://github.com/psiberx/cp2077-codeware/blob/v1.21.2/scripts/World/DynamicEntitySpec.reds)

No NightCoop code or binaries are used.
