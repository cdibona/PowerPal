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
}
