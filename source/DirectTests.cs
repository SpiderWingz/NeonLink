using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NeonLink;

namespace NeonLink
{
    public static class DirectTests
    {
        static int count;
        static string root;
        static void Check(bool value, string name) { if (!value) throw new Exception("Direct check failed: " + name); count++; File.AppendAllText(Path.Combine(root, "checks.txt"), "PASS " + name + Environment.NewLine); }
        static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
        sealed class Router : IRouterMappings
        {
            public RouterMapping Current;
            public int Adds, Deletes;
            public bool DisableResult;
            public RouterMapping Find(int port) { return Current != null && Current.ExternalPort == port ? Current : null; }
            public RouterMapping Add(RouterMapping wanted)
            {
                Adds++; Current = new RouterMapping { Description = wanted.Description, ExternalPort = wanted.ExternalPort, InternalPort = wanted.InternalPort, InternalAddress = wanted.InternalAddress, Protocol = "TCP", Enabled = !DisableResult, ExternalAddress = "203.0.113.19" }; return Current;
            }
            public void Remove(int port) { if (Current != null && Current.ExternalPort == port) { Current = null; Deletes++; } }
            public void Dispose() { }
        }
        static void Helpers()
        {
            Check(InternetHosting.HostCommand("0.0.0.0", "47890", "relay.example.com", "48900") == "HOSTDIRECT 0.0.0.0 47890 relay.example.com 48900", "Listen and advertised endpoints remain separate");
            string key = new string('a', 64);
            var invite = InternetHosting.ParseInvite("nlink://HOST.example.com:47890/" + key);
            Check(invite.Host == "host.example.com" && invite.Key == key && invite.Port == 47890, "DNS invite parsing preserves the authenticated key");
            Check(InternetHosting.ResolveIPv4("localhost").GetAwaiter().GetResult() == "127.0.0.1", "Guest resolves DNS to an IPv4 target");
            foreach (string text in new[] { "nlink://0.0.0.0:47890/" + key, "nlink://127.0.0.1:47890/short", "nlink://user@host.example.com:47890/" + key, "nlink://host.example.com:47890/" + key + "?extra=x", "nlink://host.example.com:47890/" + key + "/", "nlink://[::1]:47890/" + key })
                Reject(() => InternetHosting.ParseInvite(text), "Malformed or unsupported invite rejected");
            foreach (string port in new[] { "0", "65536", "-1", "abc" }) Reject(() => InternetHosting.Port(port), "Invalid port rejected");
            Check(!InternetHosting.PublicAddress("192.168.1.4") && !InternetHosting.PublicAddress("100.64.1.1") && InternetHosting.PublicAddress("8.8.8.8"), "LAN and carrier-grade-NAT addresses produce a warning");
            var router = new Router();
            var mapped = PortMappingManager.Create(router, "192.168.1.4", 47890, 48900);
            Check(mapped.ExternalPort == 48900 && mapped.InternalPort == 47890 && mapped.ExternalAddress == "203.0.113.19", "UPnP planner preserves distinct internal/external ports");
            Check(PortMappingManager.Reuse(router, mapped, "192.168.1.4", 47890, 48900) == mapped && router.Adds == 1, "Automatic hosting reuses a confirmed owned mapping without adding another");
            Reject(() => PortMappingManager.Reuse(router, mapped, "192.168.1.5", 47890, 48900), "Automatic hosting cannot reuse a mapping for another PC");
            Reject(() => PortMappingManager.Reuse(router, mapped, "192.168.1.4", 47891, 48900), "Automatic hosting cannot silently reuse the wrong local port");
            Reject(() => PortMappingManager.Create(router, "192.168.1.4", 47890, 48900), "Existing mappings are never deliberately overwritten");
            Check(router.Adds == 1, "Conflicting mapping is rejected before router Add");
            var altered = new RouterMapping { Description = "Other application", ExternalPort = 48900, InternalPort = 47890, InternalAddress = "192.168.1.4" };
            router.Current = altered;
            Reject(() => PortMappingManager.Remove(router, mapped), "Changed mapping is left untouched");
            Check(router.Deletes == 0, "Unrelated mapping is never removed");
            Reject(() => PortMappingManager.Reuse(router, mapped, "192.168.1.4", 47890, 48900), "Automatic hosting never takes ownership of another application's mapping");
            router.Current = mapped; PortMappingManager.Remove(router, mapped);
            Check(router.Current == null && router.Deletes == 1, "Owned mapping can be removed");
            var broken = new Router { DisableResult = true };
            Reject(() => PortMappingManager.Create(broken, "192.168.1.4", 47890, 48900), "Router confirmation failure is rejected");
            Check(broken.Current == null && broken.Deletes == 1, "Failed own mapping is rolled back");
        }
        static Process Spawn(string harness, string dll, string ready)
        {
            var info = new ProcessStartInfo(harness, "--serve-strict \"" + dll + "\" \"" + ready + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            return Process.Start(info);
        }
        static int Ready(Process process, string path)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(path) && !process.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(40);
            if (!File.Exists(path)) throw new Exception("Native direct fixture failed to start.");
            return Int32.Parse(File.ReadAllText(path));
        }
        static RuntimeStatus Phase(int pid, string phase)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8); RuntimeStatus state = null;
            while (DateTime.UtcNow < deadline) { state = RuntimeClient.Command(pid, "STATUS"); if (state.phase == phase) return state; Thread.Sleep(40); }
            throw new Exception("Expected " + phase + ", got " + (state == null ? "no status" : state.phase));
        }
        public static int Run(string folder, string harness, string dll)
        {
            root = folder; Directory.CreateDirectory(root); Helpers();
            string hf = Path.Combine(root, "host.ready"), gf = Path.Combine(root, "guest.ready"); Process host = null, guest = null;
            try
            {
                host = Spawn(harness, dll, hf); guest = Spawn(harness, dll, gf);
                int hp = Ready(host, hf), gp = Ready(guest, gf);
                foreach (string command in new[] { "HOSTDIRECT 127.0.0.1 0 0.0.0.0 47890", "HOSTDIRECT 127.0.0.1 0 bad-.example.com 47890", "HOSTDIRECT 127.0.0.1 0 999.999.999.999 47890", "HOSTDIRECT 127.0.0.1 0 example.com 65536", "JOINDIRECT 0.0.0.0 47890 " + new string('a', 64) })
                    Reject(() => RuntimeClient.Command(hp, command), "Invalid native direct command rejected");
                RuntimeClient.Command(hp, "HOSTDIRECT 0.0.0.0 0 203.0.113.19 48900");
                var listening = Phase(hp, "hosting"); var invite = InternetHosting.ParseInvite(listening.invite);
                Check(listening.localPort > 0 && invite.Host == "203.0.113.19" && invite.Port == 48900, "Native host binds locally while advertising a non-local public endpoint");
                RuntimeClient.Command(gp, "JOINDIRECT 127.0.0.1 " + listening.localPort + " " + new string('0', 64));
                Check(Phase(gp, "rejected").peers == 0, "Direct guest with wrong key is rejected"); Phase(hp, "hosting");
                RuntimeClient.Command(gp, "JOINDIRECT 127.0.0.1 " + listening.localPort + " " + invite.Key);
                Check(Phase(hp, "connected").peers == 1 && Phase(gp, "connected").peers == 1, "Normally loaded host and guest connect directly without a relay");
                Thread.Sleep(1800); var hs = RuntimeClient.Command(hp, "STATUS"); var gs = RuntimeClient.Command(gp, "STATUS");
                Check(hs.sent >= 2 && hs.received >= 2 && gs.sent >= 2 && gs.received >= 2, "Encrypted authenticated transport is preserved");
                RuntimeClient.Command(gp, "STOP"); Phase(hp, "hosting");
                RuntimeClient.Command(gp, "JOINDIRECT 127.0.0.1 " + listening.localPort + " " + invite.Key);
                Check(Phase(gp, "connected").peers == 1, "Direct guest can reconnect to the same invite");
                RuntimeClient.Command(gp, "STOP"); RuntimeClient.Command(hp, "STOP");
                Check(Phase(hp, "idle").peers == 0, "Direct host stops cleanly");
                RuntimeClient.Command(hp, "HOSTDIRECT 0.0.0.0 0 203.0.113.19 48900");
                var fresh = InternetHosting.ParseInvite(Phase(hp, "hosting").invite);
                Check(fresh.Key.Length == 64 && fresh.Key != invite.Key && fresh.Key.Any(c => c != '0'), "Every newly created host session receives a fresh automatic 256-bit key");
                RuntimeClient.Command(hp, "STOP"); Phase(hp, "idle");
            }
            finally
            {
                File.WriteAllText(hf + ".stop", "stop"); File.WriteAllText(gf + ".stop", "stop");
                foreach (var process in new[] { host, guest }.Where(x => x != null))
                { if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new Exception("Native direct fixture did not unload cleanly."); process.Dispose(); }
            }
            File.WriteAllText(Path.Combine(root, "result.txt"), count + " direct-hosting checks passed.\r\nUPnP tested with a fake router only. No real router/firewall/game was changed. Internet reachability needs verification on separate networks.\r\n");
            return 0;
        }
    }
}
