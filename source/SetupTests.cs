using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NeonLink
{
    public static class SetupTests
    {
        static int checks;
        static void Assert(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); checks++; }
        static void Reject(Action action, string message)
        { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("FAILED: " + message); }
        public static int Run(string root)
        {
            Directory.CreateDirectory(root);
            byte[] bytes = BundledContent.Resource();
            string cache = Path.Combine(root, "standalone-cache");
            BundledContent.Extract(bytes, cache);
            string payload = Path.Combine(cache, "payload");
            Assert(Core.ValidatePayload(payload).Files.Count == 5, "Extracted payload contains the complete loader and native plugin");
            Assert(Backdrops.Catalog.All(x => File.Exists(Path.Combine(cache, "assets", "backdrops", x.File)) && File.Exists(Path.Combine(cache, "assets", "backdrops", x.Poster))), "All eight backdrops are embedded");
            Assert(File.Exists(Path.Combine(cache, "licenses", "RED4ext-SDK-MIT.txt")) && File.Exists(Path.Combine(cache, "licenses", "libsodium-ISC.txt")), "Required component licenses included");
            string loader = Path.Combine(payload, "bin", "x64", "winmm.dll");
            string original = Core.Hash(loader);
            DateTime unchanged = File.GetLastWriteTimeUtc(loader);
            BundledContent.Extract(bytes, cache);
            Assert(File.GetLastWriteTimeUtc(loader) == unchanged, "Valid cached files are reused");
            File.WriteAllText(loader, "damaged cache");
            BundledContent.Extract(bytes, cache);
            Assert(Core.Hash(loader) == original, "Damaged cached files are repaired from the executable");
            string media = Path.Combine(cache, "assets", "backdrops", "johnny.jpg"); File.Delete(media);
            BundledContent.Extract(bytes, cache);
            Assert(File.Exists(media), "Deleted cached media is restored");
            Reject(() => BundledContent.Extract(Archive("../escape.dll"), Path.Combine(root, "traversal")), "Archive path traversal is rejected");
            Reject(() => BundledContent.Extract(Archive("C:/escape.dll"), Path.Combine(root, "absolute")), "Rooted archive paths are rejected");
            Reject(() => BundledContent.Extract(Archive("README.txt", "README.txt"), Path.Combine(root, "duplicate")), "Duplicate archive entries are rejected");
            Reject(() => BundledContent.Extract(Archive("README.txt"), Path.Combine(root, "no-index")), "Archive without an index is rejected");
            Assert(!File.Exists(Path.Combine(root, "escape.dll")), "Rejected entries never write outside the cache");
            Assert(Core.GetSetupState("").NeedsGame && !Core.GetSetupState("").Ready, "First launch requires game selection");
            Assert(!Core.GetSetupState(Path.Combine(root, "missing-game")).CanInstall, "Installation cannot start without a game");
            File.WriteAllText(Path.Combine(root, "bundle-result.txt"), checks + " standalone bundle checks passed.\r\n");
            return 0;
        }
        static byte[] Archive(params string[] names)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                    foreach (string name in names) using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("fixture");
                return memory.ToArray();
            }
        }
    }
}
