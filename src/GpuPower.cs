using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerPal {
    internal sealed class GpuPowerReading {
        public string Id,Name,Method;
        public double? Watts,EnergyWh;
        public double Seconds,UnassignedWatts,ObservedWh;
    }
    internal sealed class GpuPowerReport {
        public DateTime Time;
        public bool NewSample;
        public string Status="GPU sensors waiting for activity";
        public readonly List<GpuPowerReading> Devices=new List<GpuPowerReading>();
        public readonly Dictionary<string,double> AppWatts=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        public bool Fresh(DateTime now) { return Time<=now && (now-Time).TotalSeconds<=12 && Devices.Any(d=>d.Watts.HasValue); }
        public string Summary(DateTime now) {
            if(!Fresh(now)) return Status;
            return "NVIDIA GPU: "+Devices.Sum(d=>d.Watts??0).ToString("0.0")+" W measured / "+Devices.Sum(d=>d.ObservedWh).ToString("0.000")+" Wh observed";
        }
        public string Detail(DateTime now) {
            if(!Fresh(now)) return "GPU app watts need a supported, active NVIDIA GPU. CPU watts unavailable.";
            return Devices.Count+" GPU(s) / "+Devices.Sum(d=>d.UnassignedWatts).ToString("0.0")+" W unassigned / app GPU watts estimated / CPU watts unavailable";
        }
    }
    internal sealed class GpuEnergyCounter {
        ulong? energy; double? watts; double previousTime;
        bool primed;
        public double ObservedWh;
        public void Reset() { energy=null; watts=null; primed=false; }
        public GpuPowerReading Read(RawGpuPower raw,double seconds) {
            var result=new GpuPowerReading { Id=raw.Id,Name=raw.Name,Watts=raw.Watts,Method="nvml-power",ObservedWh=ObservedWh };
            double elapsed=seconds-previousTime;
            if(primed && elapsed>=1 && elapsed<=20) {
                result.Seconds=elapsed;
                if(energy.HasValue && raw.Millijoules.HasValue) {
                    // Reset/wrap or implausible energy leaves a gap, never a huge fabricated spike.
                    if(raw.Millijoules.Value>=energy.Value) {
                        double joules=(raw.Millijoules.Value-energy.Value)/1000.0;
                        if(joules/elapsed<=2000) { result.Watts=joules/elapsed; result.EnergyWh=joules/3600; result.Method="nvml-energy"; }
                    }
                } else if(watts.HasValue && raw.Watts.HasValue) {
                    result.EnergyWh=(watts.Value+raw.Watts.Value)*.5*elapsed/3600;
                    result.Method="nvml-power-integrated";
                }
            }
            if(result.EnergyWh.HasValue) ObservedWh+=result.EnergyWh.Value;
            result.ObservedWh=ObservedWh;
            energy=raw.Millijoules; watts=raw.Watts; previousTime=seconds; primed=true;
            return result;
        }
    }
    internal sealed class GpuAttributionWindow {
        public double Seconds;
        public bool Valid=true;
        readonly Dictionary<string,double> totals=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Dictionary<string,double>> apps=new Dictionary<string,Dictionary<string,double>>(StringComparer.OrdinalIgnoreCase);
        internal static string AdapterId(string engine) { int end=engine.IndexOf("_phys_",StringComparison.OrdinalIgnoreCase); return end<0?null:engine.Substring(0,end); }
        static void Add(Dictionary<string,double> target,Dictionary<string,double> engines,string id,double seconds) {
            if(engines==null) return;
            foreach(var e in engines.Where(e=>string.Equals(AdapterId(e.Key),id,StringComparison.OrdinalIgnoreCase))) {
                double old; target.TryGetValue(e.Key,out old); target[e.Key]=old+Math.Max(0,Math.Min(100,e.Value))*seconds;
            }
        }
        public void Observe(string id,GpuSnapshot snapshot,IList<Consumer> consumers,double seconds) {
            if(seconds<=0 || seconds>20 || !snapshot.Available || snapshot.InvalidAdapters.Contains(id)) { Valid=false; return; }
            Seconds+=seconds;
            // Include protected/unreadable processes in the denominator using WDDM counters.
            foreach(var engines in snapshot.Engines.Values) Add(totals,engines,id,seconds);
            foreach(var app in consumers) {
                if(app.Engines==null) continue;
                Dictionary<string,double> weights; if(!apps.TryGetValue(app.Name,out weights)) { weights=new Dictionary<string,double>(); apps[app.Name]=weights; }
                Add(weights,app.Engines,id,seconds);
            }
        }
        public Dictionary<string,double> Fractions() {
            var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
            if(!Valid || Seconds<=0 || totals.Count==0) return result;
            double sum=totals.Values.Sum(); if(sum<=0) return result;
            // Activity-weighted board-power allocation, not a measured idle/dynamic split.
            double busy=Math.Min(1,totals.Values.Max()/Seconds/100);
            foreach(var app in apps) { double fraction=app.Value.Values.Sum()/sum*busy; if(fraction>0) result[app.Key]=fraction; }
            double assigned=result.Values.Sum(); if(assigned>busy) foreach(string key in result.Keys.ToList()) result[key]*=busy/assigned;
            return result;
        }
    }
    internal sealed class GpuPowerMonitor : IDisposable {
        readonly object gate=new object();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Func<IGpuPowerSource> factory;
        IGpuPowerSource source;
        readonly Dictionary<string,GpuAttributionWindow> windows=new Dictionary<string,GpuAttributionWindow>();
        readonly Dictionary<string,GpuEnergyCounter> counters=new Dictionary<string,GpuEnergyCounter>();
        readonly Dictionary<string,double> appEnergy=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        GpuPowerReport report=new GpuPowerReport();
        double nextRead,retry,rediscover;
        bool disposed;
        public GpuPowerMonitor() : this(()=>new NvidiaSensors()) { }
        internal GpuPowerMonitor(Func<IGpuPowerSource> create) { factory=create; }
        public GpuPowerReport Read(IList<Consumer> apps,GpuSnapshot snapshot,double interval,bool enabled) { return Read(apps,snapshot,interval,enabled,DateTime.UtcNow,clock.Elapsed.TotalSeconds); }
        internal GpuPowerReport Read(IList<Consumer> apps,GpuSnapshot snapshot,double interval,bool enabled,DateTime now,double seconds) { lock(gate) {
            report.NewSample=false;
            if(disposed) return report;
            if(!enabled) { Pause("GPU sensors disabled in Settings"); Apply(apps,now); return report; }
            if(seconds<retry) { Apply(apps,now); return report; }
            try {
                if(source==null) { source=factory(); rediscover=seconds+60; }
                if(source.Adapters.Count==0) { Pause("No supported NVIDIA GPU sensor"); source.Dispose(); source=null; retry=seconds+60; Apply(apps,now); return report; }
                var active=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var adapter in source.Adapters) {
                    double busy=snapshot.Available?snapshot.Engines.Values.Sum(e=>e.Where(p=>string.Equals(GpuAttributionWindow.AdapterId(p.Key),adapter.Id,StringComparison.OrdinalIgnoreCase)).Sum(p=>p.Value)):0;
                    if(busy>=.1) active.Add(adapter.Id);
                }
                if(active.Count==0) { Pause(snapshot.Available?"GPU sensors paused while NVIDIA GPU is idle":"GPU sensors waiting for Windows GPU counters"); if(seconds>=rediscover) { source.Dispose(); source=null; } Apply(apps,now); return report; }
                foreach(var adapter in source.Adapters) {
                    GpuAttributionWindow window; if(!windows.TryGetValue(adapter.Id,out window)) { window=new GpuAttributionWindow(); windows[adapter.Id]=window; }
                    window.Observe(adapter.Id,snapshot,apps,interval);
                }
                // Coalesce normal timer jitter; never sample faster than about five seconds.
                if(seconds+.1>=nextRead) {
                    nextRead=seconds+5;
                    var raw=source.Read(active);
                    var next=new GpuPowerReport { Time=now,NewSample=true,Status="GPU sensor reading unavailable" };
                    foreach(var value in raw) {
                        GpuEnergyCounter counter; if(!counters.TryGetValue(value.Id,out counter)) { counter=new GpuEnergyCounter(); counters[value.Id]=counter; }
                        var reading=counter.Read(value,seconds); next.Devices.Add(reading);
                        GpuAttributionWindow window;
                        if(reading.Watts.HasValue && windows.TryGetValue(value.Id,out window) && reading.Seconds>0 && Math.Abs(window.Seconds-reading.Seconds)<.5) {
                            var fractions=window.Fractions();
                            foreach(var app in fractions) {
                                double old; next.AppWatts.TryGetValue(app.Key,out old); next.AppWatts[app.Key]=old+reading.Watts.Value*app.Value;
                                if(reading.EnergyWh.HasValue) {
                                    appEnergy.TryGetValue(app.Key,out old);
                                    // Bound memory even on systems launching thousands of unique executables.
                                    if(appEnergy.Count<4096 || appEnergy.ContainsKey(app.Key)) appEnergy[app.Key]=old+reading.EnergyWh.Value*app.Value;
                                }
                            }
                            reading.UnassignedWatts=reading.Watts.Value*(1-fractions.Values.Sum());
                        } else reading.UnassignedWatts=reading.Watts??0;
                    }
                    foreach(var key in counters.Keys.Except(raw.Select(r=>r.Id)).ToList()) counters[key].Reset();
                    report=next; windows.Clear();
                }
            } catch(Exception ex) {
                Pause("GPU sensor unavailable: "+ex.Message); retry=seconds+60;
                if(source!=null) { source.Dispose(); source=null; }
            }
            Apply(apps,now); return report;
        } }
        void Apply(IList<Consumer> apps,DateTime now) {
            foreach(var app in apps) {
                double value; app.GpuWatts=null; app.GpuPowerTime=null; app.GpuEnergyWh=null;
                if(report.Fresh(now) && report.AppWatts.TryGetValue(app.Name,out value)) { app.GpuWatts=value; app.GpuPowerTime=report.Time; }
                if(appEnergy.TryGetValue(app.Name,out value)) app.GpuEnergyWh=value;
            }
        }
        void Pause(string status) {
            if(source!=null) source.Pause(); windows.Clear(); foreach(var counter in counters.Values) counter.Reset();
            report=new GpuPowerReport { Status=status };
        }
        public void Dispose() { lock(gate) { disposed=true; if(source!=null) source.Dispose(); } }
    }
    internal sealed class SensorHistory {
        public const string Header="utc,adapter,name,watts,interval_seconds,interval_wh,observed_wh_this_run,unassigned_watts,source,app_model";
        public readonly string Folder;
        public SensorHistory(string folder) { Folder=folder; Directory.CreateDirectory(folder); }
        public void Append(GpuPowerReport report) {
            if(!report.NewSample || report.Devices.Count==0) return;
            string path=Path.Combine(Folder,report.Time.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+".csv");
            using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)) {
                bool newline=false; if(stream.Length>0) { stream.Seek(-1,SeekOrigin.End); newline=stream.ReadByte()!=10; } stream.Seek(0,SeekOrigin.End);
                using(var writer=new StreamWriter(stream)) {
                    if(stream.Length==0) writer.WriteLine(Header); else if(newline) writer.WriteLine();
                    foreach(var d in report.Devices) writer.WriteLine(string.Join(",",new[]{report.Time.ToString("o"),ActivityHistory.Quote(d.Id),ActivityHistory.Quote(d.Name),Sample.Number(d.Watts),Sample.Number(d.Seconds),d.EnergyWh.HasValue?d.EnergyWh.Value.ToString("G17",CultureInfo.InvariantCulture):"",d.ObservedWh.ToString("G17",CultureInfo.InvariantCulture),Sample.Number(d.UnassignedWatts),d.Method,PowerEstimates.GpuModel}));
                }
            }
        }
        public IEnumerable<string> Lines(DateTime since) {
            yield return Header;
            foreach(string path in Directory.GetFiles(Folder,"????-??-??.csv").OrderBy(p=>p)) {
                DateTime day; if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out day) || day<since.Date) continue;
                using(var reader=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))) {
                    if(reader.ReadLine()!=Header) continue; string line;
                    while((line=reader.ReadLine())!=null) { int end=line.IndexOf(','); DateTime time; if(end>0 && DateTime.TryParseExact(line.Substring(0,end),"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time) && time.ToUniversalTime()>=since && line.Count(c=>c==',')>=9) yield return line; }
                }
            }
        }
    }
}
