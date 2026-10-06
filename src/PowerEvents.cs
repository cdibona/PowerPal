using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace PowerPal {
    internal sealed class PowerEvent {
        public DateTime Time;
        public string Title, Detail;
    }
    internal sealed class PowerEventLog {
        readonly string folder;
        Dictionary<string,Sample> previous=new Dictionary<string,Sample>();
        public string Folder { get { return folder; } }
        public PowerEventLog(string path) { folder=path; Directory.CreateDirectory(path); }
        public static string Details(Sample s) {
            return s.Device+" / "+s.Source+" / "+s.State+" / "+(s.Percent.HasValue?s.Percent.Value.ToString("0")+"%":"charge unavailable")+" / "+(s.Volts.HasValue?s.Volts.Value.ToString("0.00")+" V":"voltage unavailable")+" / "+(s.Watts.HasValue?s.Watts.Value.ToString("0.0")+" W":"watts unavailable");
        }
        public void Observe(IList<Sample> samples) {
            var events=new List<PowerEvent>();
            foreach(var s in samples) {
                Sample last;
                if(!previous.TryGetValue(s.Device,out last)) events.Add(new PowerEvent { Time=s.Time,Title=previous.Count==0?"Recording started":"Battery now reported",Detail=Details(s) });
                else {
                    if(last.Source!=s.Source) events.Add(new PowerEvent { Time=s.Time,Title=s.Source=="External power"?"External power connected":s.Source=="Battery"?"Running on battery":"Power source changed",Detail=Details(s) });
                    if(last.State!=s.State) events.Add(new PowerEvent { Time=s.Time,Title=s.State=="Charging"?"Charging started":s.State=="Discharging"?"Discharging started":s.State=="Idle"?"Battery is idle":s.State,Detail=Details(s) });
                    if(s.Percent.HasValue && (!last.Percent.HasValue || Math.Floor(s.Percent.Value)!=Math.Floor(last.Percent.Value))) events.Add(new PowerEvent { Time=s.Time,Title=s.Percent.Value>=100?"Battery reached 100%":"Battery level "+s.Percent.Value.ToString("0")+"%",Detail=Details(s) });
                }
            }
            foreach(var lost in previous.Keys.Except(samples.Select(s=>s.Device))) events.Add(new PowerEvent { Time=DateTime.UtcNow,Title="Battery no longer reported",Detail=lost });
            Append(events); previous=samples.GroupBy(s=>s.Device).ToDictionary(g=>g.Key,g=>g.Last());
        }
        public void Add(string title,string detail) { Append(new[]{new PowerEvent { Time=DateTime.UtcNow,Title=title,Detail=detail }}); }
        void Append(IEnumerable<PowerEvent> events) {
            var serializer=new JavaScriptSerializer();
            foreach(var group in events.GroupBy(e=>e.Time.ToUniversalTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))) {
                string path=Path.Combine(folder,group.Key+".jsonl");
                using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)) {
                    bool newline=false; if(stream.Length>0) { stream.Seek(-1,SeekOrigin.End); newline=stream.ReadByte()!=10; } stream.Seek(0,SeekOrigin.End);
                    using(var writer=new StreamWriter(stream)) { if(newline) writer.WriteLine(); foreach(var e in group) writer.WriteLine(serializer.Serialize(e)); }
                }
            }
        }
        public List<PowerEvent> Recent(DateTime since,int limit=200) {
            var events=new List<PowerEvent>(); var serializer=new JavaScriptSerializer();
            foreach(string path in Directory.GetFiles(folder,"????-??-??.jsonl").OrderByDescending(p=>p)) {
                DateTime day; if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out day) || day<since.Date) continue;
                using(var reader=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))) {
                    string line; while((line=reader.ReadLine())!=null) {
                        try { var e=serializer.Deserialize<PowerEvent>(line); if(e!=null && e.Time.ToUniversalTime()>=since) events.Add(e); } catch(ArgumentException) { } catch(InvalidOperationException) { }
                    }
                }
                if(events.Count>=limit) break;
            }
            return events.OrderByDescending(e=>e.Time).Take(limit).ToList();
        }
    }
    internal sealed class ActivityPoint { public DateTime Time; public double Cpu,Memory; public double? Io,Gpu,Watts,Share; }
    internal sealed class ActivityTrends {
        readonly Dictionary<string,List<ActivityPoint>> tracks=new Dictionary<string,List<ActivityPoint>>(StringComparer.OrdinalIgnoreCase);
        public void Observe(DateTime time,IEnumerable<Consumer> consumers) {
            foreach(var c in consumers) {
                List<ActivityPoint> points; if(!tracks.TryGetValue(c.Name,out points)) { points=new List<ActivityPoint>(); tracks[c.Name]=points; }
                points.Add(new ActivityPoint { Time=time,Cpu=c.Cpu,Memory=c.MemoryMb,Io=c.DiskMb,Gpu=c.Gpu,Watts=c.GpuWatts,Share=c.PowerShare });
            }
            foreach(var key in tracks.Keys.ToList()) { tracks[key].RemoveAll(p=>p.Time<time.AddMinutes(-5)); if(tracks[key].Count==0) tracks.Remove(key); }
        }
        public List<ActivityPoint> Get(string name) { List<ActivityPoint> result; return name!=null && tracks.TryGetValue(name,out result)?result:new List<ActivityPoint>(); }
    }
}
