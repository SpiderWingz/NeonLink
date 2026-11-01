using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NeonLink
{
    public class RuntimeStatus
    {
        public string version;
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
