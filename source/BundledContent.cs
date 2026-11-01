using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace NeonLink
{
    [XmlRoot("NeonLinkBundle")]
    public class BundleIndex
    {
        [XmlAttribute] public string Version;
        [XmlElement("File")] public List<BundleFile> Files = new List<BundleFile>();
    }
    public class BundleFile
    {
        [XmlAttribute] public string Path;
        [XmlAttribute] public long Length;
        [XmlAttribute] public string Sha256;
    }

    public static class BundledContent
    {
        public const string Version = "0.7.2";
        static readonly Lazy<string> cachedRoot = new Lazy<string>(Prepare);
        public static string Root { get { return cachedRoot.Value; } }

        internal static byte[] Resource()
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeonLink.Bundle.zip"))
            {
                if (source == null || source.Length > 64 * 1024 * 1024) throw new InvalidDataException("The embedded Redline package is missing or too large.");
                using (var memory = new MemoryStream()) { source.CopyTo(memory); return memory.ToArray(); }
            }
        }
        internal static string Digest(byte[] data)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }

        static string Prepare()
        {
            string checksum;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeonLink.Bundle.sha256"))
            using (var reader = new StreamReader(stream)) checksum = reader.ReadToEnd().Trim();
            byte[] bytes = Resource();
            if (!Regex.IsMatch(checksum, "^[0-9a-f]{64}$") || Digest(bytes) != checksum)
                throw new InvalidDataException("The embedded Redline package failed verification. Download a fresh copy of the launcher.");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonLink", "client-cache", Version + "-" + checksum.Substring(0, 16));
            using (var mutex = new Mutex(false, @"Local\NeonLink.Bundle." + checksum.Substring(0, 24)))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(TimeSpan.FromSeconds(45)); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Another Redline window is preparing the included files. Try again shortly.");
                    Extract(bytes, root);
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
            return root;
        }

        internal static bool Allowed(string path)
        {
            if (String.IsNullOrEmpty(path) || path.Contains("\\") || path.Contains(":") || path.StartsWith("/") || path.Split('/').Any(x => x == ".." || x == "." || x.Length == 0)) return false;
            if (new[] { "README.txt", "RUNTIME-README.txt", "CRASH-REPORTING.txt", "CREDITS.txt", "payload/manifest.xml" }.Contains(path, StringComparer.Ordinal)) return true;
            if (path.StartsWith("payload/", StringComparison.Ordinal))
                return Core.AllowedFiles.Contains(path.Substring(8), StringComparer.Ordinal);
            if (path.StartsWith("licenses/", StringComparison.Ordinal))
                return Regex.IsMatch(path, @"^licenses/[A-Za-z0-9_-]+\.txt$");
            return Regex.IsMatch(path, @"^assets/backdrops/[a-z0-9-]+\.(jpg|png|mp4)$");
        }

        static string Destination(string root, string relative)
        {
            if (!Allowed(relative)) throw new InvalidDataException("Unexpected bundled path: " + relative);
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', '\\')));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A bundled path escapes its cache.");
            // Refuse junctions or symlinks in any existing destination ancestor.
            for (string cursor = path; !String.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Redline's file cache contains a redirected path: " + cursor);
            return path;
        }

        internal static void Extract(byte[] bytes, string root)
        {
            using (var memory = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (zip.Entries.Count > 80) throw new InvalidDataException("Too many bundled files.");
                foreach (var entry in zip.Entries)
                    if (!names.Add(entry.FullName) || (entry.FullName != "bundle-index.xml" && !Allowed(entry.FullName)))
                        throw new InvalidDataException("Unexpected or duplicate bundled entry.");
                var indexEntry = zip.GetEntry("bundle-index.xml");
                if (indexEntry == null || indexEntry.Length > 1024 * 1024) throw new InvalidDataException("The bundled file index is missing or invalid.");
                BundleIndex index;
                using (var input = indexEntry.Open())
                using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                    index = (BundleIndex)new XmlSerializer(typeof(BundleIndex)).Deserialize(reader);
                if (index.Version != Version || index.Files == null || index.Files.Count + 1 != zip.Entries.Count) throw new InvalidDataException("The bundled file index is incomplete.");
                names.Clear(); long total = 0;
                foreach (var file in index.Files)
                {
                    var entry = zip.GetEntry(file.Path ?? "");
                    if (!Allowed(file.Path) || !names.Add(file.Path) || file.Length < 0 || file.Length > 32 * 1024 * 1024 || entry == null || entry.Length != file.Length || !Regex.IsMatch(file.Sha256 ?? "", "^[0-9a-f]{64}$"))
                        throw new InvalidDataException("An embedded file failed index verification.");
                    total += file.Length;
                    if (total > 128 * 1024 * 1024) throw new InvalidDataException("The embedded package is too large.");
                }
                // Validate the complete index before writing anything.
                foreach (var file in index.Files)
                {
                    string destination = Destination(root, file.Path);
                    if (File.Exists(destination) && new FileInfo(destination).Length == file.Length && Core.Hash(destination) == file.Sha256) continue;
                    byte[] content;
                    using (var input = zip.GetEntry(file.Path).Open())
                    using (var output = new MemoryStream()) { input.CopyTo(output); content = output.ToArray(); }
                    if (content.LongLength != file.Length || Digest(content) != file.Sha256) throw new InvalidDataException("An embedded file failed checksum verification: " + file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, content);
                        if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
        }
    }
}
