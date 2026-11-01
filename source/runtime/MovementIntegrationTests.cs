using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NeonLink;

static class MovementIntegrationTests
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAILED " + name); checks++; Console.WriteLine("PASS " + name); }
    static void Wait(Func<bool> ready, string detail)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline) { try { if (ready()) return; } catch (IOException) { } Thread.Sleep(30); }
        throw new Exception("Timeout: " + detail);
    }
    static Process Spawn(string dll, string ready, bool strict)
    {
        var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
            (strict ? "--serve-strict" : "--serve") + " \"" + dll + "\" \"" + ready + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.EnvironmentVariables["NEONLINK_TEST_LOG_ROOT"] = Path.Combine(Path.GetDirectoryName(ready), "logs");
        return Process.Start(start);
    }
    static int Ready(string ready, Process process)
    {
        Wait(() => File.Exists(ready) || process.HasExited, "native process startup");
        if (process.HasExited) throw new Exception(process.StandardError.ReadToEnd());
        return Int32.Parse(File.ReadAllText(ready));
    }
    static bool Sample(string ready, float x, float y, float z)
    {
        if (!File.Exists(ready + ".sample")) return false;
        string value = File.ReadAllText(ready + ".sample");
        if (value == "absent" || value.Split(',').Length != 7) return false;
        float[] numbers = value.Split(',').Select(v => Single.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return Math.Abs(numbers[0] - x) < 0.01 && Math.Abs(numbers[1] - y) < 0.01 && Math.Abs(numbers[2] - z) < 0.01;
    }
    static void Shutdown(Process process, string ready)
    {
        if (process == null) return;
        File.WriteAllText(ready + ".stop", "stop");
        if (!process.WaitForExit(8000)) { process.Kill(); process.WaitForExit(); throw new Exception("Native fixture failed to unload promptly"); }
        Check(process.ExitCode == 0, "Native movement fixture unloads cleanly"); process.Dispose();
    }
    public static int Run(string dll, string root)
    {
        Directory.CreateDirectory(root); string hostReady = Path.Combine(root, "host"), guestReady = Path.Combine(root, "guest"), strictReady = Path.Combine(root, "strict");
        Process host = null, guest = null, strict = null;
        try
        {
            host = Spawn(dll, hostReady, false); guest = Spawn(dll, guestReady, false);
            int hp = Ready(hostReady, host), gp = Ready(guestReady, guest);
            Check(RuntimeClient.Command(hp, "STATUS").movementProtocol == 1, "Compiled DLL exposes movement protocol version");
            Check(!RuntimeClient.Command(hp, "STATUS").gameplayReplication, "Incomplete gameplay remains clearly marked");
            File.WriteAllText(hostReady + ".pose", "-100,2000,20,0,0,0,1");
            File.WriteAllText(guestReady + ".pose", "-101,2001,21,0,0,0.6,0.8");
            Wait(() => RuntimeClient.Command(hp, "STATUS").localPlayerReady && RuntimeClient.Command(gp, "STATUS").localPlayerReady, "local position publication");
            Check(true, "Both local game adapters publish to bounded native mailboxes");
            RuntimeClient.Command(hp, "HOSTDIRECT 127.0.0.1 0 localhost 47890");
            Wait(() => RuntimeClient.Command(hp, "STATUS").phase == "hosting", "host listener");
            var hs = RuntimeClient.Command(hp, "STATUS"); string key = new Uri(hs.invite).AbsolutePath.Trim('/');
            RuntimeClient.Command(gp, "JOINDIRECT 127.0.0.1 " + hs.localPort + " " + key);
            Wait(() => RuntimeClient.Command(hp, "STATUS").phase == "connected" && RuntimeClient.Command(gp, "STATUS").phase == "connected", "encrypted connection");
            Check(true, "Real DLLs authenticate and negotiate movement capabilities");
            Wait(() => Sample(hostReady, -101, 2001, 21) && Sample(guestReady, -100, 2000, 20), "bidirectional movement payloads");
            Check(true, "Positions survive both encrypted native transport directions");
            File.WriteAllText(hostReady + ".pose", "-50,2050,30,0,0,0,1");
            Wait(() => Sample(guestReady, -50, 2050, 30), "teleport replication");
            Check(true, "Changed player position reaches the partner");
            Wait(() => RuntimeClient.Command(hp, "STATUS").movementReceived > 15 && RuntimeClient.Command(gp, "STATUS").movementReceived > 15, "sustained updates");
            hs = RuntimeClient.Command(hp, "STATUS"); var gs = RuntimeClient.Command(gp, "STATUS");
            Check(hs.remotePlayerReady && gs.remotePlayerReady && hs.movementSent > 15 && gs.movementSent > 15, "Both peers exchange sustained movement updates");
            File.WriteAllText(hostReady + ".pose", "clear");
            Wait(() => !RuntimeClient.Command(gp, "STATUS").remotePlayerReady, "remote world exit");
            Check(File.ReadAllText(hostReady + ".accepted") == "True", "Fixture publication uses the real native boundary");
            Check(true, "Leaving a game world clears the partner while keeping transport alive");
            Check(RuntimeClient.Command(gp, "STATUS").phase == "connected", "World exit does not disconnect the session");
            File.WriteAllText(hostReady + ".pose", "-20,2000,23,0,0,0,1");
            Wait(() => Sample(guestReady, -20, 2000, 23), "world reentry");
            Check(true, "Player can reenter a world in the same connection");
            RuntimeClient.Command(gp, "STOP");
            Wait(() => !RuntimeClient.Command(hp, "STATUS").remotePlayerReady && !RuntimeClient.Command(gp, "STATUS").remotePlayerReady, "disconnect cleanup");
            Check(true, "Stopping a peer removes remote state on both sides");
            Wait(() => RuntimeClient.Command(hp, "STATUS").phase == "hosting", "host recovery");
            RuntimeClient.Command(gp, "JOINDIRECT 127.0.0.1 " + hs.localPort + " " + key);
            Wait(() => Sample(hostReady, -101, 2001, 21) && Sample(guestReady, -20, 2000, 23), "reconnection state reset");
            Check(true, "Reconnection resets sequence state and restores movement");
            File.Delete(hostReady + ".pose");
            Wait(() => !RuntimeClient.Command(gp, "STATUS").remotePlayerReady, "stale local sample expiry");
            Check(true, "A stalled game adapter cannot keep a ghost partner alive");
            File.WriteAllText(strictReady + ".pose", "1,2,3,0,0,0,1");
            strict = Spawn(dll, strictReady, true); int sp = Ready(strictReady, strict);
            Wait(() => File.Exists(strictReady + ".accepted"), "strict injection result");
            Check(File.ReadAllText(strictReady + ".accepted") == "False" && !RuntimeClient.Command(sp, "STATUS").localPlayerReady, "Fixture injection is disabled in a normally loaded plugin");
            bool previewBlocked = false;
            try { RuntimeClient.Command(hp, "PREVIEW"); } catch (InvalidOperationException) { previewBlocked = true; }
            Check(previewBlocked && RuntimeClient.Command(hp, "STATUS").phase == "connected", "A local test cannot silently replace a real peer connection");
            RuntimeClient.Command(gp, "STOP"); RuntimeClient.Command(hp, "STOP");
            File.WriteAllText(hostReady + ".pose", "100,2000,20,0,0,0,1");
            Wait(() => RuntimeClient.Command(hp, "STATUS").localPlayerReady, "preview local sample");
            RuntimeClient.Command(hp, "PREVIEW");
            Wait(() => RuntimeClient.Command(hp, "STATUS").previewSamples > 3 && RuntimeClient.Command(hp, "STATUS").remotePlayerReady, "simulated partner sample");
            hs = RuntimeClient.Command(hp, "STATUS");
            Check(hs.preview && hs.phase == "preview" && hs.peers == 0 && hs.localPort == 0 && hs.sent == 0 && hs.received == 0 && String.IsNullOrEmpty(hs.invite), "Preview opens no listener and never claims network traffic or a peer");
            Wait(() => {
                string[] parts = File.ReadAllText(hostReady + ".sample").Split(',');
                float x, y;
                return parts.Length == 7 && Single.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    Single.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && x >= 102.9 && x <= 105.1 && Math.Abs(y - 2000) <= 1.1;
            }, "simulated partner coordinates");
            Check(true, "Simulated partner stays near the local test character");
            File.WriteAllText(hostReady + ".pose", "clear");
            Wait(() => !RuntimeClient.Command(hp, "STATUS").remotePlayerReady, "preview world exit");
            Check(RuntimeClient.Command(hp, "STATUS").preview, "World exit removes the simulated character without pretending a disconnect");
            RuntimeClient.Command(hp, "STOP");
            hs = RuntimeClient.Command(hp, "STATUS");
            Check(!hs.preview && !hs.remotePlayerReady && hs.phase == "idle", "Stopping a local test clears its partner state immediately");
        }
        finally { Shutdown(host, hostReady); Shutdown(guest, guestReady); Shutdown(strict, strictReady); }
        Console.WriteLine(checks + " native movement integration checks passed"); return 0;
    }
}
