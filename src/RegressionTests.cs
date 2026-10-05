using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PowerPal {
    internal static class RegressionTests {
        static void Check(bool condition,string name) { if(!condition) throw new Exception(name); }
        public static void Run(string folder) {
            Check(ActivityMonitor.CpuPercent(2000,2,4)==25,"CPU normalization");
            Check(ActivityMonitor.CpuPercent(-1,2,4)==0 && ActivityMonitor.CpuPercent(100,0,4)==0,"CPU resets");
            string digest=new string('a',64);
            string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v0.3.0\",\"assets\":[{\"name\":\"PowerPal-Setup-0.3.0-win-x64.exe\",\"url\":\"https://api.github.com/repos/cdibona/PowerPal/releases/assets/123\",\"size\":20,\"digest\":\"sha256:"+digest+"\"}]}";
            var release=ReleaseUpdater.Parse(json); Check(release.Version==new Version(0,3,0,0) && release.Digest==digest,"Release version");
            Check(ReleaseUpdater.Parse(json.Replace("\"prerelease\":false","\"prerelease\":true"))==null,"Skip prerelease");
            Check(ReleaseUpdater.Parse(json.Replace("v0.3.0","v0.3.0-beta"))==null,"Skip unstable tags");
            bool rejected=false; try { ReleaseUpdater.Parse(json.Replace("api.github.com","evil.example")); } catch(InvalidDataException) { rejected=true; } Check(rejected,"Reject untrusted update URL");
            rejected=false; try { ReleaseUpdater.Parse(json.Replace("sha256:","sha1:")); } catch(InvalidDataException) { rejected=true; } Check(rejected,"Require SHA-256");
            Check(!ReleaseUpdater.ValidDownload(new Uri("http://release-assets.githubusercontent.com/a")) && !ReleaseUpdater.ValidDownload(new Uri("https://api.github.com/repos/other/project/releases/assets/123")),"Reject insecure or other-repo download");
            string file=Path.Combine(folder,"digest.txt"); File.WriteAllText(file,"PowerPal fixture"); string expected;
            using(var sha=SHA256.Create()) expected=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","");
            Check(ReleaseUpdater.Verify(file,expected,new FileInfo(file).Length),"Good digest");
            Check(!ReleaseUpdater.Verify(file,new string('0',64),new FileInfo(file).Length),"Tampered digest");
            Check(!ReleaseUpdater.Verify(file,expected,999),"Wrong length");
            var history=new History(Path.Combine(folder,"repair")); var s=new Sample(); history.Append(new[]{s});
            File.AppendAllText(Directory.GetFiles(history.Folder)[0],"partial"); history.Append(new[]{s}); Check(history.Load(DateTime.UtcNow.AddDays(-1)).Count==2,"Recover torn CSV append");
            Check(Program.BackgroundOnStart(new string[0]) && Program.BackgroundOnStart(new[]{"--background"}) && !Program.BackgroundOnStart(new[]{"--show"}),"Install opens dashboard; normal startup stays hidden");
            var logs=new ActivityHistory(Path.Combine(folder,"activity"));
            var consumers=new List<Consumer> { new Consumer { Name="Browser, \"work\"",Cpu=12.5,MemoryMb=512.25,DiskMb=null },new Consumer { Name="=unsafe",Cpu=1,MemoryMb=20,DiskMb=0 } };
            DateTime now=DateTime.UtcNow;
            var culture=System.Threading.Thread.CurrentThread.CurrentCulture;
            try { System.Threading.Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE"); Check(logs.Append(now,consumers,2.0,3),"Activity snapshot persisted"); } finally { System.Threading.Thread.CurrentThread.CurrentCulture=culture; }
            var exported=logs.Lines(now.AddSeconds(-1)).ToList();
            Check(exported.Count==3 && exported[1].Contains("\"Browser, \"\"work\"\"\",12.5,512.25,,2,3") && exported[2].Contains("\"'=unsafe\""),"Activity CSV culture, escaping, unknown I/O");
            Check(logs.Lines(now.AddSeconds(1)).Count()==1,"Activity export date filter");
            File.AppendAllText(Directory.GetFiles(logs.Folder)[0],"torn"); logs.Append(now,consumers,2,3); Check(logs.Lines(now.AddSeconds(-1)).Count()==5,"Activity log interrupted append recovery");
            var events=new PowerEventLog(Path.Combine(folder,"events"));
            var first=new Sample { Time=now,Source="Battery",State="Discharging",Percent=50,Watts=-10,Volts=15 };
            events.Observe(new[]{first}); events.Observe(new[]{first}); Check(events.Recent(now.AddSeconds(-1)).Count==1,"No duplicated unchanged power events");
            events.Observe(new[]{new Sample { Time=now.AddSeconds(10),Source="External power",State="Charging",Percent=51,Watts=20,Volts=15.5 }});
            var eventRows=events.Recent(now.AddSeconds(-1)); Check(eventRows.Count==4 && eventRows.Any(e=>e.Title=="External power connected") && eventRows.Any(e=>e.Title=="Charging started"),"Source, charging and battery level event persistence");
            File.AppendAllText(Directory.GetFiles(events.Folder)[0],"torn"); events.Add("Recording stopped","Test"); Check(events.Recent(now.AddSeconds(-1)).Count==5,"Event log interrupted append recovery");
            var trends=new ActivityTrends(); trends.Observe(now.AddMinutes(-6),consumers); trends.Observe(now,consumers); Check(trends.Get(consumers[0].Name).Count==1,"Bounded five-minute resource histories");
        }
    }
}
