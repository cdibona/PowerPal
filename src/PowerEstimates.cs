using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerPal {
    internal sealed class PowerFrame {
        public DateTime Time;
        public string Source="Unknown";
        public double? BatteryWatts,DrawWatts;
        public static PowerFrame From(IList<Sample> samples) {
            if(samples==null || samples.Count==0) return new PowerFrame();
            var frame=new PowerFrame { Time=samples.Min(s=>s.Time),Source=string.Join(" / ",samples.Select(s=>s.Source).Distinct()) };
            if(samples.All(s=>s.Watts.HasValue)) frame.BatteryWatts=samples.Sum(s=>s.Watts.Value);
            if(frame.Source=="Battery" && samples.All(s=>s.Watts.HasValue && s.Watts<=0)) frame.DrawWatts=-frame.BatteryWatts;
            return frame;
        }
        public bool Fresh(DateTime now) { return Time<=now && (now-Time).TotalSeconds<=15; }
    }
    internal static class PowerEstimates {
        public const string Model="cpu-plus-busiest-gpu-v1";
        public static void Apply(IList<Consumer> apps,PowerFrame power,DateTime now) {
            double total=apps.Sum(c=>Math.Max(0,c.Cpu)+Math.Max(0,c.Gpu??0));
            bool usable=apps.Count>0 && apps.All(c=>c.Gpu.HasValue) && total>=0.1;
            foreach(var app in apps) {
                // This is a transparent allocation heuristic, not a calibrated meter.
                // Equal CPU/GPU utilization weights distribute *all* battery draw,
                // including shared system overhead, among readable app groups.
                app.PowerShare=usable?(double?)(100*(Math.Max(0,app.Cpu)+Math.Max(0,app.Gpu.Value))/total):null;
                app.EstimatedWatts=power!=null && power.Fresh(now) && power.DrawWatts.HasValue && app.PowerShare.HasValue ? power.DrawWatts*app.PowerShare/100 : null;
            }
        }
    }
    internal sealed class SessionCapture {
        public const string Header="utc,process,present,cpu_percent,gpu_percent,estimated_power_share_percent,estimated_app_watts,battery_watts,battery_sample_utc,power_source,model";
        public readonly string Folder;
        public string App { get; private set; }
        public string PathName { get; private set; }
        public bool Active { get { return App!=null; } }
        public SessionCapture(string folder) { Folder=folder; }
        public void Start(string app) {
            if(Active || string.IsNullOrWhiteSpace(app)) throw new InvalidOperationException("Select an app before starting a capture.");
            Directory.CreateDirectory(Folder);
            string path=Path.Combine(Folder,DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".csv");
            File.WriteAllText(path,Header+Environment.NewLine); PathName=path; App=app;
        }
        public void Append(DateTime now,IList<Consumer> apps,PowerFrame power) {
            if(!Active) return;
            var app=apps.FirstOrDefault(c=>string.Equals(c.Name,App,StringComparison.OrdinalIgnoreCase)); bool fresh=power!=null && power.Fresh(now);
            File.AppendAllText(PathName,string.Join(",",new[]{now.ToString("o"),ActivityHistory.Quote(App),app==null?"false":"true",Sample.Number(app==null?null:(double?)app.Cpu),Sample.Number(app==null?null:app.Gpu),Sample.Number(app==null?null:app.PowerShare),Sample.Number(app==null?null:app.EstimatedWatts),Sample.Number(fresh?power.BatteryWatts:null),fresh?power.Time.ToString("o"):"",ActivityHistory.Quote(fresh?power.Source:"Unknown"),PowerEstimates.Model})+Environment.NewLine);
        }
        public string Stop() { string path=PathName; App=null; return path; }
    }
}
