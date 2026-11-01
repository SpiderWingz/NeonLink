using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace NeonLink
{
    public static class CrashReportTests
    {
        static int checks;
        static void Check(bool value, string message) { if (!value) throw new Exception("Crash-report check failed: " + message); checks++; }
        static GameSession Fake(int pid)
        {
            var session = new GameSession { Pid = pid, StartUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks, GameRoot = "", GameVersion = "fixture", ClientVersion = "0.4.1-test", RuntimeVersion = "0.4.1", ProcessName = "CrashFixture" };
            session.Id = CrashReports.Key(pid, session.StartUtcTicks); return session;
        }
        public static int StartFixture(string fixture, string root, string code)
        {
            CrashReports.TestRoot = root;
            using (var process = Process.Start(new ProcessStartInfo(fixture, code) { UseShellExecute = false, CreateNoWindow = true }))
            {
                string key = CrashReports.Key(process.Id, process.StartTime.ToUniversalTime().Ticks);
                CrashReports.Attach(process, "");
                File.WriteAllText(Path.Combine(root, "starter-session.txt"), key);
            }
            return 0; // The watcher must continue after this launcher process exits.
        }
        static void Wait(Func<bool> condition, string name)
        {
            var timeout = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < timeout)
            {
                try { if (condition()) { Check(true, name); return; } } catch (IOException) { }
                Thread.Sleep(100);
            }
            Check(false, name);
        }
        static string Starter(string root, string fixture, string code)
        {
            Directory.CreateDirectory(root);
            var info = new ProcessStartInfo(typeof(CrashReportTests).Assembly.Location,
                "--crash-test-starter \"" + fixture + "\" \"" + root + "\" " + code) { UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(info))
            { if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new Exception("Crash-test starter failed."); }
            return File.ReadAllText(Path.Combine(root, "starter-session.txt"));
        }
        public static int Run(string root, string fixture)
        {
            CrashReports.TestRoot = root; Directory.CreateDirectory(root);
            string key = new string('a', 64);
            string scrubbed = CrashReports.Scrub("nlink://127.0.0.1:47890/" + key + " key=" + key + " " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "");
            Check(!scrubbed.Contains(key) && !scrubbed.Contains("nlink://"), "Invite and standalone key are redacted");
            Check(!scrubbed.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "User profile path is redacted");
            string privateSession = CrashReports.Scrub("join code: AB2CD3 fingerprint=" + new string('b', 32), "");
            Check(!privateSession.Contains("AB2CD3") && !privateSession.Contains(new string('b', 32)), "Join codes and peer fingerprints are redacted from shared logs");

            var normal = Fake(Int32.MaxValue - 1); CrashReports.Finish(normal, 0, true);
            Check(!Directory.Exists(Path.Combine(CrashReports.Root, normal.Id)), "Normal exit does not create a crash report");
            var abnormal = Fake(Int32.MaxValue - 2);
            string game = Path.Combine(root, "fixture-game"); Directory.CreateDirectory(Path.Combine(game, "bin", "x64"));
            File.WriteAllText(Path.Combine(game, Core.GameExe), "fixture"); abnormal.GameRoot = game;
            Directory.CreateDirectory(Path.Combine(game, "red4ext", "logs"));
            File.WriteAllText(Path.Combine(game, "red4ext", "logs", "loader.log"), String.Concat(Enumerable.Repeat("bounded log fixture\n", 16000)) + "nlink://127.0.0.1:47890/" + key);
            string report = CrashReports.Finish(abnormal, unchecked((int)0xC0000005), true);
            var details = new JavaScriptSerializer().Deserialize<GameCrashReport>(File.ReadAllText(Path.Combine(report, "crash.json")));
            Check(details.ExitCode == "0xC0000005" && details.Outcome == "abnormal_exit", "Abnormal exit gets a precise code and honest classification");
            Check(details.Fingerprint.Length == 20 && details.ClientVersion == "0.4.1-test", "Reports contain version and grouping fingerprint");
            string copied = File.ReadAllText(Path.Combine(report, "game-log-1.txt"));
            Check(new FileInfo(Path.Combine(report, "game-log-1.txt")).Length < 132000 && !copied.Contains(key), "Copied logs are bounded and redacted");
            CrashReports.Finish(abnormal, unchecked((int)0xC0000005), true);
            Check(Directory.GetDirectories(CrashReports.Root).Length == 1, "Finalizing the same crash twice does not duplicate it");
            string zip = Path.Combine(root, "export.zip"); CrashReports.ExportLatest(zip);
            using (var archive = ZipFile.OpenRead(zip)) Check(archive.GetEntry("crash.json") != null && archive.GetEntry("crash.txt") != null && archive.Entries.All(x => !x.FullName.Contains("session.xml")), "Export contains reports rather than raw session configuration");

            string abnormalRoot = Path.Combine(root, "watcher-abnormal");
            string abnormalKey = Starter(abnormalRoot, fixture, "crash");
            string standaloneReport = Path.Combine(abnormalRoot, "crashes", abnormalKey, "crash.json");
            Wait(() => File.Exists(standaloneReport), "Watcher records abnormal exit after its launcher process has closed");
            var watched = new JavaScriptSerializer().Deserialize<GameCrashReport>(File.ReadAllText(standaloneReport));
            Check(watched.ExitCode == "0xC0000005", "Independent watcher preserves the simulated crash exit code");

            string cleanRoot = Path.Combine(root, "watcher-clean");
            string cleanKey = Starter(cleanRoot, fixture, "clean");
            string cleanSession = Path.Combine(cleanRoot, "sessions", cleanKey, "session.xml");
            Wait(() => File.Exists(cleanSession) && !String.IsNullOrEmpty(Core.ReadXml<GameSession>(cleanSession).FinishedUtc), "Watcher records a clean session completion");
            Check(!Directory.Exists(Path.Combine(cleanRoot, "crashes", cleanKey)), "Watched clean exit produces no crash report");

            // Native marker recovery is independent of the launcher/watchdog.
            var unclean = Fake(Int32.MaxValue - 3); Directory.CreateDirectory(CrashReports.RuntimeLogs);
            string nativeBase = Path.Combine(CrashReports.RuntimeLogs, "runtime-" + unclean.Pid + "-" + new DateTime(unclean.StartUtcTicks, DateTimeKind.Utc).ToFileTimeUtc().ToString("x16"));
            File.WriteAllText(nativeBase + ".active", unclean.Pid + "\n" + new DateTime(unclean.StartUtcTicks, DateTimeKind.Utc).ToFileTimeUtc() + "\n0.4.1\n");
            File.WriteAllText(nativeBase + ".log", "Native transport started.\n");
            Check(CrashReports.RecoverPending("") == 1 && File.Exists(Path.Combine(CrashReports.Root, unclean.Id, "native-runtime.log")), "A stale native marker recovers a report and breadcrumbs on next startup");
            Check(CrashReports.RecoverPending("") == 0, "Recovery is idempotent");
            var pending = Fake(Int32.MaxValue - 4);
            string pendingFolder = Path.Combine(CrashReports.Sessions, pending.Id); Directory.CreateDirectory(pendingFolder);
            using (var file = File.Create(Path.Combine(pendingFolder, "session.xml"))) new System.Xml.Serialization.XmlSerializer(typeof(GameSession)).Serialize(file, pending);
            using (var owner = new FileStream(Path.Combine(pendingFolder, "watcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check(CrashReports.RecoverPending("") == 0, "Startup recovery does not overwrite a report while its watcher is still finalizing it");
            Check(CrashReports.RecoverPending("") == 1, "An abandoned watcher session becomes recoverable when its lock is released");
            using (var current = Process.GetCurrentProcess())
            {
                var reused = Fake(current.Id); reused.StartUtcTicks = current.StartTime.ToUniversalTime().Ticks + 1; reused.Id = CrashReports.Key(reused.Pid, reused.StartUtcTicks);
                Check(!CrashReports.SameProcess(reused), "PID reuse cannot attach a report to a different process instance");
            }
            File.WriteAllText(Path.Combine(root, "result.txt"), checks + " crash-report integration checks passed.\r\nNo game was launched or crashed.\r\n");
            return 0;
        }
    }
}
