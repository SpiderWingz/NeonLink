using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NeonLink
{
    public static class PlayerIdentity
    {
        public static string Name(string text)
        {
            string value = (text ?? "").Trim().Normalize(NormalizationForm.FormC);
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                int c = Char.ConvertToUtf32(value, i); if (c > 65535) i++;
                if (c < 32 || (c >= 127 && c <= 159) || c == '<' || c == '>' || c == '&' || c == '"' || c == '\\' ||
                    c == '[' || c == ']' || c == '{' || c == '}' || (c >= 0x200b && c <= 0x200f) ||
                    (c >= 0x2028 && c <= 0x202e) || (c >= 0x2060 && c <= 0x206f) || c == 0xfeff)
                    throw new ArgumentException("Names cannot contain markup, control characters, or hidden direction changes.");
                count++;
            }
            if (count < 1 || count > 24 || Encoding.UTF8.GetByteCount(value) > 96)
                throw new ArgumentException("Choose a player name with 1 to 24 visible characters.");
            return value;
        }
        public static string Build(uint[] values, bool ready)
        {
            if (!ready || values == null || values.Length != 16) return "Load a save to read your character.";
            return String.Format("Level {0} / Street Cred {1}\nBody {2} · Reflexes {3} · Tech {4} · Intelligence {5} · Cool {6}\nUnspent points: {7} perk / {8} attribute / {9} relic", values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9]) +
                String.Format("\nSkills: Headhunter {0} · Netrunner {1} · Shinobi {2} · Solo {3} · Engineer {4}", values[10], values[11], values[12], values[13], values[14]);
        }
    }
    public static class ConnectionProgress
    {
        public static string Title(RuntimeStatus status)
        {
            if (status == null) return "Runtime not connected";
            if (status.preview)
            {
                if (!status.localPlayerReady) return "Local partner test / return to your loaded save";
                return status.remotePlayerReady && status.avatarReportFresh && status.avatarStage == 4 ? "Simulated partner spawned / local test" : "Local partner test / preparing character";
            }
            if (status.phase == "connected")
            {
                if (!status.localPlayerReady) return "Peer connected / load your save";
                if (!status.remotePlayerReady) return "Peer connected / waiting for their character";
                if (status.avatarReportFresh && status.avatarStage == 4) return "Partner character spawned / movement preview";
                if (status.avatarReportFresh && status.avatarStage == 2) return "Peer connected / characters are far apart";
                if (status.avatarReportFresh && status.avatarStage == 5) return "Peer connected / character spawn needs attention";
                return "Peer connected / preparing partner character";
            }
            if (status.phase == "hosting") return "Hosting / waiting for a peer";
            if (status.phase == "connecting" || status.phase == "recovering") return "Connecting / waiting for the host";
            if (status.phase == "rejected") return "Connection rejected";
            if (status.phase == "error") return "Connection error / check details";
            if (status.phase == "starting") return "Preparing connection";
            if (!status.movementBridge) return "Runtime loaded / game adapter is starting";
            return status.localPlayerReady ? "Character ready / choose host, join or local test" : "Runtime loaded / load a save";
        }
        public static string Detail(RuntimeStatus status)
        {
            string progress = "Game adapter: " + (status.movementBridge ? "registered" : "not ready") +
                " | Your character: " + (status.localPlayerReady ? "detected" : "not detected") +
                " | Partner pose: " + (status.remotePlayerReady ? "available" : "waiting");
            if (!String.IsNullOrEmpty(status.remoteName)) progress += "\nPartner: " + status.remoteName + " | Map marker: " + (status.mapMarker ? "active" : "waiting") + " | Nameplate: " + (status.nameplate ? "in view" : "outside view or waiting");
            if (status.preview) return "Simulated partner only. No other player is connected and no server port is opened.\n" + progress;
            return (status.phase == "error" || status.phase == "rejected" ? status.detail + "\n" : "") + progress + "\nEncrypted frames: " + status.sent + " sent / " + status.received + " received.\nMovement preview only: host saves, quests, combat and progression are not synchronized.";
        }
    }
    public class RuntimeStatus
    {
        public string version;
        public string build;
        public string identityKey;
        public int protocol;
        public int pid;
        public int localPort;
        public string phase;
        public string detail;
        public string invite;
        public int peers;
        public ulong sent;
        public ulong received;
        public bool engineRunning;
        public bool gameplayReplication;
        public int movementProtocol;
        public bool movementBridge;
        public bool localPlayerReady;
        public bool remotePlayerReady;
        public ulong movementSent;
        public ulong movementReceived;
        public bool preview;
        public ulong previewSamples;
        public int avatarStage;
        public bool avatarReportFresh;
        public string error;
        public string localName;
        public string remoteName;
        public bool localBuildReady;
        public bool remoteBuildReady;
        public uint[] localBuild;
        public uint[] remoteBuild;
        public bool mapMarker;
        public bool nameplate;
    }

    public static class RuntimeClient
    {
        // Requests never leave this PC. The native side limits access to the
        // current Windows user and exposes no game memory or console commands.
        public static RuntimeStatus Command(int pid, string command)
        {
            if (command == null || Encoding.UTF8.GetByteCount(command) > 900) throw new ArgumentException("Runtime command is too long.");
            using (var pipe = new NamedPipeClientStream(".", "NeonLink.Runtime." + pid, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(600); pipe.ReadMode = PipeTransmissionMode.Message;
                byte[] request = Encoding.UTF8.GetBytes(command + "\n"); pipe.Write(request, 0, request.Length);
                using (var buffer = new MemoryStream())
                {
                    var block = new byte[2048];
                    do
                    {
                        var pending = pipe.BeginRead(block, 0, block.Length, null, null);
                        using (pending.AsyncWaitHandle)
                        {
                            if (!pending.AsyncWaitHandle.WaitOne(2000)) throw new TimeoutException("The runtime did not answer in time.");
                            int read = pipe.EndRead(pending); if (read == 0) throw new IOException("The runtime closed its control pipe.");
                            buffer.Write(block, 0, read);
                        }
                        if (buffer.Length > 4096) throw new InvalidDataException("Unexpected runtime response size.");
                    } while (!pipe.IsMessageComplete);
                    pipe.WriteByte(1);
                    var result = new JavaScriptSerializer { MaxJsonLength = 4096 }.Deserialize<RuntimeStatus>(Encoding.UTF8.GetString(buffer.ToArray()));
                    if (result == null) throw new InvalidDataException("The runtime returned an empty response.");
                    if (!String.IsNullOrEmpty(result.error)) throw new InvalidOperationException(result.error);
                    return result;
                }
            }
        }

        public static Task<RuntimeStatus> CommandAsync(int pid, string command)
        { return Task.Run(() => Command(pid, command)); }
    }
}
