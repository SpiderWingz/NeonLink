using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("Redline Launcher")]
[assembly: AssemblyDescription("Redline desktop launcher for Cyberpunk 2077")]
[assembly: AssemblyCompany("Redline fan project")]
[assembly: AssemblyProduct("Redline Client")]
[assembly: AssemblyVersion("0.7.2.0")]
[assembly: AssemblyFileVersion("0.7.2.0")]

namespace NeonLink
{
    public class Preferences
    {
        public string GameRoot = "";
        public bool AutoDetect = true;
        public bool MinimizeOnLaunch = true;
        public bool RotateBackdrops = true;
        public bool AnimateBackdrops = true;
        public string LastBackdrop = "";
        public string HostBindAddress = "0.0.0.0";
        public string PublicHost = "";
        public int LocalHostPort = 47890;
        public int PublicHostPort = 47890;
        public string RouterTarget = "";
    }

    public class Backdrop
    {
        public string Name;
        public string File;
        public string Poster;
        public bool Video { get { return File.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase); } }
        public Backdrop(string name, string file, string poster) { Name = name; File = file; Poster = poster; }
    }

    public static class Backdrops
    {
        public static readonly TimeSpan RotationInterval = TimeSpan.FromMinutes(5);
        public static string Root { get { return Path.Combine(BundledContent.Root, "assets", "backdrops"); } }
        public static readonly Backdrop[] Catalog = {
            new Backdrop("Johnny Silverhand", "johnny.jpg", "johnny.jpg"),
            new Backdrop("The Black Sapphire", "black-sapphire.jpg", "black-sapphire.jpg"),
            new Backdrop("Phantom Liberty convoy", "convoy.jpg", "convoy.jpg"),
            new Backdrop("V / Cyberpunk 2077", "original-v.jpg", "original-v.jpg"),
            new Backdrop("Fifth anniversary", "anniversary.png", "anniversary.png"),
            new Backdrop("Outnumbered, but not outgunned", "outnumbered.jpg", "outnumbered.jpg"),
            new Backdrop("Phantom Liberty", "phantom-liberty.mp4", "phantom-liberty-poster.jpg"),
            new Backdrop("Ultimate Edition", "ultimate-edition.mp4", "ultimate-edition-poster.jpg")
        };
        public static Backdrop Choose(IEnumerable<Backdrop> available, string previous, bool videoAllowed, Random random)
        {
            var pool = available.Where(x => videoAllowed || !x.Video).ToList();
            if (pool.Count > 1) pool = pool.Where(x => x.File != previous).ToList();
            return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
        }
    }

    public class Candidate
    {
        public string Root { get; set; }
        public string Store { get; set; }
        public string Display { get { return Store + "  |  " + Root; } }
    }

    public class Check
    {
        public string Name;
        public string State;
        public string Detail;
        public Check(string name, string state, string detail) { Name = name; State = state; Detail = detail; }
    }

    [XmlRoot("NeonLinkPayload")]
    public class PayloadManifest
    {
        public string Version;
        public string ClientVersion;
        public string Red4extVersion;
        public string GameVersion;
        // A preview build must never claim that the unfinished game adapter is playable.
        public bool MultiplayerReady;
        [XmlArray("Files"), XmlArrayItem("File")]
        public List<PayloadFile> Files = new List<PayloadFile>();
    }
    public class PayloadFile
    {
        [XmlAttribute] public string Path;
        [XmlAttribute] public string Sha256;
    }

    public class SetupState
    {
        public bool NeedsGame;
        public bool Ready;
        public bool CanInstall;
        public string Title;
        public string Detail;
    }

    public static class Core
    {
        public const string GameExe = @"bin\x64\Cyberpunk2077.exe";
        public const string PluginDll = @"red4ext\plugins\NeonLink\NeonLink.dll";
        public static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonLink");
        public static readonly string SettingsFile = Path.Combine(DataRoot, "client-settings.xml");
        public static readonly string LogRoot = Path.Combine(DataRoot, "logs");
        public static readonly string BackupRoot = Path.Combine(DataRoot, "backups");
        public static string PayloadRoot { get { return Path.Combine(BundledContent.Root, "payload"); } }
        public static readonly string[] AllowedFiles = {
            "bin/x64/winmm.dll", "red4ext/RED4ext.dll", "red4ext/LICENSE.txt",
            "red4ext/THIRD_PARTY_LICENSES.txt", "red4ext/plugins/NeonLink/NeonLink.dll",
            "red4ext/plugins/NeonLink/NeonLink.toml"
        };

        public static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(LogRoot);
                File.AppendAllText(Path.Combine(LogRoot, "launcher-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
            }
            catch { /* Logging failures must not stop the launcher. */ }
        }

        public static Preferences LoadPreferences()
        {
            try
            {
                if (File.Exists(SettingsFile)) return ReadXml<Preferences>(SettingsFile);
            }
            catch (Exception e) { Log("Settings could not be read: " + e.Message); }
            return new Preferences();
        }

        public static void SavePreferences(Preferences settings)
        {
            Directory.CreateDirectory(DataRoot);
            string temp = SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(stream, settings);
                if (File.Exists(SettingsFile)) File.Replace(temp, SettingsFile, null);
                else File.Move(temp, SettingsFile);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static T ReadXml<T>(string file)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = XmlReader.Create(stream, settings)) return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }

        public static string NormalizeRoot(string input)
        {
            if (String.IsNullOrWhiteSpace(input)) return "";
            string path = Path.GetFullPath(input.Trim().Trim('"'));
            if (String.Equals(Path.GetFileName(path), "Cyberpunk2077.exe", StringComparison.OrdinalIgnoreCase))
            {
                string directory = Path.GetDirectoryName(path);
                if (String.Equals(Path.GetFileName(directory), "x64", StringComparison.OrdinalIgnoreCase))
                    path = Path.GetFullPath(Path.Combine(directory, "..", ".."));
            }
            return path.TrimEnd(Path.DirectorySeparatorChar);
        }

        public static bool ValidGame(string root) { return !String.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, GameExe)); }
        public static bool GameRunning() { return Process.GetProcessesByName("Cyberpunk2077").Any(); }
        public static string VersionOf(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown"; }
            catch { return "unknown"; }
        }

        public static IEnumerable<string> ReadVdfValues(string contents, string field)
        {
            return Regex.Matches(contents, "\"" + Regex.Escape(field) + "\"\\s+\"([^\"]+)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        static void AddCandidate(List<Candidate> list, string path, string store)
        {
            try
            {
                var root = NormalizeRoot(path);
                if (ValidGame(root) && !list.Any(x => String.Equals(x.Root, root, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new Candidate { Root = root, Store = store });
            }
            catch { }
        }

        static string RegistryValue(RegistryKey hive, string keyName, params string[] valueNames)
        {
            try
            {
                using (var key = hive.OpenSubKey(keyName))
                {
                    if (key == null) return null;
                    foreach (string name in valueNames) { var value = key.GetValue(name) as string; if (!String.IsNullOrWhiteSpace(value)) return value; }
                }
            }
            catch { }
            return null;
        }

        public static List<Candidate> Discover()
        {
            var results = new List<Candidate>();
            var steamRoots = new List<string>();
            steamRoots.Add(RegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", "InstallPath"));
            steamRoots.Add(RegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "SteamPath"));
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (string steam in steamRoots.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var libraries = new List<string> { steam };
                try
                {
                    string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf)) libraries.AddRange(ReadVdfValues(File.ReadAllText(vdf), "path"));
                }
                catch { }
                foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    AddCandidate(results, Path.Combine(library, @"steamapps\common\Cyberpunk 2077"), "Steam");
                    try
                    {
                        string manifest = Path.Combine(library, @"steamapps\appmanifest_1091500.acf");
                        if (File.Exists(manifest))
                        {
                            foreach (string dir in ReadVdfValues(File.ReadAllText(manifest), "installdir"))
                            {
                                // Install directory must be a single name inside this Steam library.
                                if (dir.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && dir != "..")
                                    AddCandidate(results, Path.Combine(library, "steamapps", "common", dir), "Steam");
                            }
                        }
                    }
                    catch { }
                }
            }
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (string baseKey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games" })
                {
                    try
                    {
                        using (var root = hive.OpenSubKey(baseKey))
                        {
                            if (root == null) continue;
                            foreach (string name in root.GetSubKeyNames())
                            using (var item = root.OpenSubKey(name))
                            {
                                if (item == null) continue;
                                string title = Convert.ToString(item.GetValue("DisplayName") ?? item.GetValue("gameName"));
                                if (title.IndexOf("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string location = Convert.ToString(item.GetValue("InstallLocation") ?? item.GetValue("path"));
                                if (!String.IsNullOrWhiteSpace(location)) AddCandidate(results, location, "GOG / installed game");
                            }
                        }
                    }
                    catch { }
                }
            }
            string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(epic))
            {
                try
                {
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                    foreach (string file in Directory.GetFiles(epic, "*.item"))
                    {
                        try
                        {
                            var fields = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                            object title, location;
                            if (fields.TryGetValue("DisplayName", out title) && Convert.ToString(title).IndexOf("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase) >= 0 && fields.TryGetValue("InstallLocation", out location))
                                AddCandidate(results, Convert.ToString(location), "Epic Games");
                        }
                        catch { }
                    }
                }
                catch { }
            }
            Log("Discovery completed: " + results.Count + " valid installation(s).");
            return results.OrderBy(x => x.Store).ThenBy(x => x.Root).ToList();
        }

        public static List<Check> CheckInstallation(string root)
        {
            var checks = new List<Check>();
            if (!ValidGame(root))
            {
                checks.Add(new Check("Cyberpunk 2077", "SETUP", "Select your game folder to continue."));
                checks.Add(new Check("RED4ext loader", "WAITING", "Checked after selecting a game."));
                checks.Add(new Check("Redline runtime", "DEVELOPMENT", "Playable co-op runtime has not been released."));
                return checks;
            }
            string version = VersionOf(Path.Combine(root, GameExe));
            checks.Add(new Check("Cyberpunk 2077", "FOUND", "Game executable found. Version: " + version));
            bool compatible = Regex.IsMatch(version, @"^2\.5(?:\.|$|\s)");
            checks.Add(new Check("Target game version", compatible ? "OK" : "REVIEW", "Redline currently targets patch 2.5."));
            bool loader = File.Exists(Path.Combine(root, @"red4ext\RED4ext.dll")) && File.Exists(Path.Combine(root, @"bin\x64\winmm.dll"));
            checks.Add(new Check("RED4ext loader", loader ? "FOUND" : "MISSING", loader ? "Loader and winmm.dll are present." : "RED4ext.dll and the winmm.dll loader are required."));
            bool plugin = File.Exists(Path.Combine(root, PluginDll));
            checks.Add(new Check("Redline runtime", plugin ? "PRESENT" : "MISSING", plugin ? "NeonLink.dll is present. This preview does not certify a playable build." : "No NeonLink.dll is installed in this game."));
            return checks;
        }

        public static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static SetupState GetSetupState(string gameRoot)
        {
            if (!ValidGame(gameRoot)) return new SetupState { NeedsGame = true, Title = "Find your copy of Cyberpunk 2077", Detail = "Choose Find my game, or browse to Cyberpunk2077.exe. RED4ext and Redline are already included in this launcher." };
            if (!Regex.IsMatch(VersionOf(Path.Combine(gameRoot, GameExe)), @"^2\.5(?:\.|$|\s)"))
                return new SetupState { Title = "Cyberpunk patch 2.5 is required", Detail = "Selected game version: " + VersionOf(Path.Combine(gameRoot, GameExe)) + ". Select a compatible installation to set up this development build." };
            try
            {
                var manifest = ValidatePayload(PayloadRoot);
                int changes = 0;
                foreach (var entry in manifest.Files)
                {
                    string target = ContainedPath(gameRoot, entry.Path);
                    if (entry.Path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                    if (!File.Exists(target) || !String.Equals(Hash(target), entry.Sha256, StringComparison.OrdinalIgnoreCase)) changes++;
                }
                if (changes == 0) return new SetupState { Ready = true, CanInstall = !GameRunning(), Title = "Your game is set up", Detail = "RED4ext 1.31.0 and Redline's native runtime are installed and verified. You can launch the game below. Gameplay co-op is still in development." };
                bool running = GameRunning();
                return new SetupState { CanInstall = !running, Title = running ? "Close Cyberpunk to finish setup" : "Ready to set up your game", Detail = running ? "Close the game, then choose Verify files to enable installation or repair." : "The launcher will install or repair " + changes + " bundled files. Replaced files are backed up automatically; existing configuration is preserved." };
            }
            catch (Exception e) { return new SetupState { Title = "Setup needs attention", Detail = e.Message }; }
        }

        static void RejectReparse(string file, string root)
        {
            string cursor = file;
            while (!String.IsNullOrEmpty(cursor) && cursor.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("An installation path redirects to another location: " + cursor);
                if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                cursor = Path.GetDirectoryName(cursor);
            }
        }

        public static string ContainedPath(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || !AllowedFiles.Contains(relative, StringComparer.Ordinal))
                throw new InvalidDataException("Unexpected runtime file: " + relative);
            string absoluteRoot = Path.GetFullPath(root).TrimEnd('\\');
            string destination = Path.GetFullPath(Path.Combine(absoluteRoot, relative.Replace('/', '\\')));
            if (!destination.StartsWith(absoluteRoot + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Runtime path escapes the installation folder.");
            RejectReparse(destination, absoluteRoot);
            return destination;
        }

        public static PayloadManifest ValidatePayload(string payloadRoot)
        {
            string file = Path.Combine(payloadRoot, "manifest.xml");
            if (!File.Exists(file)) throw new InvalidDataException("This launcher preview has no bundled co-op runtime.");
            var manifest = ReadXml<PayloadManifest>(file);
            if (manifest == null || String.IsNullOrWhiteSpace(manifest.Version) || manifest.GameVersion != "2.5" || manifest.Files == null)
                throw new InvalidDataException("The runtime manifest is incomplete or targets another game version.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                if (!names.Add(entry.Path)) throw new InvalidDataException("Duplicate runtime entry.");
                string path = ContainedPath(payloadRoot, entry.Path);
                if (!Regex.IsMatch(entry.Sha256 ?? "", "^[0-9a-fA-F]{64}$") || !File.Exists(path) || !String.Equals(Hash(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Runtime file is missing or damaged: " + entry.Path);
            }
            foreach (string required in AllowedFiles.Take(5))
                if (!names.Contains(required)) throw new InvalidDataException("Runtime is missing " + required);
            return manifest;
        }

        public static string InstallPayload(string gameRoot)
        {
            if (!ValidGame(gameRoot)) throw new IOException("Select a valid game folder first.");
            if (GameRunning()) throw new IOException("Close Cyberpunk 2077 before installing the runtime.");
            if (!Regex.IsMatch(VersionOf(Path.Combine(gameRoot, GameExe)), @"^2\.5(?:\.|$|\s)"))
                throw new IOException("This runtime requires Cyberpunk patch 2.5.");
            var manifest = ValidatePayload(PayloadRoot);
            var changes = new List<Tuple<string, string>>();
            foreach (var entry in manifest.Files)
            {
                string target = ContainedPath(gameRoot, entry.Path);
                if (entry.Path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                if (!File.Exists(target) || !String.Equals(Hash(target), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    changes.Add(Tuple.Create(ContainedPath(PayloadRoot, entry.Path), target));
            }
            if (changes.Count == 0) return "Bundled runtime is already installed.";
            string backup = Path.Combine(BackupRoot, "runtime-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var completed = new List<Tuple<string, string>>();
            try
            {
                // Prepare backups before changing any game file.
                foreach (var change in changes)
                    if (File.Exists(change.Item2))
                    {
                        string relative = change.Item2.Substring(Path.GetFullPath(gameRoot).TrimEnd('\\').Length + 1);
                        string saved = Path.Combine(backup, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        File.Copy(change.Item2, saved, false);
                    }
                foreach (var change in changes)
                {
                    completed.Add(change);
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Item2));
                    File.Copy(change.Item1, change.Item2, true);
                    if (Hash(change.Item1) != Hash(change.Item2)) throw new IOException("Installed file verification failed.");
                }
                Log("Runtime installed from local bundle: " + changes.Count + " files.");
                return "Bundled runtime installed. Previous files, if any, are in the runtime backup folder.";
            }
            catch
            {
                foreach (var change in completed.AsEnumerable().Reverse())
                {
                    string relative = change.Item2.Substring(Path.GetFullPath(gameRoot).TrimEnd('\\').Length + 1);
                    string saved = Path.Combine(backup, relative);
                    try { if (File.Exists(saved)) File.Copy(saved, change.Item2, true); else if (File.Exists(change.Item2)) File.Delete(change.Item2); }
                    catch (Exception e) { Log("Rollback requires attention: " + e.Message); }
                }
                throw;
            }
        }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folder, uint flags, IntPtr token, out IntPtr path);
        public static string SavesPath()
        {
            IntPtr path;
            int result = SHGetKnownFolderPath(new Guid("4C5C32FF-BB9D-43B0-BF7D-CA9FB8C8B8BC"), 0, IntPtr.Zero, out path);
            if (result != 0) throw new IOException("Windows could not locate the Saved Games folder.");
            try { return Path.Combine(Marshal.PtrToStringUni(path), @"CD Projekt Red\Cyberpunk 2077"); }
            finally { Marshal.FreeCoTaskMem(path); }
        }

        public static string BackupSaves()
        {
            if (GameRunning()) throw new IOException("Close Cyberpunk 2077 before copying its saves.");
            string source = SavesPath();
            if (!Directory.Exists(source)) throw new IOException("No Cyberpunk 2077 saves were found on this PC.");
            string destination = Path.Combine(BackupRoot, "saves-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            CopyDirectory(source, destination);
            Log("Save backup completed.");
            return destination;
        }
        static void CopyDirectory(string source, string destination)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("A save directory redirects to another location.");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A save file redirects to another location.");
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
            }
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public sealed class Client
    {
        readonly Window window;
        readonly Preferences preferences;
        readonly Dictionary<string, FrameworkElement> pages = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        bool changingSettings;
        bool busy;
        readonly Random backdropRandom = new Random(Guid.NewGuid().GetHashCode());
        readonly DispatcherTimer backdropTimer = new DispatcherTimer();
        readonly HashSet<string> failedVideos = new HashSet<string>();
        Backdrop currentBackdrop;
        bool videoIsReady;
        readonly DispatcherTimer runtimeTimer = new DispatcherTimer();
        RuntimeStatus runtimeStatus;
        bool pollingRuntime;
        bool networkBusy;

        public Client()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeonLink.Shell.xaml")) window = (Window)XamlReader.Load(stream);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Redline.Icon"))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                window.Icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                window.Icon.Freeze();
            }
            LoadImage("BrandImage", "Redline.Logo.png");
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            preferences = Core.LoadPreferences();
            LoadImage("HeroImage", "NeonLink.night-city.jpg");
            LoadImage("LocationImage", "NeonLink.rooftop.jpg");
            LoadImage("RuntimeImage", "NeonLink.street.jpg");
            var video = Find<MediaElement>("HeroVideo");
            video.MediaOpened += delegate
            {
                videoIsReady = true;
                if (currentBackdrop != null) Core.Log("Video backdrop opened: " + currentBackdrop.File + " (" + video.NaturalVideoWidth + "x" + video.NaturalVideoHeight + ")");
                UpdateMediaPlayback();
            };
            video.MediaEnded += delegate { video.Position = TimeSpan.Zero; UpdateMediaPlayback(); };
            video.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e)
            {
                if (currentBackdrop != null) failedVideos.Add(currentBackdrop.File);
                videoIsReady = false; video.Visibility = Visibility.Hidden;
                Core.Log("Video backdrop could not be decoded; keeping its poster: " + e.ErrorException.Message);
                if (currentBackdrop != null) Find<TextBlock>("BackdropLabel").Text = currentBackdrop.Name + " / still image";
            };
            backdropTimer.Interval = Backdrops.RotationInterval;
            backdropTimer.Tick += delegate { ChangeBackdrop(); };
            window.StateChanged += delegate { UpdateMediaPlayback(); };
            window.Closed += delegate { backdropTimer.Stop(); video.Close(); };
            On("NextBackdrop", delegate { ChangeBackdrop(); });
            On("NextBackdropSetting", delegate { ChangeBackdrop(); });
            foreach (string page in new[] { "Play", "Servers", "Setup", "Settings", "Diagnostics", "About" })
            {
                pages[page] = Find<FrameworkElement>("Page" + page);
                navigation[page] = Find<Button>("Nav" + page);
                string target = page;
                navigation[page].Click += delegate { Navigate(target); };
            }
            pages["Welcome"] = Find<FrameworkElement>("PageWelcome");
            Find<Grid>("TitleBar").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource is TextBlock || e.OriginalSource == sender)
                { if (e.ClickCount == 2) ToggleMaximize(); else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); }
            };
            On("CloseButton", delegate { window.Close(); });
            On("MinimizeButton", delegate { window.WindowState = WindowState.Minimized; });
            On("MaximizeButton", delegate { ToggleMaximize(); });
            foreach (string link in new[] { "PlaySetupLink", "LocationCardButton", "RuntimeCardButton", "ServerSetupButton" }) On(link, delegate { Navigate("Setup"); });
            On("BrowseGame", delegate { Browse(); });
            On("DetectGame", async delegate { await Discover(true); });
            On("VerifyGame", delegate { Refresh(); Status("Installation checks refreshed."); });
            On("RunDiagnostics", delegate { Refresh(); Status("Diagnostics complete. No data was uploaded."); });
            On("ExportDiagnostics", delegate { ExportDiagnostics(); });
            On("OpenLogs", delegate { OpenFolder(Core.LogRoot); });
            On("OpenCrashReports", delegate { OpenFolder(CrashReports.Root); });
            On("ExportCrashReport", delegate { ExportCrashReport(); });
            On("OpenBackups", delegate { OpenFolder(Core.BackupRoot); });
            On("ArtworkSource", delegate { OpenUrl("https://www.cyberpunk.net/us/en/cyberpunk-2077"); });
            On("LaunchGame", delegate { Launch(); });
            On("InstallRuntime", async delegate { await Install(); });
            On("WelcomeBrowse", delegate { Browse(); });
            On("WelcomeDashboard", delegate { Navigate("Play"); });
            On("WelcomeAction", async delegate
            {
                if (busy) return;
                var state = Core.GetSetupState(preferences.GameRoot);
                if (state.NeedsGame) await Discover(true);
                else if (state.Ready) Launch();
                else if (state.CanInstall) await Install();
                else { Refresh(); Status(state.Detail); }
            });
            On("OpenLicenses", delegate { OpenFolder(Path.Combine(BundledContent.Root, "licenses")); });
            On("BackupSaves", async delegate { await Backup(); });
            On("RefreshRuntime", async delegate { await PollRuntime(); });
            On("HostConnection", async delegate { await HostConnection(); });
            On("JoinConnection", async delegate { await JoinConnection(); });
            On("StopConnection", async delegate { await SendRuntime("STOP"); });
            On("CreatePortMapping", async delegate { await MapPort(false); });
            On("RemovePortMapping", async delegate { await MapPort(true); });
            Find<TextBox>("HostAddress").Text = preferences.HostBindAddress;
            Find<TextBox>("HostPort").Text = preferences.LocalHostPort.ToString();
            Find<TextBox>("PublicHost").Text = preferences.PublicHost;
            Find<TextBox>("PublicPort").Text = preferences.PublicHostPort.ToString();
            Find<CheckBox>("CustomLocalPort").IsChecked = preferences.LocalHostPort != preferences.PublicHostPort;
            Find<Expander>("AdvancedHostSettings").IsExpanded = preferences.LocalHostPort != preferences.PublicHostPort || preferences.HostBindAddress != "0.0.0.0";
            Find<TextBox>("PublicPort").TextChanged += delegate { SyncLocalPort(); };
            Find<CheckBox>("CustomLocalPort").Checked += delegate { SyncLocalPort(); };
            Find<CheckBox>("CustomLocalPort").Unchecked += delegate { SyncLocalPort(); };
            SyncLocalPort();
            Find<TextBox>("RouterTarget").Text = String.IsNullOrEmpty(preferences.RouterTarget) ? InternetHosting.LocalAddress() : preferences.RouterTarget;
            UpdateMappingStatus();
            On("CopyInvite", delegate
            {
                if (runtimeStatus != null && !String.IsNullOrEmpty(runtimeStatus.invite))
                { Clipboard.SetText(runtimeStatus.invite); Status("Transport-test invite copied."); }
            });
            runtimeTimer.Interval = TimeSpan.FromSeconds(2);
            runtimeTimer.Tick += async delegate { await PollRuntime(); };
            window.Closed += delegate { runtimeTimer.Stop(); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (networkBusy) { e.Cancel = true; Status("Wait for the current connection or router setup request to finish before closing."); } };
            changingSettings = true;
            Find<CheckBox>("AutoDetectSetting").IsChecked = preferences.AutoDetect;
            Find<CheckBox>("MinimizeSetting").IsChecked = preferences.MinimizeOnLaunch;
            Find<CheckBox>("RotateBackdropSetting").IsChecked = preferences.RotateBackdrops;
            Find<CheckBox>("AnimatedBackdropSetting").IsChecked = preferences.AnimateBackdrops;
            changingSettings = false;
            foreach (string name in new[] { "AutoDetectSetting", "MinimizeSetting", "RotateBackdropSetting", "AnimatedBackdropSetting" })
            {
                Find<CheckBox>(name).Checked += delegate { SaveOptions(); };
                Find<CheckBox>(name).Unchecked += delegate { SaveOptions(); };
            }
            Find<ComboBox>("DetectedGames").SelectionChanged += delegate
            {
                var candidate = Find<ComboBox>("DetectedGames").SelectedItem as Candidate;
                if (candidate != null) SelectGame(candidate.Root);
            };
            Refresh();
            Navigate(Core.GetSetupState(preferences.GameRoot).Ready ? "Play" : "Welcome");
            // Choose an image before the window is shown; actual video playback
            // starts only after Loaded, leaving a poster visible while loading.
            ChangeBackdrop(false);
            window.Loaded += async delegate
            {
                UpdateMediaPlayback();
                if (currentBackdrop != null)
                {
                    preferences.LastBackdrop = currentBackdrop.File;
                    try { Core.SavePreferences(preferences); } catch (Exception e) { Core.Log("Backdrop preference not saved: " + e.Message); }
                }
                if (preferences.RotateBackdrops) backdropTimer.Start();
                runtimeTimer.Start(); await PollRuntime();
                await Task.Run(() => CrashReports.RecoverPending(preferences.GameRoot));
                UpdateCrashSummary();
                if (!Core.ValidGame(preferences.GameRoot) && preferences.AutoDetect) await Discover(false);
            };
            Core.Log("Redline Launcher 0.7.2 opened with embedded setup package.");
        }

        public Window Window { get { return window; } }
        T Find<T>(string name) where T : class
        {
            var item = window.FindName(name) as T;
            if (item == null) throw new InvalidOperationException("UI element missing: " + name);
            return item;
        }
        void On(string name, RoutedEventHandler handler) { Find<Button>(name).Click += handler; }
        void ToggleMaximize() { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        void LoadImage(string name, string resource)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                Find<Image>(name).Source = image;
            }
        }
        public void Navigate(string page)
        {
            foreach (var item in pages) item.Value.Visibility = item.Key == page ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in navigation)
            {
                item.Value.Background = Brush(item.Key == page ? "#203548" : "Transparent");
                item.Value.Foreground = Brush(item.Key == page ? "#70E6FC" : "#9AA8BB");
            }
            UpdateMediaPlayback();
        }
        public void ChangeBackdrop(bool saveSelection = true)
        {
            var available = Backdrops.Catalog.Where(x => File.Exists(Path.Combine(Backdrops.Root, x.Poster)) && File.Exists(Path.Combine(Backdrops.Root, x.File)) && !failedVideos.Contains(x.File));
            var next = Backdrops.Choose(available, currentBackdrop == null ? preferences.LastBackdrop : currentBackdrop.File, preferences.AnimateBackdrops, backdropRandom);
            if (next == null) return;
            SetBackdrop(next, saveSelection);
        }
        public void SetBackdrop(Backdrop next, bool saveSelection)
        {
            var video = Find<MediaElement>("HeroVideo");
            video.Close(); video.Source = null; video.Visibility = Visibility.Hidden; videoIsReady = false;
            currentBackdrop = next;
            var poster = new BitmapImage(); poster.BeginInit(); poster.CacheOption = BitmapCacheOption.OnLoad;
            poster.UriSource = new Uri(Path.Combine(Backdrops.Root, next.Poster), UriKind.Absolute); poster.EndInit(); poster.Freeze();
            Find<Image>("HeroImage").Source = poster;
            Find<TextBlock>("BackdropLabel").Text = next.Name + (next.Video ? " / animated" : " / image");
            Find<TextBlock>("BackdropSettingLabel").Text = "Showing: " + next.Name + ". " + (preferences.RotateBackdrops ? "Changes every five minutes." : "Automatic rotation is paused.");
            if (next.Video && preferences.AnimateBackdrops)
            {
                video.Source = new Uri(Path.Combine(Backdrops.Root, next.File), UriKind.Absolute);
                UpdateMediaPlayback();
            }
            if (preferences.RotateBackdrops) { backdropTimer.Stop(); if (window.IsLoaded) backdropTimer.Start(); }
            if (saveSelection || window.IsLoaded)
            {
                preferences.LastBackdrop = next.File;
                try { Core.SavePreferences(preferences); } catch (Exception e) { Core.Log("Backdrop preference not saved: " + e.Message); }
            }
            Core.Log("Backdrop selected: " + next.File);
        }
        void UpdateMediaPlayback()
        {
            var video = Find<MediaElement>("HeroVideo");
            if (currentBackdrop == null || !currentBackdrop.Video || video.Source == null) return;
            bool play = preferences.AnimateBackdrops && window.IsLoaded && window.WindowState != WindowState.Minimized;
            if (play)
            {
                video.Play();
                if (videoIsReady)
                {
                    video.Visibility = Visibility.Visible;
                    if (video.Opacity == 0) video.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
                }
            }
            else video.Pause();
        }
        static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
        void Status(string text) { Find<TextBlock>("StatusBar").Text = text; }
        void Notify(string message) { MessageBox.Show(window, message, "Redline", MessageBoxButton.OK, MessageBoxImage.Information); }
        void Fail(Exception error) { Core.Log(error.Message); Status(error.Message); Notify(error.Message); }
        void SelectGame(string root)
        {
            if (!Core.ValidGame(root)) { Notify("That folder does not contain bin\\x64\\Cyberpunk2077.exe."); return; }
            preferences.GameRoot = Core.NormalizeRoot(root);
            try { Core.SavePreferences(preferences); } catch (Exception e) { Fail(e); }
            Refresh(); Status("Cyberpunk 2077 selected. Your choice is saved on this PC.");
        }
        void Browse()
        {
            if (busy) return;
            var dialog = new OpenFileDialog { Title = "Select Cyberpunk2077.exe inside your game folder", Filter = "Cyberpunk 2077|Cyberpunk2077.exe", CheckFileExists = true };
            if (Core.ValidGame(preferences.GameRoot)) dialog.InitialDirectory = Path.Combine(preferences.GameRoot, "bin", "x64");
            if (dialog.ShowDialog(window) == true)
            {
                try { SelectGame(Core.NormalizeRoot(dialog.FileName)); } catch (Exception e) { Fail(e); }
            }
        }
        async Task Discover(bool userRequested)
        {
            if (busy) return;
            busy = true; Refresh(); Status("Checking Steam libraries, GOG records, and Epic manifests...");
            try
            {
                List<Candidate> candidates = await Task.Run(() => Core.Discover());
                var list = Find<ComboBox>("DetectedGames");
                list.ItemsSource = candidates; list.Visibility = candidates.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                if (candidates.Count == 1) SelectGame(candidates[0].Root);
                else if (candidates.Count > 1)
                { Navigate("Setup"); Status("Multiple game installations found. Choose one from the list."); }
                else
                { Status("Cyberpunk 2077 was not detected. Use Browse in Game files to select Cyberpunk2077.exe."); if (userRequested) Navigate("Setup"); }
            }
            catch (Exception e) { Fail(e); }
            finally { busy = false; Refresh(); }
        }
        void SaveOptions()
        {
            if (changingSettings) return;
            preferences.AutoDetect = Find<CheckBox>("AutoDetectSetting").IsChecked == true;
            preferences.MinimizeOnLaunch = Find<CheckBox>("MinimizeSetting").IsChecked == true;
            preferences.RotateBackdrops = Find<CheckBox>("RotateBackdropSetting").IsChecked == true;
            preferences.AnimateBackdrops = Find<CheckBox>("AnimatedBackdropSetting").IsChecked == true;
            if (preferences.RotateBackdrops) { backdropTimer.Stop(); backdropTimer.Start(); } else backdropTimer.Stop();
            if (!preferences.AnimateBackdrops && currentBackdrop != null && currentBackdrop.Video) ChangeBackdrop();
            else UpdateMediaPlayback();
            if (currentBackdrop != null) Find<TextBlock>("BackdropSettingLabel").Text = "Showing: " + currentBackdrop.Name + ". " + (preferences.RotateBackdrops ? "Changes every five minutes." : "Automatic rotation is paused.");
            try { Core.SavePreferences(preferences); Status("Launcher preferences saved."); } catch (Exception e) { Fail(e); }
        }
        void Refresh()
        {
            bool valid = Core.ValidGame(preferences.GameRoot);
            var setup = Core.GetSetupState(preferences.GameRoot);
            Find<TextBox>("GamePath").Text = String.IsNullOrEmpty(preferences.GameRoot) ? "No game selected" : preferences.GameRoot;
            Find<Button>("LaunchGame").IsEnabled = valid && !busy;
            Find<Button>("LaunchGame").Content = setup.Ready ? "Launch Cyberpunk 2077" : "Set up before launching";
            Find<TextBlock>("WelcomeTitle").Text = setup.Title;
            Find<TextBlock>("WelcomeDetail").Text = setup.Detail;
            Find<TextBlock>("WelcomePath").Text = valid ? preferences.GameRoot : "Cyberpunk 2077 has not been selected yet.";
            Find<TextBlock>("WelcomeGameState").Text = valid ? "GAME FOUND" : "FIND YOUR GAME";
            Find<Button>("WelcomeAction").Content = busy ? "Working..." : setup.NeedsGame ? "Find my game" : setup.Ready ? "Launch Cyberpunk 2077" : setup.CanInstall ? "Set up RED4ext + Redline" : "Refresh setup checks";
            Find<Button>("WelcomeAction").IsEnabled = !busy;
            foreach (string button in new[] { "DetectGame", "BrowseGame", "WelcomeBrowse" }) Find<Button>(button).IsEnabled = !busy;
            // The current mod source has no gameplay adapter. File presence must
            // never unlock a false promise of playable multiplayer.
            Find<Button>("PlayNeonLink").IsEnabled = false;
            Find<TextBlock>("GameLocationSummary").Text = valid ? "Cyberpunk 2077 located" : "Find Cyberpunk 2077";
            Find<TextBlock>("RuntimeSummary").Text = valid && File.Exists(Path.Combine(preferences.GameRoot, Core.PluginDll)) ? "Runtime file detected" : "Development in progress";
            var checks = Core.CheckInstallation(preferences.GameRoot);
            checks.Add(new Check("Bundled file verification", setup.Ready ? "OK" : "SETUP", setup.Detail));
            var rows = Find<StackPanel>("CheckRows"); rows.Children.Clear();
            foreach (var check in checks)
            {
                var border = new Border { Background = Brush("#151D29"), CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 13, 16, 13), Margin = new Thickness(0, 0, 0, 7) };
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
                var name = new TextBlock { Text = check.Name, Foreground = Brush("#E5EDF8"), TextWrapping = TextWrapping.Wrap };
                var state = new TextBlock { Text = check.State, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush(check.State == "FOUND" || check.State == "OK" ? "#6BE6BA" : "#F0C57A"), VerticalAlignment = VerticalAlignment.Center };
                var detail = new TextBlock { Text = check.Detail, Foreground = Brush("#9EADC1"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(state, 1); Grid.SetColumn(detail, 2); grid.Children.Add(name); grid.Children.Add(state); grid.Children.Add(detail); border.Child = grid; rows.Children.Add(border);
            }
            try
            {
                var manifest = Core.ValidatePayload(Core.PayloadRoot);
                Find<TextBlock>("PayloadStatus").Text = "Included in this executable: RED4ext 1.31.0 + Redline native runtime 0.7.0. " + (setup.Ready ? "Installed files match the bundled checksums." : setup.Detail);
                Find<Button>("InstallRuntime").IsEnabled = setup.CanInstall && !busy;
                Find<Button>("InstallRuntime").Content = setup.Ready ? "Verify / repair installation" : "Install / repair RED4ext + Redline";
            }
            catch (Exception e)
            {
                Find<TextBlock>("PayloadStatus").Text = e.Message;
                Find<Button>("InstallRuntime").IsEnabled = false;
            }
            var report = new StringBuilder("REDLINE CLIENT 0.7.2\r\n" + DateTime.Now.ToString("u") + "\r\n\r\n");
            report.AppendLine("Game folder: " + (String.IsNullOrEmpty(preferences.GameRoot) ? "not selected" : preferences.GameRoot));
            foreach (var check in checks) report.AppendLine("[" + check.State + "] " + check.Name + ": " + check.Detail);
            report.AppendLine("\r\nGameplay co-op: in development. Native encrypted transport tests are available after the plugin loads.");
            report.AppendLine("Bundled runtime: " + Find<TextBlock>("PayloadStatus").Text);
            report.AppendLine("Backdrop rotation: " + (preferences.RotateBackdrops ? "every 5 minutes" : "paused") + "; animated backdrops: " + preferences.AnimateBackdrops);
            Find<TextBox>("DiagnosticText").Text = report.ToString();
            UpdateCrashSummary();
        }
        async Task PollRuntime()
        {
            if (pollingRuntime) return;
            pollingRuntime = true;
            try
            {
                await Task.Run(() => CrashReports.AttachExisting(preferences.GameRoot));
                var processes = Process.GetProcessesByName("Cyberpunk2077");
                RuntimeStatus found = null;
                foreach (var process in processes)
                {
                    try { if (found == null) found = await RuntimeClient.CommandAsync(process.Id, "STATUS"); } catch (Exception) { }
                    finally { process.Dispose(); }
                }
                runtimeStatus = found;
                Find<TextBlock>("RuntimeConnectionState").Text = found == null ? "Runtime not connected" : "Runtime loaded / " + found.phase;
                Find<TextBlock>("RuntimeConnectionDetail").Text = found == null
                    ? "Install the bundled runtime, launch Cyberpunk, then return here. Connection tests do not synchronize gameplay."
                    : found.detail + "\nEncrypted frames: " + found.sent + " sent / " + found.received + " received.";
                Find<TextBox>("HostInvite").Text = found == null ? "" : (found.invite ?? "");
                UpdateNetworkControls();
                Find<Button>("CopyInvite").IsEnabled = found != null && !String.IsNullOrEmpty(found.invite);
                Find<TextBlock>("PlayStatus").Text = found == null ? "Co-op is in development" : "Native runtime loaded / transport preview";
            }
            finally { pollingRuntime = false; UpdateCrashSummary(); }
        }
        async Task SendRuntime(string command)
        {
            if (runtimeStatus == null) { Status("Launch Cyberpunk with the installed native runtime first."); return; }
            try { await RuntimeClient.CommandAsync(runtimeStatus.pid, command); await PollRuntime(); }
            catch (Exception e) { Fail(e); }
        }
        async Task HostConnection()
        {
            if (networkBusy || runtimeStatus == null) return;
            try
            {
                string command = InternetHosting.HostCommand(Find<TextBox>("HostAddress").Text.Trim(), LocalPortText(), Find<TextBox>("PublicHost").Text, Find<TextBox>("PublicPort").Text.Trim());
                SaveNetworkOptions(); networkBusy = true; UpdateNetworkControls();
                await RuntimeClient.CommandAsync(runtimeStatus.pid, command); await PollRuntime();
                Find<TextBlock>("HostNetworkHint").Text = InternetHosting.PublicAddress(preferences.PublicHost) ? "Invite uses your public address. A successful local listener does not verify that your router and firewall allow incoming connections." : "This invite uses a private, loopback, or carrier-grade-NAT address. It will not provide ordinary internet access; use a reachable public address for remote guests.";
            }
            catch (Exception e) { Fail(e); }
            finally { networkBusy = false; UpdateNetworkControls(); }
        }
        async Task JoinConnection()
        {
            if (networkBusy || runtimeStatus == null) return;
            try
            {
                var invite = InternetHosting.ParseInvite(Find<TextBox>("JoinInvite").Text);
                networkBusy = true; UpdateNetworkControls(); Status("Resolving the host's address...");
                string address = await InternetHosting.ResolveIPv4(invite.Host);
                await RuntimeClient.CommandAsync(runtimeStatus.pid, "JOINDIRECT " + address + " " + invite.Port + " " + invite.Key); await PollRuntime();
                Status("Direct connection requested. Check the runtime status above.");
            }
            catch (Exception e) { Fail(e); }
            finally { networkBusy = false; UpdateNetworkControls(); }
        }
        void UpdateNetworkControls()
        {
            Version version;
            bool current = runtimeStatus != null && Version.TryParse(runtimeStatus.version, out version) && version >= new Version(0, 7, 0);
            foreach (string name in new[] { "HostConnection", "JoinConnection" }) Find<Button>(name).IsEnabled = current && !networkBusy;
            Find<Button>("StopConnection").IsEnabled = runtimeStatus != null && !networkBusy;
            foreach (string name in new[] { "CreatePortMapping", "RemovePortMapping" }) Find<Button>(name).IsEnabled = !networkBusy;
            foreach (string name in new[] { "HostAddress", "HostPort", "PublicHost", "PublicPort", "RouterTarget" }) Find<TextBox>(name).IsEnabled = !networkBusy;
            Find<CheckBox>("CustomLocalPort").IsEnabled = !networkBusy;
            Find<TextBox>("HostPort").IsEnabled = !networkBusy && Find<CheckBox>("CustomLocalPort").IsChecked == true;
            if (runtimeStatus != null && !current) Find<TextBlock>("RuntimeConnectionDetail").Text = "An older Redline runtime is loaded. Close the game, use Setup & repair to install 0.7.0, then relaunch.";
        }
        void SaveNetworkOptions()
        {
            preferences.HostBindAddress = InternetHosting.IPv4(Find<TextBox>("HostAddress").Text.Trim(), true).ToString();
            preferences.LocalHostPort = InternetHosting.Port(LocalPortText());
            preferences.PublicHostPort = InternetHosting.Port(Find<TextBox>("PublicPort").Text.Trim());
            preferences.PublicHost = Find<TextBox>("PublicHost").Text.Trim(); preferences.RouterTarget = Find<TextBox>("RouterTarget").Text.Trim();
            Core.SavePreferences(preferences);
        }
        string LocalPortText()
        { return Find<TextBox>(Find<CheckBox>("CustomLocalPort").IsChecked == true ? "HostPort" : "PublicPort").Text.Trim(); }
        void SyncLocalPort()
        {
            bool custom = Find<CheckBox>("CustomLocalPort").IsChecked == true;
            if (!custom) Find<TextBox>("HostPort").Text = Find<TextBox>("PublicPort").Text;
            Find<TextBox>("HostPort").IsEnabled = custom && !networkBusy;
        }
        void UpdateMappingStatus()
        {
            try
            {
                var mapping = PortMappingManager.Load();
                Find<TextBlock>("RouterMappingStatus").Text = mapping == null ? "No Redline mapping is recorded. Map port changes your router; Remove mapping undoes the recorded mapping." : "Recorded TCP mapping: " + mapping.ExternalPort + " -> " + mapping.InternalAddress + ":" + mapping.InternalPort + ". Router state is checked when removing it.";
            }
            catch (Exception e) { Find<TextBlock>("RouterMappingStatus").Text = e.Message; }
        }
        async Task MapPort(bool remove)
        {
            if (networkBusy) return;
            networkBusy = true; UpdateNetworkControls();
            try
            {
                if (remove)
                {
                    await Task.Run(() => PortMappingManager.Remove()); Status("Redline router mapping removed.");
                }
                else
                {
                    string address = Find<TextBox>("RouterTarget").Text.Trim();
                    int local = InternetHosting.Port(LocalPortText()), external = InternetHosting.Port(Find<TextBox>("PublicPort").Text.Trim());
                    Status("Checking your UPnP router and requesting the TCP mapping...");
                    var mapping = await Task.Run(() => PortMappingManager.Create(address, local, external));
                    Find<TextBox>("PublicHost").Text = mapping.ExternalAddress; SaveNetworkOptions();
                    Find<TextBlock>("HostNetworkHint").Text = InternetHosting.PublicAddress(mapping.ExternalAddress) ? "Router mapping created and public address filled in. Windows Firewall must also allow the host's inbound TCP port." : "Your router reports a private or carrier-grade-NAT WAN address. Another NAT layer may prevent direct internet hosting even with this mapping.";
                    Status("TCP mapping created. Remove it here when hosting is finished.");
                }
            }
            catch (Exception e)
            {
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                Fail(new IOException("Router setup: " + e.Message + " You can configure the same TCP port forward manually if UPnP is unavailable."));
            }
            finally { networkBusy = false; UpdateNetworkControls(); UpdateMappingStatus(); }
        }
        void Launch()
        {
            try
            {
                if (busy) return;
                var setup = Core.GetSetupState(preferences.GameRoot);
                if (!setup.Ready) { Refresh(); Navigate("Welcome"); Status("Complete setup before launching. " + setup.Detail); return; }
                if (Core.GameRunning()) { Notify("Cyberpunk 2077 is already running."); return; }
                string exe = Path.Combine(preferences.GameRoot, Core.GameExe);
                bool monitored = false;
                using (var process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true }))
                {
                    if (process == null) throw new IOException("Windows did not return the launched game process.");
                    if (process != null)
                        try { CrashReports.Attach(process, preferences.GameRoot); monitored = true; }
                        catch (Exception e) { Core.Log("Crash watcher startup failed: " + e.Message); }
                }
                Core.Log("Game launch requested. Crash monitoring: " + monitored); Status(monitored ? "Cyberpunk 2077 launched. Crash monitoring will continue if you close this launcher." : "Cyberpunk launched, but crash monitoring could not start. Check Diagnostics / logs.");
                if (preferences.MinimizeOnLaunch) window.WindowState = WindowState.Minimized;
            }
            catch (Exception e) { Fail(e); }
        }
        async Task Install()
        {
            if (busy) return;
            busy = true; Refresh();
            try { Status("Installing bundled runtime..."); string message = await Task.Run(() => Core.InstallPayload(preferences.GameRoot)); Refresh(); Status(message); }
            catch (Exception e) { Fail(e); }
            finally { busy = false; Refresh(); }
        }
        async Task Backup()
        {
            if (busy) return;
            busy = true; Find<Button>("BackupSaves").IsEnabled = false;
            try { Status("Copying saves to a dated backup..."); string path = await Task.Run(() => Core.BackupSaves()); Status("Save backup complete."); Notify("Your saves were copied to:\n" + path); }
            catch (Exception e) { Fail(e); }
            finally { busy = false; Find<Button>("BackupSaves").IsEnabled = true; }
        }
        void ExportDiagnostics()
        {
            var dialog = new SaveFileDialog { Title = "Save local diagnostic report", Filter = "Text report|*.txt", FileName = "Redline-diagnostics.txt" };
            if (dialog.ShowDialog(window) == true)
                try { File.WriteAllText(dialog.FileName, CrashReports.Scrub(Find<TextBox>("DiagnosticText").Text, preferences.GameRoot)); Status("Redacted diagnostic report saved. No data was uploaded."); } catch (Exception e) { Fail(e); }
        }
        void UpdateCrashSummary()
        {
            string latest = CrashReports.Latest();
            Find<TextBlock>("CrashReportSummary").Text = latest == null
                ? "No crash reports recorded yet. Monitoring starts when the game launches and continues after the launcher closes."
                : "A crash or unclean-session report is available. Open the reports folder or export the latest report for troubleshooting.";
            Find<Button>("ExportCrashReport").IsEnabled = latest != null;
        }
        void ExportCrashReport()
        {
            var dialog = new SaveFileDialog { Title = "Export local crash report", Filter = "Crash report ZIP|*.zip", FileName = "Redline-crash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip" };
            if (dialog.ShowDialog(window) == true)
                try { CrashReports.ExportLatest(dialog.FileName); Status("Crash report ZIP exported. Nothing was uploaded."); } catch (Exception e) { Fail(e); }
        }
        void OpenFolder(string path) { try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { Fail(e); } }
        void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Fail(e); } }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") return Tests.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "neonlink-tests-" + Guid.NewGuid().ToString("N")));
                if (args.Length == 2 && args[0] == "--test-bundle") return SetupTests.Run(Path.GetFullPath(args[1]));
                if (args.Length == 4 && args[0] == "--test-direct") return DirectTests.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
                if (args.Length == 2 && args[0] == "--watch-process") return CrashReports.Watch(args[1]);
                if (args.Length == 3 && args[0] == "--watch-process-test") { CrashReports.TestRoot = Path.GetFullPath(args[2]); return CrashReports.Watch(args[1]); }
                if (args.Length == 3 && args[0] == "--test-crashes") return CrashReportTests.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                if (args.Length == 4 && args[0] == "--crash-test-starter") return CrashReportTests.StartFixture(args[1], args[2], args[3]);
                if (args.Length == 2 && args[0] == "--test-install") return Tests.Install(Path.GetFullPath(args[1]));
                if (args.Length == 3 && args[0] == "--check-video") return Tests.CheckVideo(args[1], args[2]);
                var app = new Application();
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                { Core.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "Redline"); e.Handled = true; };
                var client = new Client();
                if (args.Length > 1 && args[0] == "--render-preview")
                {
                    // Render the app's real visual tree without opening an interactive window.
                    if (args.Length > 2) client.Navigate(args[2]);
                    if (args.Length > 3)
                    {
                        var backdrop = Backdrops.Catalog.FirstOrDefault(x => x.File == args[3]);
                        if (backdrop != null) client.SetBackdrop(backdrop, false);
                    }
                    var visual = (FrameworkElement)client.Window.Content;
                    visual.Measure(new Size(1180, 780)); visual.Arrange(new Rect(0, 0, 1180, 780)); visual.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(1180, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(args[1])) encoder.Save(stream);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--backdrop")
                {
                    var backdrop = Backdrops.Catalog.FirstOrDefault(x => x.File == args[1]);
                    if (backdrop != null) client.SetBackdrop(backdrop, false);
                }
                return app.Run(client.Window);
            }
            catch (Exception e)
            {
                Core.Log(e.ToString());
                if (args.Length > 0) { Console.Error.WriteLine(e); return 1; }
                MessageBox.Show("The launcher could not start:\n" + e.Message, "Redline Launcher", MessageBoxButton.OK, MessageBoxImage.Error); return 1;
            }
        }
    }

    public static class Tests
    {
        static int count;
        static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); count++; }
        static void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { count++; return; } throw new Exception("FAILED: " + name); }
        public static int Run(string root)
        {
            root = Path.GetFullPath(root); Directory.CreateDirectory(root);
            Assert(Core.CheckInstallation("").Count > 0, "First launch without a game path does not fail");
            var paths = Core.ReadVdfValues("\"path\" \"D:\\\\SteamLibrary\"\n\"path\" \"E:\\\\Games\"", "path").ToArray();
            Assert(paths.Length == 2 && paths[0] == @"D:\SteamLibrary", "Steam library paths unescape correctly");
            var game = Path.Combine(root, "Cyberpunk 2077"); var exe = Path.Combine(game, Core.GameExe);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "fixture");
            Assert(Core.NormalizeRoot(exe) == game, "Selecting game executable resolves game root");
            Assert(Core.ValidGame(game), "Game detection validates executable path");
            var random = new Random(72);
            for (int i = 0; i < 64; i++) Assert(Backdrops.Choose(Backdrops.Catalog, "johnny.jpg", true, random).File != "johnny.jpg", "No immediate backdrop repeat");
            for (int i = 0; i < 32; i++) Assert(!Backdrops.Choose(Backdrops.Catalog, "", false, random).Video, "Images-only setting excludes animated files");
            Assert(Backdrops.RotationInterval == TimeSpan.FromMinutes(5), "Backdrop interval is exactly five minutes");
            Assert(Backdrops.Catalog.Length == 8, "All six supplied pictures and both videos are in the playlist");
            Reject(() => Core.ContainedPath(root, "../escaped.dll"), "Reject parent traversal");
            Reject(() => Core.ContainedPath(root, "C:/escaped.dll"), "Reject absolute payload paths");
            string payload = Path.Combine(root, "payload"); Directory.CreateDirectory(payload);
            var manifest = new PayloadManifest { Version = "test", GameVersion = "2.5" };
            foreach (string name in Core.AllowedFiles.Take(5))
            {
                string file = Core.ContainedPath(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, "payload fixture " + name);
                manifest.Files.Add(new PayloadFile { Path = name, Sha256 = Core.Hash(file) });
            }
            WriteManifest(payload, manifest);
            Assert(Core.ValidatePayload(payload).Files.Count == 5, "Valid bundled runtime manifest accepted");
            File.AppendAllText(Core.ContainedPath(payload, Core.AllowedFiles[0]), "changed");
            Reject(() => Core.ValidatePayload(payload), "Corrupted runtime blocked before installation");
            File.WriteAllText(Core.ContainedPath(payload, Core.AllowedFiles[0]), "payload fixture " + Core.AllowedFiles[0]);
            manifest.Files.Add(manifest.Files[0]); WriteManifest(payload, manifest);
            Reject(() => Core.ValidatePayload(payload), "Ambiguous runtime manifest rejected");
            manifest.Files.RemoveAt(manifest.Files.Count - 1);
            manifest.Files.RemoveAt(0); WriteManifest(payload, manifest);
            Reject(() => Core.ValidatePayload(payload), "Missing loader blocks runtime installation");
            File.WriteAllText(Path.Combine(root, "result.txt"), count + " launcher checks passed.\r\n");
            return 0;
        }
        static void WriteManifest(string root, PayloadManifest manifest)
        { using (var stream = File.Create(Path.Combine(root, "manifest.xml"))) new XmlSerializer(typeof(PayloadManifest)).Serialize(stream, manifest); }
        public static int Install(string game)
        {
            // Used only with a separately compiled fixture executable in work/.
            var manifest = Core.ValidatePayload(Core.PayloadRoot);
            Assert(!Core.GetSetupState(game).Ready && Core.GetSetupState(game).CanInstall, "Missing files require setup");
            string config = Path.Combine(game, "red4ext", "plugins", "NeonLink", "NeonLink.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(config)); File.WriteAllText(config, "fixture config to preserve");
            string unrelated = Path.Combine(game, "red4ext", "plugins", "OtherMod.dll"); File.WriteAllText(unrelated, "unrelated fixture");
            string message = Core.InstallPayload(game);
            foreach (var entry in manifest.Files)
                Assert(Core.Hash(Core.ContainedPath(game, entry.Path)) == entry.Sha256, "Installer copies the verified runtime file");
            Assert(Core.InstallPayload(game).Contains("already installed"), "Reinstall is idempotent");
            Assert(Core.GetSetupState(game).Ready, "Verified setup enables launching");
            File.WriteAllText(Path.Combine(game, Core.PluginDll), "damaged installed plugin");
            Assert(!Core.GetSetupState(game).Ready && Core.GetSetupState(game).CanInstall, "Damaged installation requires repair");
            var before = Directory.Exists(Core.BackupRoot) ? Directory.GetDirectories(Core.BackupRoot) : new string[0];
            Core.InstallPayload(game);
            Assert(Core.GetSetupState(game).Ready, "Repair restores a verified installation");
            var created = Directory.GetDirectories(Core.BackupRoot).Except(before).ToArray();
            Assert(created.Length == 1 && File.ReadAllText(Path.Combine(created[0], Core.PluginDll)) == "damaged installed plugin", "Overwritten files are backed up before repair");
            Assert(File.ReadAllText(config) == "fixture config to preserve", "Existing plugin configuration is preserved");
            Assert(File.ReadAllText(unrelated) == "unrelated fixture", "Unrelated installed mods are preserved");
            File.WriteAllText(Path.Combine(game, "install-result.txt"), count + " installation checks passed.\r\n" + message);
            return 0;
        }
        public static int CheckVideo(string source, string report)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var player = new MediaPlayer { Volume = 0, IsMuted = true };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            var playbackCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            Action<string, int> finish = delegate(string message, int code)
            {
                File.WriteAllText(report, message); player.Close(); timeout.Stop(); playbackCheck.Stop(); app.Shutdown(code);
            };
            timeout.Tick += delegate { finish("Video decoder timed out.", 1); };
            player.MediaFailed += delegate(object sender, ExceptionEventArgs e) { finish(e.ErrorException.ToString(), 1); };
            player.MediaOpened += delegate { player.Play(); playbackCheck.Start(); };
            playbackCheck.Tick += delegate
            {
                bool playing = player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0 && player.Position > TimeSpan.Zero;
                finish((playing ? "PASS " : "FAIL ") + player.NaturalVideoWidth + "x" + player.NaturalVideoHeight + "; position=" + player.Position + "; muted=" + player.IsMuted, playing ? 0 : 1);
            };
            timeout.Start(); player.Open(new Uri(Path.GetFullPath(source)));
            return app.Run();
        }
    }
}
