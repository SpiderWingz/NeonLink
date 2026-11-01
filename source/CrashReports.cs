using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace NeonLink
{
    public class GameSession
    {
        public string Id;
        public int Pid;
        public long StartUtcTicks;
        public string ProcessName;
        public string GameRoot;
        public string GameVersion;
        public string ClientVersion;
        public string RuntimeVersion = "not observed";
        public string FinishedUtc;
        public string Outcome;
    }

    public class CrashEvent
    {
        public string ModuleName;
        public string ModuleVersion;
        public string ExceptionCode;
        public string FaultingOffset;
    }

    public class GameCrashReport
    {
        public string Schema = "neonlink-crash-v1";
        public string Id;
        public string CreatedUtc;
        public string SessionStartedUtc;
        public string Outcome;
        public string Explanation;
        public string ExitCode;
        public string Fingerprint;
        public string ClientVersion;
        public string RuntimeVersion;
        public string GameVersion;
        public string WindowsVersion;
        public int GamePid;
        public bool NativeCleanUnload;
        public CrashEvent WindowsCrash;
        public string[] Plugins;
        public string[] CollectionNotes;
    }

    public static class CrashReports
    {
        // The test harness redirects all its artifacts into work/. Players use
        // their private application-data folder; no registry settings change.
        internal static string TestRoot;
        public static string DataRoot { get { return TestRoot ?? Core.DataRoot; } }
        public static string Root { get { return Path.Combine(DataRoot, "crashes"); } }
        public static string Sessions { get { return Path.Combine(DataRoot, "sessions"); } }
        public static string RuntimeLogs { get { return Path.Combine(DataRoot, "runtime-logs"); } }
        static readonly HashSet<string> attached = new HashSet<string>();
        static readonly object attachmentLock = new object();
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        const int TailLimit = 128 * 1024;

        static string Serialize(object value) { lock (json) return json.Serialize(value); }
        public static string Key(int pid, long ticks) { return pid.ToString(CultureInfo.InvariantCulture) + "-" + ticks.ToString(CultureInfo.InvariantCulture); }
        static string SessionFolder(GameSession session)
        {
            if (!Regex.IsMatch(session.Id ?? "", "^[0-9]+-[0-9]+$") || session.Id != Key(session.Pid, session.StartUtcTicks))
                throw new InvalidDataException("Invalid crash-session identity.");
            return Path.Combine(Sessions, session.Id);
        }
        static string RuntimeBase(GameSession session)
        { return Path.Combine(RuntimeLogs, "runtime-" + session.Pid + "-" + new DateTime(session.StartUtcTicks, DateTimeKind.Utc).ToFileTimeUtc().ToString("x16")); }
        static void SaveSession(GameSession session)
        {
            string folder = SessionFolder(session); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "session.xml");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new System.Xml.Serialization.XmlSerializer(typeof(GameSession)).Serialize(stream, session);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static GameSession LoadSession(string file)
        {
            string expectedRoot = Path.GetFullPath(Sessions).TrimEnd('\\') + "\\";
            if (!Path.GetFullPath(file).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Session lies outside the local session folder.");
            var session = Core.ReadXml<GameSession>(file);
            if (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(SessionFolder(session)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Session directory and identity disagree.");
            return session;
        }

        public static string Scrub(string text, string gameRoot)
        {
            text = text ?? "";
            if (!String.IsNullOrWhiteSpace(gameRoot)) text = Regex.Replace(text, Regex.Escape(gameRoot), "%GAME%", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, Regex.Escape(DataRoot), "%NEONLINK%", RegexOptions.IgnoreCase);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrEmpty(profile)) text = Regex.Replace(text, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"nlink://[^\s\""'<>]+", "[invite redacted]", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?i)\b[0-9a-f]{64}\b", "[key/hash redacted]");
            text = Regex.Replace(text, @"(?i)\b[0-9a-f]{32}\b", "[fingerprint/hash redacted]");
            text = Regex.Replace(text, @"\b[A-HJ-NP-Z2-9]{6}\b", match => match.Value.Any(Char.IsLetter) && match.Value.Any(Char.IsDigit) ? "[join code redacted]" : match.Value, RegexOptions.IgnoreCase);
            return text;
        }
        static string ReadTail(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bool partial = stream.Length > TailLimit;
                if (partial) stream.Seek(-TailLimit, SeekOrigin.End);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    // Drop the first partial line after seeking into a UTF-8 log.
                    if (partial) reader.ReadLine();
                    return reader.ReadToEnd();
                }
            }
        }
        static void Record(GameSession session, object activity)
        {
            string file = Path.Combine(SessionFolder(session), "activity.ndjson");
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.WriteAllText(file, ReadTail(file), Encoding.UTF8);
            File.AppendAllText(file, Scrub(Serialize(activity), session.GameRoot) + Environment.NewLine, Encoding.UTF8);
        }

        public static bool SameProcess(GameSession session)
        {
            try
            {
                using (var process = Process.GetProcessById(session.Pid))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == session.StartUtcTicks;
            }
            catch { return false; }
        }
        public static void Attach(Process process, string gameRoot)
        {
            var session = new GameSession {
                Pid = process.Id, StartUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                ProcessName = process.ProcessName, GameRoot = gameRoot,
                GameVersion = Core.VersionOf(Path.Combine(gameRoot, Core.GameExe)),
                ClientVersion = typeof(CrashReports).Assembly.GetName().Version.ToString()
            };
            session.Id = Key(session.Pid, session.StartUtcTicks);
            lock (attachmentLock) { if (!attached.Add(session.Id)) return; }
            string folder = SessionFolder(session); string file = Path.Combine(folder, "session.xml");
            if (!File.Exists(file)) SaveSession(session);
            if (process.HasExited)
            {
                int code = process.ExitCode;
                Task.Run(() => Finish(session, code, true)); return;
            }
            try
            {
                using (var watcher = Process.Start(new ProcessStartInfo(typeof(CrashReports).Assembly.Location,
                    TestRoot == null ? "--watch-process \"" + file + "\"" : "--watch-process-test \"" + file + "\" \"" + TestRoot + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory })) { }
            }
            catch { lock (attachmentLock) attached.Remove(session.Id); throw; }
        }
        public static void AttachExisting(string gameRoot)
        {
            if (!Core.ValidGame(gameRoot)) return;
            foreach (var process in Process.GetProcessesByName("Cyberpunk2077"))
            {
                using (process)
                {
                    try
                    {
                        if (!String.Equals(Path.GetFullPath(process.MainModule.FileName), Path.GetFullPath(Path.Combine(gameRoot, Core.GameExe)), StringComparison.OrdinalIgnoreCase)) continue;
                        Attach(process, gameRoot);
                    }
                    catch (Exception e) { Core.Log("Crash watcher could not attach: " + e.Message); }
                }
            }
        }
        public static int Watch(string file)
        {
            GameSession session = LoadSession(file);
            string folder = SessionFolder(session);
            FileStream ownership;
            try { ownership = new FileStream(Path.Combine(folder, "watcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return 0; } // Another client instance already armed this watcher.
            using (ownership)
            {
                if (!String.IsNullOrEmpty(session.FinishedUtc)) return 0;
                using (var process = FindExactProcess(session))
                {
                    if (process == null) { Finish(session, null, false); return 0; }
                    Record(session, new { utc = DateTime.UtcNow.ToString("o"), kind = "watcher_started", pid = session.Pid });
                    ulong lastSample = 0;
                    while (!process.WaitForExit(1000))
                    {
                        ulong now = (ulong)Environment.TickCount & 0xffffffffUL;
                        if (lastSample != 0 && now - lastSample < 5000) continue;
                        lastSample = now;
                        try
                        {
                            var runtime = RuntimeClient.Command(session.Pid, "STATUS");
                            session.RuntimeVersion = runtime.version ?? "unknown";
                            // Never serialize RuntimeStatus itself: its invite contains a secret.
                            Record(session, new { utc = DateTime.UtcNow.ToString("o"), kind = "runtime", version = runtime.version, phase = runtime.phase, engineRunning = runtime.engineRunning, peers = runtime.peers, sent = runtime.sent, received = runtime.received });
                            SaveSession(session);
                        }
                        catch (IOException) { }
                        catch (TimeoutException) { }
                        catch (Exception e) { Core.Log("Runtime status was unavailable to crash watcher: " + e.GetType().Name); }
                    }
                    Finish(session, process.ExitCode, true);
                }
            }
            return 0;
        }
        static Process FindExactProcess(GameSession session)
        {
            try
            {
                var process = Process.GetProcessById(session.Pid);
                // Keep a query handle open before the process can disappear.
                // A GetProcessById wrapper alone cannot retrieve ExitCode after
                // Windows has released an exited process's final handle.
                IntPtr retainedHandle = process.Handle;
                if (process.StartTime.ToUniversalTime().Ticks == session.StartUtcTicks) return process;
                process.Dispose();
            }
            catch { }
            return null;
        }
        static bool WatcherActive(GameSession session)
        {
            try
            {
                using (var file = new FileStream(Path.Combine(SessionFolder(session), "watcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) return false;
            }
            catch (IOException) { return true; }
        }

        public static int RecoverPending(string gameRoot)
        {
            int recovered = 0;
            if (Directory.Exists(Sessions))
            {
                foreach (string directory in Directory.GetDirectories(Sessions).Take(200))
                {
                    try
                    {
                        string file = Path.Combine(directory, "session.xml");
                        var session = LoadSession(file);
                        if (String.IsNullOrEmpty(session.FinishedUtc) && !SameProcess(session) && !WatcherActive(session))
                        { Finish(session, null, false); recovered++; }
                    }
                    catch (Exception e) { Core.Log("Pending report recovery: " + e.Message); }
                }
            }
            // A native marker also catches games launched while the client was closed.
            if (Directory.Exists(RuntimeLogs))
            {
                foreach (string marker in Directory.GetFiles(RuntimeLogs, "runtime-*.active").Take(200))
                {
                    try
                    {
                        if (new FileInfo(marker).Length > 512) continue;
                        string[] lines = File.ReadAllLines(marker);
                        int pid; long filetime;
                        if (lines.Length < 3 || !Int32.TryParse(lines[0], out pid) || !Int64.TryParse(lines[1], out filetime)) continue;
                        var session = new GameSession { Pid = pid, StartUtcTicks = DateTime.FromFileTimeUtc(filetime).Ticks, ProcessName = "Cyberpunk2077", GameRoot = gameRoot, GameVersion = Core.ValidGame(gameRoot) ? Core.VersionOf(Path.Combine(gameRoot, Core.GameExe)) : "unknown", ClientVersion = typeof(CrashReports).Assembly.GetName().Version.ToString(), RuntimeVersion = lines[2] };
                        session.Id = Key(pid, session.StartUtcTicks);
                        if (SameProcess(session) || !String.Equals(Path.GetFullPath(marker), Path.GetFullPath(RuntimeBase(session) + ".active"), StringComparison.OrdinalIgnoreCase)) continue;
                        string existing = Path.Combine(SessionFolder(session), "session.xml");
                        if (File.Exists(existing)) session = LoadSession(existing); else SaveSession(session);
                        if (String.IsNullOrEmpty(session.FinishedUtc) && !WatcherActive(session)) { Finish(session, null, false); recovered++; }
                    }
                    catch (Exception e) { Core.Log("Native report recovery: " + e.Message); }
                }
            }
            return recovered;
        }

        static CrashEvent FindWindowsEvent(GameSession session, List<string> notes)
        {
            try
            {
                var query = new EventLogQuery("Application", PathType.LogName, "*[System[(EventID=1000) and TimeCreated[timediff(@SystemTime) <= 86400000]]]") { ReverseDirection = true };
                using (var reader = new EventLogReader(query))
                {
                    for (int i = 0; i < 80; i++)
                    {
                        using (var record = reader.ReadEvent())
                        {
                            if (record == null) break;
                            var document = new XmlDocument { XmlResolver = null }; document.LoadXml(record.ToXml());
                            var ns = new XmlNamespaceManager(document.NameTable); ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                            var fields = new Dictionary<string, string>();
                            foreach (XmlNode node in document.SelectNodes("//e:EventData/e:Data[@Name]", ns)) fields[node.Attributes["Name"].Value] = node.InnerText;
                            string pid, appName;
                            if (!fields.TryGetValue("ProcessId", out pid) || !fields.TryGetValue("AppName", out appName)) continue;
                            long parsedPid;
                            bool validPid = pid.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Int64.TryParse(pid.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedPid) : Int64.TryParse(pid, out parsedPid);
                            if (!validPid || parsedPid != session.Pid || !String.Equals(Path.GetFileNameWithoutExtension(appName), session.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                            string started;
                            if (fields.TryGetValue("ProcessCreationTime", out started))
                            {
                                long ft;
                                if (!Int64.TryParse(started.StartsWith("0x") ? started.Substring(2) : started, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ft) || DateTime.FromFileTimeUtc(ft).Ticks != session.StartUtcTicks) continue;
                            }
                            else if (!record.TimeCreated.HasValue || record.TimeCreated.Value.ToUniversalTime().Ticks < session.StartUtcTicks) continue;
                            Func<string, string> field = name => fields.ContainsKey(name) ? fields[name] : "unknown";
                            return new CrashEvent { ModuleName = field("ModuleName"), ModuleVersion = field("ModuleVersion"), ExceptionCode = field("ExceptionCode"), FaultingOffset = field("FaultingOffset") };
                        }
                    }
                }
            }
            catch (Exception e) { notes.Add("Windows crash event was unavailable: " + e.GetType().Name); }
            return null;
        }
        static void CopyLog(string source, string destination, string gameRoot, List<string> notes)
        {
            try
            {
                if (!File.Exists(source)) return;
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) { notes.Add("Skipped a redirected log file."); return; }
                File.WriteAllText(destination, Scrub(ReadTail(source), gameRoot), Encoding.UTF8);
            }
            catch (Exception e) { notes.Add("A log could not be collected: " + e.GetType().Name); }
        }
        static void CollectGameLogs(GameSession session, string destination, List<string> notes)
        {
            if (!Core.ValidGame(session.GameRoot)) return;
            int index = 0;
            foreach (string relative in new[] { @"red4ext\logs", @"r6\logs" })
            {
                try
                {
                    string directory = Path.Combine(session.GameRoot, relative);
                    if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var file in new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(x => x.LastWriteTimeUtc).Take(3))
                        CopyLog(file.FullName, Path.Combine(destination, "game-log-" + (++index) + ".txt"), session.GameRoot, notes);
                }
                catch (Exception e) { notes.Add("Game log collection: " + e.GetType().Name); }
            }
        }
        static string[] Plugins(GameSession session)
        {
            try
            {
                if (!Core.ValidGame(session.GameRoot)) return new string[0];
                string root = Path.Combine(session.GameRoot, @"red4ext\plugins");
                if (!Directory.Exists(root)) return new string[0];
                // One level of plugin subdirectories; never walk arbitrary links or saves.
                var files = new List<string>(Directory.GetFiles(root, "*.dll"));
                foreach (string subdir in Directory.GetDirectories(root).Take(64))
                    if ((File.GetAttributes(subdir) & FileAttributes.ReparsePoint) == 0) files.AddRange(Directory.GetFiles(subdir, "*.dll"));
                return files.Take(64).Select(f => Path.GetFileName(f) + " / " + Core.VersionOf(f)).ToArray();
            }
            catch { return new string[0]; }
        }
        public static string Finish(GameSession session, int? exitCode, bool observed)
        {
            string folder = SessionFolder(session); Directory.CreateDirectory(folder);
            using (var gate = new Mutex(false, "Local\\NeonLink.CrashReport." + session.Id))
            {
                bool acquired;
                try { acquired = gate.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Crash report is already being finalized.");
                try
                {
                    string existing = Path.Combine(folder, "session.xml");
                    if (File.Exists(existing))
                    {
                        var saved = LoadSession(existing);
                        if (!String.IsNullOrEmpty(saved.FinishedUtc)) return Path.Combine(Root, session.Id);
                    }
                    bool uncleanNative = File.Exists(RuntimeBase(session) + ".active") && !File.Exists(RuntimeBase(session) + ".closed");
                    if (uncleanNative && (String.IsNullOrEmpty(session.RuntimeVersion) || session.RuntimeVersion == "not observed"))
                    {
                        try
                        {
                            string marker = RuntimeBase(session) + ".active";
                            if (new FileInfo(marker).Length <= 512)
                            {
                                string[] fields = File.ReadAllLines(marker);
                                if (fields.Length >= 3 && Regex.IsMatch(fields[2], "^[0-9]+\\.[0-9]+\\.[0-9]+$")) session.RuntimeVersion = fields[2];
                            }
                        }
                        catch (IOException) { }
                    }
                    if (observed && exitCode == 0 && !uncleanNative)
                    {
                        session.Outcome = "clean_exit"; session.FinishedUtc = DateTime.UtcNow.ToString("o"); SaveSession(session); return null;
                    }
                    if (!observed && File.Exists(RuntimeBase(session) + ".closed"))
                    {
                        session.Outcome = "native_unloaded_exit_unobserved"; session.FinishedUtc = DateTime.UtcNow.ToString("o"); SaveSession(session); return null;
                    }
                    string reportDirectory = Path.Combine(Root, session.Id); Directory.CreateDirectory(reportDirectory);
                    var notes = new List<string>();
                    // Windows may publish Application Error slightly after process exit.
                    if (observed && TestRoot == null) Thread.Sleep(2000);
                    CrashEvent crashEvent = TestRoot == null ? FindWindowsEvent(session, notes) : null;
                    string code = exitCode.HasValue ? "0x" + unchecked((uint)exitCode.Value).ToString("X8") : "unavailable";
                    var report = new GameCrashReport {
                        Id = session.Id, CreatedUtc = DateTime.UtcNow.ToString("o"), SessionStartedUtc = new DateTime(session.StartUtcTicks, DateTimeKind.Utc).ToString("o"),
                        Outcome = crashEvent != null ? "windows_crash_event" : observed ? (exitCode == 0 ? "unclean_native_shutdown" : "abnormal_exit") : "unclean_session_recovered",
                        Explanation = crashEvent != null ? "Windows recorded an application crash for this exact game process." : observed ? (exitCode == 0 ? "The process exited with code zero, but the native runtime did not record a clean unload. The cause is unknown." : "The game returned a nonzero exit code. This may be a crash, failed startup, or forced close; the exit code alone does not establish the cause.") : "A session ended before a clean completion was recorded. The exit code and cause could not be observed.",
                        ExitCode = code, ClientVersion = session.ClientVersion, RuntimeVersion = session.RuntimeVersion, GameVersion = session.GameVersion,
                        WindowsVersion = Environment.OSVersion.VersionString, GamePid = session.Pid, NativeCleanUnload = File.Exists(RuntimeBase(session) + ".closed"), WindowsCrash = crashEvent, Plugins = Plugins(session)
                    };
                    string fingerprintInput = code + "|" + session.GameVersion + "|" + (crashEvent == null ? "unknown" : crashEvent.ModuleName + "|" + crashEvent.ModuleVersion + "|" + crashEvent.ExceptionCode + "|" + crashEvent.FaultingOffset);
                    using (var sha = SHA256.Create()) report.Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintInput))).Replace("-", "").Substring(0, 20);
                    CopyLog(Path.Combine(folder, "activity.ndjson"), Path.Combine(reportDirectory, "runtime-activity.ndjson"), session.GameRoot, notes);
                    CopyLog(RuntimeBase(session) + ".log", Path.Combine(reportDirectory, "native-runtime.log"), session.GameRoot, notes);
                    CopyLog(Path.Combine(TestRoot == null ? Core.LogRoot : Path.Combine(DataRoot, "logs"), "launcher-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), Path.Combine(reportDirectory, "launcher.log"), session.GameRoot, notes);
                    CollectGameLogs(session, reportDirectory, notes);
                    report.CollectionNotes = notes.ToArray();
                    string pending = Path.Combine(reportDirectory, "crash.json.pending");
                    File.WriteAllText(pending, Serialize(report), Encoding.UTF8);
                    string final = Path.Combine(reportDirectory, "crash.json"); if (File.Exists(final)) File.Delete(final); File.Move(pending, final);
                    File.WriteAllText(Path.Combine(reportDirectory, "crash.txt"),
                        "REDLINE GAME CRASH REPORT\r\nUTC: " + report.CreatedUtc + "\r\nOutcome: " + report.Outcome + "\r\nExit code: " + code + "\r\nFingerprint: " + report.Fingerprint +
                        "\r\nClient: " + report.ClientVersion + "\r\nRuntime: " + report.RuntimeVersion + "\r\nGame: " + report.GameVersion + "\r\n\r\n" + report.Explanation +
                        (crashEvent == null ? "" : "\r\nModule: " + crashEvent.ModuleName + "\r\nException: " + crashEvent.ExceptionCode + "\r\nOffset: " + crashEvent.FaultingOffset) +
                        "\r\n\r\nReports are local. Nothing was uploaded. Memory dumps and save files are not included.\r\n", Encoding.UTF8);
                    session.Outcome = report.Outcome; session.FinishedUtc = report.CreatedUtc; SaveSession(session);
                    return reportDirectory;
                }
                finally { gate.ReleaseMutex(); }
            }
        }
        public static string Latest()
        {
            if (!Directory.Exists(Root)) return null;
            return new DirectoryInfo(Root).GetDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "crash.json"))).OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "crash.json"))).Select(d => d.FullName).FirstOrDefault();
        }
        public static void ExportLatest(string destination)
        {
            string latest = Latest(); if (latest == null) throw new IOException("No crash reports have been recorded yet.");
            if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(latest).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose an export destination outside the crash report's own folder.");
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ZipFile.CreateFromDirectory(latest, temp, CompressionLevel.Optimal, false);
                if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
