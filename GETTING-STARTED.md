# Using Redline 0.8.1

Redline currently previews player movement, names and map markers. Shared quests, NPCs, combat, vehicles, rewards and host-save synchronization are unfinished. Remote characters still use a temporary placeholder appearance.

## Set up the mod

1. Download `Redline-Player-Preview-0.8.1-dev.1.zip` from the [preview release](https://github.com/SpiderWingz/NeonLink/releases/tag/v0.8.1-dev.1). Extract the ZIP into a normal folder, then run **Redline.exe**. The source-code ZIP is for development, not installation.
2. Open **Setup & repair**. Choose **Find my game**, or use **Browse...** to select `Cyberpunk2077.exe` inside the game's `bin/x64` folder. This build requires Windows x64 and Cyberpunk 2077 **2.5**.
3. Close Cyberpunk before installing or repairing. Click **Install / repair RED4ext + Redline**. If the launcher already says the game is set up, continue. RED4ext, Codeware, redscript and Redline are included; you do not need to copy the DLL yourself. Replaced files are backed up.
4. Open **Play game** and click **Launch Cyberpunk 2077**. The large **PLAY REDLINE** button remains disabled while campaign co-op is unfinished.
5. Load **your own save**, then stand in an open area. Your own V, inventory, cyberware, perks and skills stay in your local game. Joining does not load a host's save or import their character.
6. Return to Redline, open **Host & join**, enter your name under **Your player**, and click **Save name**. Hosts and guests both choose names. You see your partner's name; your partner sees yours.

## Test it with one PC

1. Complete the setup above and load a save.
2. In **Host & join**, expand **Test a partner on this PC**.
3. Click **Start local partner test**.
4. Return to the game and leave it unpaused. Look around for the temporary partner named **Test partner**. Its test position may be beside you or outside your current camera view.
5. Press **M** to open the world map and inspect the partner marker. The minimap also shows its position. The overhead name is limited to 80 metres and the character proxy to 150 metres.
6. Return to Redline and click **Stop local test**. This removes the partner and marker. The test also ends automatically after five minutes.

This mode does not connect another person or open a server port. It verifies the local game adapter, not internet co-op.

## Connect with a friend

Both PCs need Cyberpunk 2077 2.5 and the **same Redline 0.8.1 preview**. Earlier 0.8.0 builds cannot connect using this version's player protocol.

### Host

1. Launch the game, load your own save and choose your name as above.
2. In **Host & join**, leave **Automatically set up my router** selected and click **Create session**.
3. Wait until the launcher says **Hosting / waiting for a peer**. A new random 256-bit session key is generated automatically.
4. Click **Copy session invite** and privately send the complete invite to your friend. It contains the address, port and key. This preview uses a full invite, not a six-character join code.
5. If automatic router setup fails, use **Connection details (manual)**. Enter your public IP or DNS name and the forwarded TCP port; the default is **47890**. That TCP port must reach this PC, and Windows Firewall must allow the game connection. Carrier-grade NAT or double NAT can prevent hosting. No real-router/internet connection has been verified by the local tests.

### Guest

1. Launch the game and load **your own save**. Set your name in the launcher.
2. Copy the host's complete invite.
3. In **Host & join**, click **Paste invite & join**.
4. Return to the game and stand near the same location as the host. The preview does not teleport you to the host or load their save.
5. Check the launcher. **Peer connected** confirms transport; **Partner character spawned / movement preview** confirms the adapter spawned the temporary partner. Expand **Character summaries** to inspect each peer's read-only character summary.

The marker can show a distant partner even when their proxy is outside the 150-metre spawn range. The map menu pauses this single-player preview's script clock, so marker movement resumes after leaving the map. A real two-PC/internet session still needs testing.

Direct hosting reveals the host's public IP to the guest. The connection is encrypted, but encryption does not hide the endpoints. Share invites only with intended guests.

## Stop or troubleshoot

- Click **Stop session** to end your connection. Use **Router details** to remove a Redline router mapping when finished hosting.
- If the runtime is missing or old, close the game, use **Setup & repair**, then relaunch and load a save.
- If a peer connects but no character appears, make sure both games have loaded saves, are unpaused, and the players are within 150 metres at the same map location. Check both sides use the same build.
- If joining fails, create a fresh host session, copy its entire new invite, and check public-address/port reachability. A local partner test cannot verify the router or internet path.
- **Diagnostics** provides save backups and local crash reports. Reports are not automatically uploaded.

## First live test with another user

Before arranging a session, send your tester the release ZIP. This repository is private: testers need repository access to download from GitHub, or you can send them the ZIP yourself. On each PC, use **Diagnostics → Back up saves**, close the game, install this same build and restart it.

1. Pick the same nearby open-world location in both saves. Avoid story objectives during this movement test; the preview does not synchronize campaign state.
2. Follow the host and guest instructions above. Check that both launchers report a connected peer, then a spawned partner.
3. Confirm that each person sees exactly one partner with the other person's chosen name.
4. Have the guest walk, turn and jump while the host watches. Swap roles for the movement check. The placeholder can glide because locomotion animations are unfinished.
5. Open the minimap/world map and check the partner marker and its named tooltip. Close the map to resume script updates.
6. Have one person move beyond 150 metres. The nearby character proxy should be removed while its map marker remains available. Move close again and check the proxy returns.
7. Have the guest choose **Stop session**. Confirm that the host's partner character, overhead name and marker disappear. Copy the current host invite and join again to test reconnecting.
8. Stop the session on both sides when done. Record both Redline versions, LAN versus internet, the last successful step and any exact error. Use the repository's **Two-PC preview test** issue template and attach a redacted diagnostic/crash report if needed. Do not post full invites, session keys or public IPs.

Passing these checks verifies the preview path between those two PCs. It does not establish shared world, combat or campaign correctness.
