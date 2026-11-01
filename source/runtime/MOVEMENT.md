# Movement prototype: 0.8.0-dev.2

The native runtime exchanges player positions and orientations. A RED4ext/redscript adapter samples the local player and creates a temporary partner proxy through Codeware. The launcher embeds this adapter and its frameworks. **Live local tests now confirm player detection, proxy spawning, removal, and an encrypted simulated guest driving the proxy through the real game adapter. Two-PC play remains unverified; this is not campaign co-op.**

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
- Explicit game-thread reports distinguish a connected peer from an actually spawned proxy. The launcher does not call a simulated partner a real peer.
- A five-minute, single-PC partner test synthesizes a nearby pose through the same mailbox and script adapter. It opens no socket, sends no encrypted frames, and reports zero network peers.
- Simple session creation attempts UPnP only after the host presses Create session. Owned mappings can be reused after confirmation; conflicts open manual details. Each host start generates a new random 256-bit key inside the native DLL, carried in one copyable invite. Guests can paste and join in one action.

The placeholder is a local copy of `Character.Judy`, chosen in code rather than supplied by a peer. It does not reproduce the other player's appearance or walking animations. The service only displays it within 150 metres of the local player; it never teleports the local player or loads the host's save.

## Build and verify

Use Zig 0.15.2, libsodium 1.0.22 MinGW64, and the full RED4ext SDK at commit `5246e659d783e5834cc08a90e56c8932c9ed3449`. This SDK includes the Cyberpunk 2.5 callback and RTTI changes. The engine build declares the exact supported executable version, `3.0.88.46235`; transport-only builds remain loader-independent.

Official dependency URLs and archive hashes are recorded in [dependencies.json](dependencies.json). The all-in-one launcher now installs Codeware 1.21.2, redscript 0.5.32, the movement script, the native DLL, and RED4ext through an exact 19-file manifest. Existing INI/TOML configuration is preserved. Files replaced during setup are backed up before any changes.

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

**118 native/network checks passed:** 36 movement protocol/mailbox checks, 23 real-DLL movement integration checks, 21 existing native transport checks, and 38 direct-hosting checks. These cover automatic key freshness, ownership-preserving mapping reuse, and the local simulation's no-socket/no-peer invariants alongside encrypted bidirectional movement, world exit/reentry, stale updates, stop/rejoin, clean unloading, and fixture-injection guards. The protocol tests also cover 20,000 random malformed frames and concurrent mailbox access.

The gameplay script compiled successfully with redscript CLI 0.5.32 against the installed game bundle and Codeware 1.21.2 scripts. The CLI reports an upstream missing-return warning in Codeware's `IScriptable.Call` helper. The test runner also requires a newly created output bundle because this CLI can return exit code zero after a compilation error. Router planning uses a fake router; peer integrations use loopback.

Live startup on this PC confirmed Cyberpunk 2.5 / executable 3.0.88.46235, RED4ext 1.31.0 and Codeware 1.21.2 loading, successful game-side script compilation, native function registration, and the running-engine callback. All installed payload files were verified. The launcher copied and checksum-verified all existing saves before installation. This exposed and fixed an older incorrect Windows Saved Games GUID in the launcher.

Inside a loaded open-world save, the native bridge detected the player, the local simulated partner reached script-confirmed spawn stage 4, and the Judy placeholder was visible. STOP removed the proxy and displayed the removal notification. A separate native fixture then connected over authenticated loopback, received real player transforms, returned offset guest poses, and drove a spawned proxy for 20 seconds. Bidirectional encrypted movement counters advanced. The helper disconnected both sides afterward; native status returned to idle with no remote pose and the script returned to waiting stage 1. This used one real game instance and one simulated native guest, not two human players.

The first live fixture attempt exposed a transient sharing violation while reading its pose handoff file; the helper was corrected to skip a busy file and the complete 20-second run passed. No router or public internet endpoint was used. Save/reload persistence, distant sector streaming, death, long sessions, and cross-machine play still need live testing.

To repeat the opt-in live test, load a save in an open area, stop any existing session, and run `Test-LiveMovement.ps1 -GamePid <CyberpunkPID> -VerificationDirectory <completed-Test-Movement-output> -WorkDirectory <fresh-directory>`. The fixture refuses to replace an active connection, uses loopback only, and disconnects the session it created after 20 seconds. Keep the game unpaused so its script ticks can publish the player pose. A returned success means one real game and one simulated native guest passed the path; it does not certify internet or two-game-instance co-op.

## Next in-game milestone

Verify two Cyberpunk 2.5 instances on separate PCs, with the matching development DLL, scripts, and framework versions. Start with disposable save copies and both players in the same nearby open-world location. Confirm the service starts, each peer sees exactly one proxy, position and rotation follow movement, and proxies disappear after disconnect, loading another save, death, and returning to the menu. Exercise save/reload to verify no proxy persists. Review the existing local crash reports and runtime logs for failures.

The remaining live tests cover save/reload, death, menu transitions, long sessions, and two real game instances across machines. Avatar customization, locomotion animation, combat/damage authority, NPCs, vehicles, host-save loading, quests, dialogue, rewards, and campaign progression are subsequent work. `gameplayReplication` and the launcher's playable-co-op flag remain false.

## API references

- [RED4ext native redscript registration example](https://github.com/WopsS/RED4ext.SDK/blob/5246e659d783e5834cc08a90e56c8932c9ed3449/examples/native_globals_redscript/Main.cpp)
- [Codeware lifecycle and entity API documentation](https://github.com/psiberx/cp2077-codeware/wiki)
- [Codeware dynamic entity specification](https://github.com/psiberx/cp2077-codeware/blob/v1.21.2/scripts/World/DynamicEntitySpec.reds)

No NightCoop code or binaries are used.
