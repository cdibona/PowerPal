using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerPal {
    internal sealed class Consumer {
        public string Name;
        public double Cpu, MemoryMb;
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
        internal static double CpuPercent(double deltaMs, double elapsed, int processors) { return elapsed<=0 || deltaMs<0 ? 0 : Math.Min(100,deltaMs/(elapsed*1000*Math.Max(1,processors))*100); }
        public List<Consumer> Read() {
            double now=clock.Elapsed.TotalSeconds, elapsed=now-last; last=now;
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
            return rows.GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).Select(g=>new Consumer { Name=g.Key,Cpu=g.Sum(r=>r.Cpu),MemoryMb=g.Sum(r=>r.MemoryMb),DiskMb=g.All(r=>r.DiskMb.HasValue) ? (double?)g.Sum(r=>r.DiskMb.Value) : null }).OrderByDescending(r=>r.Cpu).ThenByDescending(r=>r.MemoryMb).Take(6).ToList();
        }
    }
}
