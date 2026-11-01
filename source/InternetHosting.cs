using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace NeonLink
{
    public class PeerInvite
    {
        public string Host;
        public int Port;
        public string Key;
    }
    public static class InternetHosting
    {
        public static int Port(string text)
        {
            int value;
            if (!Regex.IsMatch(text ?? "", "^[0-9]{1,5}$") || !Int32.TryParse(text, out value) || value < 1 || value > 65535)
                throw new ArgumentException("Ports must be numbers from 1 to 65535.");
            return value;
        }
        public static IPAddress IPv4(string text, bool bind)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(text, out ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.ToString() != text || (!bind && ip.Equals(IPAddress.Any)) || ip.Equals(IPAddress.Broadcast) || ip.GetAddressBytes()[0] >= 224)
                throw new ArgumentException("Use a dotted IPv4 address. 0.0.0.0 is allowed only as a listen address.");
            return ip;
        }
        public static string Host(string text)
        {
            text = (text ?? "").Trim().ToLowerInvariant();
            if (text.Length < 1 || text.Length > 253) throw new ArgumentException("Enter the host's public IPv4 address or DNS name.");
            if (Regex.IsMatch(text, "^[0-9.]+$")) return IPv4(text, false).ToString();
            if (!text.Split('.').All(x => Regex.IsMatch(x, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")))
                throw new ArgumentException("Use a DNS name without a URL, path, spaces, or port.");
            return text;
        }
        public static bool PublicAddress(string text)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(text, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return true; // DNS cannot be classified until resolved.
            byte[] b = ip.GetAddressBytes();
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
        public static string HostCommand(string bind, string localPort, string publicHost, string publicPort)
        { return "HOSTDIRECT " + IPv4(bind, true) + " " + Port(localPort) + " " + Host(publicHost) + " " + Port(publicPort); }
        public static PeerInvite ParseInvite(string text)
        {
            text = (text ?? "").Trim(); Uri uri;
            if (text.Length > 400 || !Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.Scheme != "nlink" || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment) || uri.Port < 1 || uri.Port > 65535 || uri.AbsolutePath.Length != 65 || !Regex.IsMatch(uri.AbsolutePath.Substring(1), "^[0-9a-fA-F]{64}$") || text.Contains("%") || text.Contains("\\"))
                throw new ArgumentException("Paste the full nlink://host:port/key invite from your host.");
            return new PeerInvite { Host = Host(uri.Host), Port = uri.Port, Key = uri.AbsolutePath.Substring(1).ToLowerInvariant() };
        }
        public static async Task<string> ResolveIPv4(string host)
        {
            host = Host(host); IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return IPv4(host, false).ToString();
            var lookup = Dns.GetHostAddressesAsync(host);
            if (await Task.WhenAny(lookup, Task.Delay(5000)) != lookup) throw new TimeoutException("The host's DNS lookup timed out. Check the invite address.");
            var result = (await lookup).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !x.Equals(IPAddress.Any) && !x.Equals(IPAddress.Broadcast) && x.GetAddressBytes()[0] < 224);
            if (result == null) throw new IOException("The host name has no usable IPv4 address. IPv6-only hosts are not supported in this build.");
            return result.ToString();
        }
        public static string LocalAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                {
                    var properties = nic.GetIPProperties();
                    if (!properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any))) continue;
                    var address = properties.UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address));
                    if (address != null) return address.Address.ToString();
                }
            }
            catch { }
            return "";
        }
    }

    public class RouterMapping
    {
        public int ExternalPort;
        public int InternalPort;
        public string InternalAddress;
        public string ExternalAddress;
        public string Description;
        public bool Enabled;
        public string Protocol = "TCP";
    }
    // A small boundary permits deterministic tests without touching a real router.
    public interface IRouterMappings : IDisposable
    {
        RouterMapping Find(int port);
        RouterMapping Add(RouterMapping mapping);
        void Remove(int port);
    }
    public sealed class WindowsRouterMappings : IRouterMappings
    {
        object nat, collection;
        static object Get(object target, string name)
        { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture); }
        static object Call(object target, string name, params object[] args)
        { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture); }
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        public WindowsRouterMappings()
        {
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (type == null) throw new IOException("Windows UPnP support is unavailable.");
                nat = Activator.CreateInstance(type); collection = Get(nat, "StaticPortMappingCollection");
                if (collection == null) throw new IOException("No UPnP router was found. Enable UPnP on your router or forward the TCP port manually.");
            }
            catch { Dispose(); throw; }
        }
        static RouterMapping Snapshot(object mapping)
        {
            return new RouterMapping { ExternalPort = Convert.ToInt32(Get(mapping, "ExternalPort")), InternalPort = Convert.ToInt32(Get(mapping, "InternalPort")), InternalAddress = Convert.ToString(Get(mapping, "InternalClient")), ExternalAddress = Convert.ToString(Get(mapping, "ExternalIPAddress")), Description = Convert.ToString(Get(mapping, "Description")), Enabled = Convert.ToBoolean(Get(mapping, "Enabled")), Protocol = Convert.ToString(Get(mapping, "Protocol")) };
        }
        public RouterMapping Find(int port)
        {
            int count = 0;
            foreach (object entry in (IEnumerable)collection)
                try
                {
                    if (++count > 4096) throw new IOException("The router returned too many port mappings.");
                    var value = Snapshot(entry);
                    if (value.ExternalPort == port && value.Protocol == "TCP") return value;
                }
                finally { Release(entry); }
            return null;
        }
        public RouterMapping Add(RouterMapping mapping)
        {
            object added = Call(collection, "Add", mapping.ExternalPort, "TCP", mapping.InternalPort, mapping.InternalAddress, true, mapping.Description);
            try { return Snapshot(added); } finally { Release(added); }
        }
        public void Remove(int port) { Call(collection, "Remove", port, "TCP"); }
        public void Dispose() { Release(collection); Release(nat); collection = nat = null; }
    }
    public static class PortMappingManager
    {
        public static string RecordPath { get { return Path.Combine(Core.DataRoot, "router-mapping.xml"); } }
        public static bool Same(RouterMapping a, RouterMapping b)
        { return a != null && b != null && a.ExternalPort == b.ExternalPort && a.InternalPort == b.InternalPort && a.InternalAddress == b.InternalAddress && a.Description == b.Description && a.Protocol == "TCP" && b.Protocol == "TCP"; }
        public static RouterMapping Create(IRouterMappings router, string address, int localPort, int externalPort)
        {
            var ip = InternetHosting.IPv4(address, false);
            if (IPAddress.IsLoopback(ip)) throw new ArgumentException("Use this PC's LAN address for router mapping, not loopback.");
            InternetHosting.Port(localPort.ToString()); InternetHosting.Port(externalPort.ToString());
            if (router.Find(externalPort) != null) throw new IOException("That external TCP port already has a mapping. Choose another port or remove your previous NeonLink mapping first.");
            var wanted = new RouterMapping { InternalAddress = address, InternalPort = localPort, ExternalPort = externalPort, Description = "NeonLink-" + Guid.NewGuid().ToString("N"), Enabled = true };
            try
            {
                var result = router.Add(wanted);
                if (!Same(result, wanted) || !result.Enabled) throw new IOException("The router did not confirm the requested port mapping.");
                InternetHosting.IPv4(result.ExternalAddress, false);
                return result;
            }
            catch { if (Same(router.Find(externalPort), wanted)) router.Remove(externalPort); throw; }
        }
        public static void Remove(IRouterMappings router, RouterMapping owned)
        {
            if (owned == null || !Regex.IsMatch(owned.Description ?? "", "^NeonLink-[0-9a-f]{32}$")) throw new IOException("There is no recorded NeonLink mapping to remove.");
            var current = router.Find(owned.ExternalPort);
            if (current == null) return;
            if (!Same(current, owned)) throw new IOException("That router mapping has changed. It was left untouched.");
            router.Remove(owned.ExternalPort);
        }
        public static RouterMapping Load()
        { return File.Exists(RecordPath) ? Core.ReadXml<RouterMapping>(RecordPath) : null; }
        public static RouterMapping Create(string address, int localPort, int externalPort)
        {
            if (File.Exists(RecordPath)) throw new IOException("A NeonLink mapping is already recorded. Remove it before creating another.");
            using (var router = new WindowsRouterMappings())
            {
                var result = Create(router, address, localPort, externalPort);
                string temporary = RecordPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Core.DataRoot);
                    using (var output = File.Create(temporary)) new XmlSerializer(typeof(RouterMapping)).Serialize(output, result);
                    File.Move(temporary, RecordPath); return result;
                }
                catch { Remove(router, result); throw; }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        public static void Remove()
        {
            var owned = Load();
            if (owned == null) throw new IOException("No NeonLink-created router mapping is recorded.");
            using (var router = new WindowsRouterMappings()) Remove(router, owned);
            File.Delete(RecordPath);
        }
    }
}
