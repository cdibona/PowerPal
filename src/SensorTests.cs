using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PowerPal {
    internal static class SensorTests {
        const string Nvidia="luid_0x00000000_0x00000001",Intel="luid_0x00000000_0x00000002";
        static string Engine(string id,int number=0) { return id+"_phys_0_eng_"+number+"_engtype_3D"; }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static bool Near(double? value,double expected) { return value.HasValue && Math.Abs(value.Value-expected)<.000001; }
        sealed class FakeSource : IGpuPowerSource {
            public int Reads,Pauses; public bool Fail;
            public IList<GraphicsAdapter> Adapters { get; private set; }
            public FakeSource() { Adapters=new List<GraphicsAdapter> { new GraphicsAdapter { Id=Nvidia,Name="Fixture GPU",Vendor=0x10de } }; }
            public List<RawGpuPower> Read(ISet<string> active) { Reads++; if(Fail) throw new InvalidOperationException("driver unavailable"); return new List<RawGpuPower> { new RawGpuPower { Id=Nvidia,Name="Fixture GPU",Millijoules=(ulong)(Reads*500000),Watts=100 } }; }
            public void Pause() { Pauses++; }
            public void Dispose() { }
        }
        static GpuSnapshot Snapshot(double game=40,double background=20) {
            var s=new GpuSnapshot { Available=true };
            s.Engines[1]=new Dictionary<string,double> { {Engine(Nvidia),game} };
            s.Engines[2]=new Dictionary<string,double> { {Engine(Nvidia),background} };
            s.Engines[3]=new Dictionary<string,double> { {Engine(Intel),95} };
            return s;
        }
        static List<Consumer> Apps(GpuSnapshot s) { return new List<Consumer> { new Consumer { Name="Game",Engines=s.Engines[1],Gpu=40 },new Consumer { Name="Intel app",Engines=s.Engines[3],Gpu=95 } }; }
        public static void Run(string folder) {
            var counter=new GpuEnergyCounter();
            var raw=new RawGpuPower { Id=Nvidia,Watts=99,Millijoules=1000000 };
            var first=counter.Read(raw,0); Check(first.EnergyWh==null && first.Seconds==0,"No energy invented before first interval");
            raw.Millijoules=1500000; var next=counter.Read(raw,5);
            Check(Near(next.Watts,100) && Near(next.EnergyWh,500.0/3600) && next.Method=="nvml-energy","Energy delta gives average watts/Wh");
            raw.Millijoules=1; Check(counter.Read(raw,10).EnergyWh==null,"Energy reset leaves a gap");
            raw.Millijoules=100000000; Check(counter.Read(raw,15).EnergyWh==null,"Implausible sensor spike rejected");
            raw.Millijoules=100500000; Check(counter.Read(raw,100).EnergyWh==null,"Sleep gap excluded");
            counter.Reset(); raw.Millijoules=null; raw.Watts=20; counter.Read(raw,0); raw.Watts=40;
            next=counter.Read(raw,5); Check(Near(next.EnergyWh,150.0/3600) && next.Method=="nvml-power-integrated","Power fallback integration explicitly labeled");
            var snapshot=Snapshot(); var apps=Apps(snapshot); var window=new GpuAttributionWindow(); window.Observe(Nvidia,snapshot,apps,5);
            var fractions=window.Fractions();
            Check(Near(fractions["Game"],.4) && !fractions.ContainsKey("Intel app"),"GPU-specific attribution excludes Intel; protected process remains unassigned");
            snapshot.Invalid.Add(3); snapshot.InvalidAdapters.Add(Intel); window=new GpuAttributionWindow(); window.Observe(Nvidia,snapshot,apps,5);
            Check(window.Fractions().ContainsKey("Game"),"Invalid counters on another GPU do not suppress NVIDIA attribution");
            snapshot.Invalid.Add(2); snapshot.InvalidAdapters.Add(Nvidia); window=new GpuAttributionWindow(); window.Observe(Nvidia,snapshot,apps,5); Check(window.Fractions().Count==0,"Invalid GPU denominator suppresses app attribution");
            var matching=new GraphicsAdapter { Id=Nvidia,Vendor=0x10de,Device=0x1234,Subsystem=7 };
            Check(GraphicsAdapters.Match(new[]{matching},0x123410de,7)==matching,"PCI IDs match the WDDM adapter");
            Check(GraphicsAdapters.Match(new[]{matching,matching},0x123410de,7)==null,"Identical adapters never guessed");
            var fake=new FakeSource(); DateTime now=DateTime.UtcNow; snapshot=Snapshot(); apps=Apps(snapshot);
            using(var monitor=new GpuPowerMonitor(()=>fake)) {
                var report=monitor.Read(apps,snapshot,5,true,now,0); Check(fake.Reads==1 && apps[0].GpuWatts==null,"First sensor sample has no fabricated app allocation");
                monitor.Read(apps,snapshot,2,true,now.AddSeconds(2),2); Check(fake.Reads==1,"Sensor reads throttled independently of UI");
                report=monitor.Read(apps,snapshot,3,true,now.AddSeconds(5),5);
                Check(fake.Reads==2 && Near(apps[0].GpuWatts,40) && apps[1].GpuWatts==null,"Matched GPU watts available independently of battery/AC");
                Check(Near(apps[0].GpuEnergyWh,200.0/3600) && Near(report.Devices[0].UnassignedWatts,60),"Conserved GPU energy; idle/unreadable share stays unassigned");
                Check(!report.Fresh(now.AddSeconds(18)),"Stale sensor readings expire");
                var history=new SensorHistory(Path.Combine(folder,"sensors")); history.Append(report);
                Check(history.Lines(now).Count()==2 && history.Lines(now).Last().Contains("nvml-energy"),"Hardware sensor history persists source and energy");
                monitor.Read(apps,snapshot,1,false,now.AddSeconds(6),6); Check(fake.Reads==2 && fake.Pauses>0 && apps[0].GpuWatts==null,"Disabling sensors stops reads and clears watts");
                var idle=new GpuSnapshot { Available=true };
                monitor.Read(apps,idle,5,true,now.AddSeconds(11),11); Check(fake.Reads==2,"Idle GPU never polled");
                monitor.Read(apps,snapshot,5,true,now.AddSeconds(16),16); Check(apps[0].GpuWatts==null,"Reactivation starts a new energy interval");
                fake.Fail=true; monitor.Read(apps,snapshot,5,true,now.AddSeconds(21),21); int failedReads=fake.Reads;
                monitor.Read(apps,snapshot,5,true,now.AddSeconds(26),26); Check(fake.Reads==failedReads && apps[0].GpuWatts==null,"Driver failure clears readings and backs off");
            }
            var jitterSource=new FakeSource();
            using(var monitor=new GpuPowerMonitor(()=>jitterSource)) {
                snapshot=Snapshot(); apps=Apps(snapshot);
                monitor.Read(apps,snapshot,5,true,now,0);
                monitor.Read(apps,snapshot,4.98,true,now.AddSeconds(4.98),4.98);
                Check(jitterSource.Reads==2,"Tiny timer jitter must not defer sensor reads by another whole tray tick");
            }
            var legacy=new ActivityHistory(Path.Combine(folder,"sensor-export"));
            File.WriteAllLines(Path.Combine(legacy.Folder,now.ToString("yyyy-MM-dd")+"-v2.csv"),new[]{ActivityHistory.V2Header,now.ToString("o")+",1,Old app,1,2,3,2,0,4,5,6,-10,"+now.ToString("o")+",Battery,cpu-plus-busiest-gpu-v1"});
            Check(legacy.Lines(now.AddSeconds(-1)).Last().EndsWith(",,,,"),"V2 exports preserve old watts and add empty sensor fields");
        }
        public static void Probe(string output) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))); var lines=new List<string>();
            using(var source=new NvidiaSensors()) {
                foreach(var adapter in source.Adapters) lines.Add("Adapter: "+adapter.Name+" / "+adapter.Id);
                var ids=new HashSet<string>(source.Adapters.Select(a=>a.Id));
                if(ids.Count>0) {
                    var counters=new Dictionary<string,GpuEnergyCounter>(); var clock=Stopwatch.StartNew();
                    for(int i=0;i<3;i++) {
                        var query=Stopwatch.StartNew(); var values=source.Read(ids); query.Stop();
                        foreach(var value in values) {
                            GpuEnergyCounter counter; if(!counters.TryGetValue(value.Id,out counter)) { counter=new GpuEnergyCounter(); counters[value.Id]=counter; }
                            var reading=counter.Read(value,clock.Elapsed.TotalSeconds);
                            lines.Add("Direct diagnostic sample: "+value.Name+" / power="+value.Watts+" W / energy counter="+value.Millijoules+" mJ / average="+reading.Watts+" W / interval Wh="+reading.EnergyWh+" / source="+reading.Method+" / query ms="+query.Elapsed.TotalMilliseconds.ToString("0.00"));
                        }
                        if(i<2) Thread.Sleep(5000);
                    }
                }
            }
            using(var activity=new ActivityMonitor()) using(var sensors=new GpuPowerMonitor()) using(var process=Process.GetCurrentProcess()) {
                activity.Read(); Thread.Sleep(2000); double cpu=process.TotalProcessorTime.TotalMilliseconds; var elapsed=Stopwatch.StartNew();
                for(int i=0;i<15;i++) {
                    var apps=activity.Read(); var report=sensors.Read(apps,activity.GpuSnapshot,activity.IntervalSeconds,true);
                    lines.Add("Counters: invalid="+activity.GpuSnapshot.Invalid.Count+" app engines="+apps.Count(a=>a.Engines!=null)+" interval="+activity.IntervalSeconds);
                    lines.Add("Automatic: "+report.Summary(DateTime.UtcNow)+" / "+report.Detail(DateTime.UtcNow));
                    foreach(var app in apps.Where(a=>a.GpuWatts.HasValue).OrderByDescending(a=>a.GpuWatts).Take(3)) lines.Add("  "+app.Name+" GPU estimate="+app.GpuWatts+" W / "+app.GpuEnergyWh+" Wh");
                    Thread.Sleep(2000);
                }
                double cpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu;
                lines.Add("Combined sampler CPU ms="+cpuMs+" / elapsed seconds="+elapsed.Elapsed.TotalSeconds.ToString("0.0")+" / machine CPU percent="+(100*cpuMs/elapsed.Elapsed.TotalMilliseconds/Environment.ProcessorCount).ToString("0.000"));
            }
            File.WriteAllLines(output,lines);
        }
    }
}
