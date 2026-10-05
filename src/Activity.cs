using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;

namespace PowerPal {
    internal sealed class Consumer {
        public string Name;
        public double Cpu, MemoryMb;
        public int Processes=1;
        public double? DiskMb;
    }
    internal sealed class ActivityMonitor {
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessIoCounters(IntPtr h, out IoCounters counters);
        sealed class Reading { public double Cpu; public ulong? Bytes; public DateTime Started; }
        Dictionary<int,Reading> previous = new Dictionary<int,Reading>();
        readonly Stopwatch clock=Stopwatch.StartNew();
        double last;
        public int Inaccessible { get; private set; }
        public double IntervalSeconds { get; private set; }
        internal static double CpuPercent(double deltaMs, double elapsed, int processors) { return elapsed<=0 || deltaMs<0 ? 0 : Math.Min(100,deltaMs/(elapsed*1000*Math.Max(1,processors))*100); }
        public List<Consumer> Read() {
            double now=clock.Elapsed.TotalSeconds, elapsed=now-last; last=now; IntervalSeconds=elapsed;
            var next=new Dictionary<int,Reading>(); var rows=new List<Consumer>(); Inaccessible=0;
            foreach(var process in Process.GetProcesses()) using(process) {
                try {
                    if(process.Id==0) continue;
                    var r=new Reading { Cpu=process.TotalProcessorTime.TotalMilliseconds,Started=process.StartTime.ToUniversalTime() };
                    IoCounters io; if(GetProcessIoCounters(process.Handle,out io)) r.Bytes=io.ReadBytes+io.WriteBytes;
                    next[process.Id]=r; Reading old;
                    bool matched=previous.TryGetValue(process.Id,out old) && old.Started==r.Started;
                    if(matched) rows.Add(new Consumer { Name=process.ProcessName,Cpu=CpuPercent(r.Cpu-old.Cpu,elapsed,Environment.ProcessorCount),MemoryMb=process.WorkingSet64/1048576.0,
                        DiskMb=r.Bytes.HasValue && old.Bytes.HasValue && r.Bytes>=old.Bytes && elapsed>0 ? (double?)((r.Bytes.Value-old.Bytes.Value)/1048576.0/elapsed) : null });
                } catch(System.ComponentModel.Win32Exception) { Inaccessible++; } catch(InvalidOperationException) { Inaccessible++; } catch(NotSupportedException) { Inaccessible++; }
            }
            previous=next;
            return rows.GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).Select(g=>new Consumer { Name=g.Key,Processes=g.Count(),Cpu=g.Sum(r=>r.Cpu),MemoryMb=g.Sum(r=>r.MemoryMb),DiskMb=g.All(r=>r.DiskMb.HasValue) ? (double?)g.Sum(r=>r.DiskMb.Value) : null }).OrderByDescending(r=>r.Cpu).ThenByDescending(r=>r.DiskMb).ThenByDescending(r=>r.MemoryMb).ToList();
        }
    }
    internal sealed class ActivityHistory {
        public const string Header="utc,rank,process,cpu_percent,memory_mb,io_mb_per_second,sample_window_seconds,inaccessible_processes";
        public readonly string Folder;
        public ActivityHistory(string folder) { Folder=folder; Directory.CreateDirectory(folder); }
        internal static string Quote(string value) {
            value=(value??"").Replace('\r',' ').Replace('\n',' ');
            // Quoting alone does not prevent spreadsheet formula interpretation.
            if(value.Length>0 && "=+-@".IndexOf(value[0])>=0) value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        }
        public bool Append(DateTime time,IList<Consumer> consumers,double interval,int inaccessible) {
            if(consumers.Count==0 || interval<=0) return false;
            time=time.ToUniversalTime();
            string path=Path.Combine(Folder,time.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+".csv");
            using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)) {
                bool newline=false; if(stream.Length>0) { stream.Seek(-1,SeekOrigin.End); newline=stream.ReadByte()!=10; }
                stream.Seek(0,SeekOrigin.End);
                using(var writer=new StreamWriter(stream)) {
                    if(stream.Length==0) writer.WriteLine(Header); else if(newline) writer.WriteLine();
                    for(int i=0;i<consumers.Count;i++) {
                        var c=consumers[i];
                        writer.WriteLine(string.Join(",",new[]{time.ToString("o"), (i+1).ToString(CultureInfo.InvariantCulture), Quote(c.Name),Sample.Number(c.Cpu),Sample.Number(c.MemoryMb),Sample.Number(c.DiskMb),Sample.Number(interval),inaccessible.ToString(CultureInfo.InvariantCulture)}));
                    }
                }
            }
            return true;
        }
        public IEnumerable<string> Lines(DateTime since) {
            yield return Header;
            foreach(string path in Directory.GetFiles(Folder,"????-??-??.csv").OrderBy(p=>p)) {
                DateTime day; if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out day) || day<since.Date) continue;
                using(var reader=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))) {
                    reader.ReadLine(); string line;
                    while((line=reader.ReadLine())!=null) {
                        int end=line.IndexOf(','); DateTime time;
                        if(end>0 && DateTime.TryParseExact(line.Substring(0,end),"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time) && time.ToUniversalTime()>=since && line.Count(c=>c==',')>=7) yield return line;
                    }
                }
            }
        }
    }
}
