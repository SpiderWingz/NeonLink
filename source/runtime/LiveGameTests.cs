// Opt-in live-game fixture. Starts one simulated guest on loopback and stops
// only the game session whose invite it created. No router or save writes.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NeonLink;
class GameLoopbackTest
{
    static void Wait(Func<bool> condition, string name) {
        var until = DateTime.UtcNow.AddSeconds(12);
        while(DateTime.UtcNow < until) { try { if(condition()) return; } catch(IOException) {} catch(TimeoutException) {} Thread.Sleep(60); }
        throw new Exception("Timed out: " + name);
    }
    static bool ReadPose(string file, out float[] pose) {
        pose = new float[7];
        try {
            if(!File.Exists(file)) return false;
            var parts=File.ReadAllText(file).Split(','); if(parts.Length!=7) return false;
            for(int i=0;i<7;i++) if(!Single.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out pose[i])) return false;
            return true;
        } catch(IOException) { return false; }
    }
    static void WritePose(string file, float[] pose) {
        string temporary=file+".tmp";
        File.WriteAllText(temporary,String.Join(",",pose.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
        try { if(File.Exists(file)) File.Replace(temporary,file,null); else File.Move(temporary,file); }
        catch(IOException) { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    static int Main(string[] args) {
        int gamePid=Int32.Parse(args[0]), peerPid=0; string root=Path.GetFullPath(args[3]);
        string ready=Path.Combine(root,"peer"), invite=""; Process peer=null;
        Directory.CreateDirectory(root);
        try {
            var initial=RuntimeClient.Command(gamePid,"STATUS");
            if(initial.phase!="idle" || !initial.localPlayerReady || !initial.movementBridge) throw new Exception("Load a save and stop existing connections first.");
            var reservation=new TcpListener(IPAddress.Loopback,0); reservation.Start(); int port=((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            RuntimeClient.Command(gamePid,"HOSTDIRECT 127.0.0.1 "+port+" 127.0.0.1 "+port);
            Wait(()=>RuntimeClient.Command(gamePid,"STATUS").phase=="hosting","game listener");
            invite=RuntimeClient.Command(gamePid,"STATUS").invite;
            string key=new Uri(invite).AbsolutePath.Trim('/');
            WritePose(ready+".pose",new float[]{0,0,0,0,0,0,1});
            var start=new ProcessStartInfo(Path.GetFullPath(args[1]),"--serve \""+Path.GetFullPath(args[2])+"\" \""+ready+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            start.EnvironmentVariables["NEONLINK_TEST_LOG_ROOT"]=Path.Combine(root,"runtime-logs");
            peer=Process.Start(start);
            Wait(()=>File.Exists(ready) || peer.HasExited,"fixture startup");
            if(peer.HasExited) throw new Exception(peer.StandardError.ReadToEnd());
            peerPid=Int32.Parse(File.ReadAllText(ready));
            RuntimeClient.Command(peerPid,"NAME Redline Tester");
            File.WriteAllText(ready+".build","42,30,15,12,17,20,9,3,2,1,40,50,30,20,10,1");
            RuntimeClient.Command(peerPid,"JOINDIRECT 127.0.0.1 "+port+" "+key);
            Wait(()=>RuntimeClient.Command(gamePid,"STATUS").phase=="connected","encrypted authentication");
            Console.WriteLine("PASS Live game authenticated a simulated guest over loopback.");
            float[] first=null;
            Wait(()=>ReadPose(ready+".sample",out first),"real player pose received by fixture");
            bool spawned=false, marker=false, label=false, build=false; int updates=0; var began=DateTime.UtcNow;
            while(DateTime.UtcNow-began<TimeSpan.FromSeconds(80)) {
                var host=RuntimeClient.Command(gamePid,"STATUS");
                if(host.invite!=invite || host.phase!="connected") throw new Exception("Host session changed during the test.");
                float[] pose;
                if(ReadPose(ready+".sample",out pose)) {
                    double time=(DateTime.UtcNow-began).TotalSeconds;
                    // REDengine's local forward is +Y. Put the simulated guest
                    // ahead of the published player orientation for HUD QA.
                    float forwardX=2*(pose[3]*pose[4]-pose[6]*pose[5]);
                    float forwardY=1-2*(pose[3]*pose[3]+pose[5]*pose[5]);
                    pose[0]+=forwardX*6+(float)Math.Cos(time)*0.4f;
                    pose[1]+=forwardY*6+(float)Math.Sin(time)*0.4f;
                    WritePose(ready+".pose",pose); updates++;
                }
                spawned |= host.remotePlayerReady && host.avatarReportFresh && host.avatarStage==4;
                marker |= host.mapMarker;
                label |= host.nameplate;
                build |= host.remoteName=="Redline Tester" && host.remoteBuildReady && host.remoteBuild!=null && host.remoteBuild[0]==42 && host.localBuildReady;
                Thread.Sleep(50);
            }
            var final=RuntimeClient.Command(gamePid,"STATUS");
            if(!spawned || final.preview || final.peers!=1 || final.movementReceived<20 || updates<20) throw new Exception("Live encrypted movement did not reach a spawned proxy.");
            Console.WriteLine("PASS Encrypted guest movement reached the real game adapter and a spawned proxy.");
            Console.WriteLine("PASS Sustained bidirectional movement frames exchanged; no external address or router was used.");
            if(!marker || !label || !build) throw new Exception("Player presentation checks: marker="+marker+", label="+label+", personal summaries="+build);
            Console.WriteLine("PASS Real game registered its map marker, displayed an overhead label and read both character summaries.");
            File.WriteAllText(Path.Combine(root,"result.txt"),"Live game + simulated loopback guest: authentication, bidirectional movement, proxy spawn, map marker, overhead name report and personal character summaries passed.\r\n");
            return 0;
        } catch(Exception e) {Console.Error.WriteLine(e.Message);return 1;}
        finally {
            if(peerPid!=0) try {RuntimeClient.Command(peerPid,"STOP");}catch{}
            if(invite!="") try {if(RuntimeClient.Command(gamePid,"STATUS").invite==invite) RuntimeClient.Command(gamePid,"STOP");}catch{}
            File.WriteAllText(ready+".stop","stop");
            if(peer!=null) {if(!peer.WaitForExit(7000)) peer.Kill();peer.Dispose();}
        }
    }
}
