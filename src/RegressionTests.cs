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
            Check(Theme.Resolve("Auto",true) && !Theme.Resolve("Auto",false) && Theme.Resolve("Light",false) && !Theme.Resolve("Dark",true),"Windows theme and explicit overrides");
            var gpu=new GpuSnapshot { Available=true };
            GpuMonitor.Add(gpu,"pid_7_luid_0x1_phys_0_eng_0_engtype_3D",35,0);
            GpuMonitor.Add(gpu,"pid_7_luid_0x1_phys_0_eng_1_engtype_Copy",60,0);
            GpuMonitor.Add(gpu,"pid_8_luid_0x1_phys_0_eng_0_engtype_3D",40,1);
            Check(GpuMonitor.Busiest(new[]{gpu.ForProcess(7),gpu.ForProcess(8)})==75,"Aggregate same engine across app processes, then choose busiest engine");
            GpuMonitor.Add(gpu,"pid_9_luid_0x1_phys_0_eng_0_engtype_3D",99,0xC0000BC6);
            Check(gpu.ForProcess(9)==null && GpuMonitor.Busiest(new[]{gpu.ForProcess(7),gpu.ForProcess(9)})==null,"Unavailable GPU is not zero");
            var estimateApps=new List<Consumer> { new Consumer { Name="Game",Cpu=10,Gpu=80 },new Consumer { Name="Browser",Cpu=10,Gpu=0 } };
            DateTime estimateTime=DateTime.UtcNow;
            var frame=PowerFrame.From(new[]{new Sample { Time=estimateTime,Source="Battery",Watts=-50 }});
            PowerEstimates.Apply(estimateApps,frame,estimateTime);
            Check(estimateApps[0].PowerShare==90 && estimateApps[0].EstimatedWatts==45 && estimateApps.Sum(c=>c.EstimatedWatts)==50,"CPU/GPU allocation conserves measured discharge");
            PowerEstimates.Apply(estimateApps,frame,estimateTime.AddSeconds(16)); Check(estimateApps[0].EstimatedWatts==null && estimateApps[0].PowerShare==90,"Stale battery watts suppressed; activity share stays available");
            var ac=PowerFrame.From(new[]{new Sample { Time=estimateTime,Source="External power",Watts=40 }});
            PowerEstimates.Apply(estimateApps,ac,estimateTime); Check(estimateApps.All(c=>c.EstimatedWatts==null),"Charging is not system power consumption");
            Check(PowerFrame.From(new[]{new Sample { Source="Battery",Watts=-10 },new Sample { Source="Battery",Watts=null }}).DrawWatts==null,"Missing battery must not produce partial system watts");
            estimateApps[0].Gpu=null; PowerEstimates.Apply(estimateApps,frame,estimateTime); Check(estimateApps.All(c=>c.PowerShare==null),"Do not fabricate estimates without complete GPU readings");
            estimateApps[0].Gpu=80; PowerEstimates.Apply(estimateApps,frame,estimateTime);
            var ranked=ActivityMonitor.Rank(new[]{new Consumer { Name="CPU worker",Cpu=40,Gpu=0 },new Consumer { Name="GPU game",Cpu=5,Gpu=90 },new Consumer { Name="Missing GPU",Cpu=20,Gpu=null }});
            Check(ranked[0].Name=="GPU game" && ranked[1].Name=="CPU worker","Automatic logging includes GPU-heavy apps and ranks partial readings by known activity");
            var prefs=new Preferences { StorageFolder=Path.Combine(folder,"preferences"),DisplayScalePercent=125,TopAppCount=7 };
            prefs.Save(); var reloaded=Preferences.LoadFrom(prefs.StorageFolder);
            Check(reloaded.DisplayScalePercent==125 && reloaded.AppLimit==7 && reloaded.AutoUpdate,"Scaling and top-app count survive restart");
            Check(DisplayScaling.ResolveDpi(192,125)==120 && DisplayScaling.ResolveDpi(120,0)==120 && DisplayScaling.ResolveDpi(192,0)==192,"Manual scaling replaces Windows scale; follow mode uses monitor DPI");
            Check(DisplayScaling.Normalize(-1)==0 && DisplayScaling.Normalize(999)==0 && new Preferences { TopAppCount=999 }.AppLimit==100,"Invalid preferences stay bounded");
            File.WriteAllText(Path.Combine(prefs.StorageFolder,"settings.json"),"{\"AutoUpdate\":false,\"ThemeMode\":\"Dark\"}");
            reloaded=Preferences.LoadFrom(prefs.StorageFolder); Check(!reloaded.AutoUpdate && reloaded.ThemeMode=="Dark" && reloaded.DisplayScalePercent==0 && reloaded.AppLimit==20,"Old preferences retain defaults for new controls");
            File.WriteAllLines(Path.Combine(logs.Folder,now.ToString("yyyy-MM-dd")+".csv"),new[]{ActivityHistory.LegacyHeader,now.ToString("o")+",1,Legacy,1,2,3,2,0"});
            Check(logs.Lines(now.AddSeconds(-1)).Any(l=>l.Contains(",Legacy,") && l.EndsWith(",,,,,,,")),"Legacy activity export gains empty new columns without rewriting history");
            var trends=new ActivityTrends(); trends.Observe(now.AddMinutes(-6),consumers); trends.Observe(now,consumers); Check(trends.Get(consumers[0].Name).Count==1,"Bounded five-minute resource histories");
        }
    }
}
