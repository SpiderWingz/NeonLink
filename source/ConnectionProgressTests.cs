using System;
using System.IO;
namespace NeonLink
{
    public static class ConnectionProgressTests
    {
        static int count;
        static void Check(bool value, string description) { if (!value) throw new Exception("Connection status check failed: " + description); ++count; }
        public static int Run(string root)
        {
            Directory.CreateDirectory(root);
            Check(ConnectionProgress.Title(null) == "Runtime not connected", "No process means no connection");
            var status = new RuntimeStatus { phase = "connected", movementBridge = true, peers = 1 };
            Check(ConnectionProgress.Title(status).Contains("load your save"), "An authenticated peer without a world is not reported as joined");
            status.localPlayerReady = true;
            Check(ConnectionProgress.Title(status).Contains("waiting for their character"), "Local player alone is insufficient");
            status.remotePlayerReady = true;
            Check(ConnectionProgress.Title(status).Contains("preparing partner"), "Remote movement alone does not prove a spawned character");
            status.avatarStage = 4;
            Check(!ConnectionProgress.Title(status).Contains("spawned"), "Stale avatar reports cannot claim success");
            status.avatarReportFresh = true;
            Check(ConnectionProgress.Title(status) == "Partner character spawned / movement preview", "Success requires both world samples and a fresh spawn report");
            status.remotePlayerReady = false;
            Check(!ConnectionProgress.Title(status).Contains("spawned"), "A fresh spawn report cannot outlive the remote player");
            status.remotePlayerReady = true; status.avatarStage = 2;
            Check(ConnectionProgress.Title(status).Contains("far apart"), "Out-of-range partners are explained");
            status.avatarStage = 5;
            Check(ConnectionProgress.Title(status).Contains("needs attention"), "Failed spawns do not appear joined");
            status.preview = true; status.avatarStage = 4;
            Check(ConnectionProgress.Title(status) == "Simulated partner spawned / local test", "Simulated characters never claim another person joined");
            Check(ConnectionProgress.Detail(status).Contains("No other player is connected"), "Local test limitations stay visible");
            status.localPlayerReady = false;
            Check(ConnectionProgress.Title(status).Contains("return to your loaded save"), "Paused games receive a resume instruction instead of a false spawn result");
            status.localPlayerReady = true;
            status.preview = false; status.phase = "error"; status.detail = "Listener unavailable";
            Check(ConnectionProgress.Title(status).Contains("error") && ConnectionProgress.Detail(status).Contains("Listener unavailable"), "Native failures remain actionable");
            File.WriteAllText(Path.Combine(root, "connection-status-result.txt"), count + " connection progress checks passed.\r\n");
            return 0;
        }
    }
}
