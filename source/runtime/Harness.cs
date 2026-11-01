using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NeonLink;

class Harness
{
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibrary(string file);
    [DllImport("kernel32", CharSet=CharSet.Ansi, SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Supports();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Query(IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool MainPlugin(IntPtr module, byte reason, IntPtr sdk);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool MovementValues([In, Out] float[] values);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ClearMovement();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool TestStart();
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); checks++; Console.WriteLine("PASS " + name); }
    static T Export<T>(IntPtr module, string name) where T : class
    {
        var address = GetProcAddress(module, name); if (address == IntPtr.Zero) throw new Exception("Missing export: " + name);
        return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
    }
    static int Serve(string dll, string ready, bool strict = false)
    {
        IntPtr module = LoadLibrary(dll); if (module == IntPtr.Zero) throw new Exception("LoadLibrary failed: " + Marshal.GetLastWin32Error());
        var main = Export<MainPlugin>(module, "Main"); bool started = false; IntPtr sdk = IntPtr.Zero;
        try
        {
            if (Export<Supports>(module, "Supports")() != 1) throw new Exception("Unsupported loader API.");
            IntPtr info = Marshal.AllocHGlobal(56);
            try
            {
                Export<Query>(module, "Query")(info);
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, 16)) != "NeonLink") throw new Exception("Plugin identity mismatch.");
                ushort runtimeMajor = (ushort)Marshal.ReadInt16(info, 48);
                if (runtimeMajor != 65535 && (runtimeMajor != 3 || (ushort)Marshal.ReadInt16(info, 52) != 88 || (ushort)Marshal.ReadInt16(info, 54) != 46235)) throw new Exception("Unexpected engine version gate.");
            }
            finally { Marshal.FreeHGlobal(info); }
            // A zeroed, non-null API table exercises normal privacy restrictions
            // without invoking the real game or any SDK callback.
            if (strict) { sdk = Marshal.AllocHGlobal(256); Marshal.Copy(new byte[256], 0, sdk, 256); }
            started = main(module, 0, sdk); if (!started) throw new Exception("Native runtime initialization failed.");
            if (Export<TestStart>(module, "NeonLinkTestStart")()) throw new Exception("Loaded runtime entered fixture mode through the smoke-test export.");
            File.WriteAllText(ready, Process.GetCurrentProcess().Id.ToString());
            var deadline = DateTime.UtcNow.AddMinutes(5);
            var publish = Export<MovementValues>(module, "NeonLinkTestPublish");
            var sample = Export<MovementValues>(module, "NeonLinkTestSample");
            var clear = Export<ClearMovement>(module, "NeonLinkTestClear");
            while (!File.Exists(ready + ".stop") && DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(ready + ".pose"))
                    {
                        string text = File.ReadAllText(ready + ".pose");
                        if (text == "clear") clear();
                        else
                        {
                            float[] values = text.Split(',').Select(v => Single.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                            if (values.Length == 7) File.WriteAllText(ready + ".accepted", publish(values).ToString());
                        }
                        var remote = new float[7];
                        File.WriteAllText(ready + ".sample", sample(remote) ? String.Join(",", remote.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) : "absent");
                    }
                }
                catch (IOException) { } // A fixture writer may be replacing its input.
                Thread.Sleep(15);
            }
        }
        finally { if (started) main(module, 1, sdk); if (sdk != IntPtr.Zero) Marshal.FreeHGlobal(sdk); FreeLibrary(module); }
        return 0;
    }
    static Process Spawn(string dll, string ready)
    {
        var info = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
            "--serve \"" + dll + "\" \"" + ready + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        string logs = Path.Combine(Path.GetDirectoryName(ready), "runtime-logs"); Directory.CreateDirectory(logs);
        info.EnvironmentVariables["NEONLINK_TEST_LOG_ROOT"] = logs;
        return Process.Start(info);
    }
    static int WaitReady(string file, Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(file) && DateTime.UtcNow < deadline && !process.HasExited) Thread.Sleep(50);
        if (!File.Exists(file)) throw new Exception("Native harness did not become ready: " + (process.HasExited ? process.StandardError.ReadToEnd() : "timed out"));
        return Int32.Parse(File.ReadAllText(file));
    }
    static RuntimeStatus WaitPhase(int pid, string phase, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds); RuntimeStatus last = null;
        while (DateTime.UtcNow < deadline)
        {
            try { last = RuntimeClient.Command(pid, "STATUS"); if (last.phase == phase) return last; } catch (IOException) { } catch (TimeoutException) { }
            Thread.Sleep(60);
        }
        throw new Exception("Expected " + phase + ", got " + (last == null ? "no reply" : last.phase));
    }
    static void ExpectInvalid(int pid, string command)
    {
        try { RuntimeClient.Command(pid, command); } catch (InvalidOperationException) { checks++; return; }
        throw new Exception("Malformed command was accepted.");
    }
    static int Test(string dll, string folder)
    {
        Directory.CreateDirectory(folder); string hostFile = Path.Combine(folder, "host-" + Guid.NewGuid().ToString("N")); string guestFile = Path.Combine(folder, "guest-" + Guid.NewGuid().ToString("N"));
        Process host = null, guest = null; string inviteKey = "";
        try
        {
            host = Spawn(dll, hostFile); guest = Spawn(dll, guestFile);
            int hp = WaitReady(hostFile, host), gp = WaitReady(guestFile, guest);
            Check(WaitPhase(hp, "idle", 4).gameplayReplication == false, "Real DLL loads with truthful gameplay status");
            Check(WaitPhase(gp, "idle", 4).protocol == 1, "Second native DLL instance initializes");
            foreach (string bad in new[] { "HOST localhost 47890", "HOST 127.0.0.1 0", "HOST 127.0.0.1 65536", "JOIN 127.0.0.1 47890 short", "STOP extra", "UNKNOWN", "HOST 0.0.0.0 47890" }) ExpectInvalid(hp, bad);
            Check(RuntimeClient.Command(hp, "STATUS").phase == "idle", "Invalid local commands leave the runtime idle");
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            RuntimeClient.Command(hp, "HOST 127.0.0.1 " + port); var listening = WaitPhase(hp, "hosting", 5);
            string secret = new Uri(listening.invite).AbsolutePath.Trim('/');
            inviteKey = secret;
            Check(secret.Length == 64, "Host creates a fresh 256-bit invite key");
            RuntimeClient.Command(gp, "JOIN 127.0.0.1 " + port + " " + new string('0', 64));
            Check(WaitPhase(gp, "rejected", 7).peers == 0, "Wrong invite key is rejected cryptographically");
            WaitPhase(hp, "hosting", 5);
            RuntimeClient.Command(gp, "JOIN 127.0.0.1 " + port + " " + secret);
            Check(WaitPhase(gp, "connected", 7).peers == 1 && WaitPhase(hp, "connected", 5).peers == 1, "Both native peers establish an encrypted connection");
            Thread.Sleep(2200);
            var hs = RuntimeClient.Command(hp, "STATUS"); var gs = RuntimeClient.Command(gp, "STATUS");
            Check(hs.sent >= 3 && hs.received >= 3 && gs.sent >= 3 && gs.received >= 3, "Encrypted authenticated frames flow in both directions");
            RuntimeClient.Command(gp, "STOP"); WaitPhase(hp, "hosting", 5);
            Check(RuntimeClient.Command(gp, "STATUS").phase == "idle", "Guest stops cleanly without ending the host listener");
            RuntimeClient.Command(gp, "JOIN 127.0.0.1 " + port + " " + secret);
            Check(WaitPhase(gp, "connected", 5).peers == 1, "Guest can reconnect to the same host invite");
            string hostLog = Directory.GetFiles(Path.Combine(folder, "runtime-logs"), "runtime-" + hp + "-*.log").Single();
            File.AppendAllText(hostLog, String.Concat(Enumerable.Repeat("old breadcrumb fixture\n", 16000)));
            RuntimeClient.Command(gp, "STOP"); RuntimeClient.Command(hp, "STOP");
            Check(WaitPhase(hp, "idle", 5).peers == 0, "Host shutdown returns to idle");
            Check(new FileInfo(hostLog).Length < 260000 && File.ReadAllText(hostLog).Contains("Connection stop requested."), "Native log rolls forward to retain recent events at its size limit");
        }
        finally
        {
            File.WriteAllText(hostFile + ".stop", "stop"); File.WriteAllText(guestFile + ".stop", "stop");
            foreach (var process in new[] { host, guest }.Where(p => p != null))
            {
                if (!process.WaitForExit(10000)) throw new Exception("Native DLL did not unload within 10 seconds.");
                if (process.ExitCode != 0) throw new Exception("Native child failed: " + process.StandardError.ReadToEnd()); process.Dispose();
            }
        }
        string logRoot = Path.Combine(folder, "runtime-logs");
        Check(Directory.GetFiles(logRoot, "*.log").Length == 2, "Both native instances leave runtime breadcrumbs");
        Check(Directory.GetFiles(logRoot, "*.closed").Length == 2 && Directory.GetFiles(logRoot, "*.active").Length == 0, "Clean unload retires each active-session marker");
        Check(!String.Concat(Directory.GetFiles(logRoot, "*.log").Select(File.ReadAllText)).Contains(inviteKey), "Native breadcrumbs never contain the invite key");
        File.WriteAllText(Path.Combine(folder, "result.txt"), checks + " native runtime integration checks passed.\r\n");
        return 0;
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--serve") return Serve(Path.GetFullPath(args[1]), args[2]);
            if (args.Length == 3 && args[0] == "--serve-strict") return Serve(Path.GetFullPath(args[1]), args[2], true);
            if (args.Length == 3 && args[0] == "--test") return Test(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            if (args.Length == 3 && args[0] == "--test-movement") return MovementIntegrationTests.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            if (args.Length == 3 && args[0] == "--test-direct") return DirectTests.Run(Path.GetFullPath(args[2]), Process.GetCurrentProcess().MainModule.FileName, Path.GetFullPath(args[1]));
            Console.Error.WriteLine("Usage: RuntimeHarness --test NeonLink.dll work-folder"); return 2;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
