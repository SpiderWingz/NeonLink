using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NeonLink
{
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
