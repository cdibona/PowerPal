using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PowerPal {
    internal sealed class GpuSnapshot {
        public bool Available;
        public readonly Dictionary<int,Dictionary<string,double>> Engines=new Dictionary<int,Dictionary<string,double>>();
        public readonly HashSet<int> Invalid=new HashSet<int>();
        public readonly HashSet<string> InvalidAdapters=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,double> ForProcess(int pid) {
            if(!Available || Invalid.Contains(pid)) return null;
            Dictionary<string,double> result;
            return Engines.TryGetValue(pid,out result)?result:new Dictionary<string,double>();
        }
    }
    internal sealed class GpuMonitor : IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct Value { public uint Status; public double Number; }
        [StructLayout(LayoutKind.Sequential)] struct Item { public IntPtr Name; public Value Value; }
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhOpenQuery(string source,IntPtr user,out IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr user,out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
        readonly object gate=new object();
        IntPtr query,counter; bool primed,disposed;
        DateTime retry;
        public GpuSnapshot Read() { lock(gate) {
            var result=new GpuSnapshot(); if(disposed) return result;
            if(query==IntPtr.Zero) {
                if(DateTime.UtcNow<retry) return result;
                retry=DateTime.UtcNow.AddMinutes(1);
                if(PdhOpenQuery(null,IntPtr.Zero,out query)!=0) { query=IntPtr.Zero; return result; }
                if(PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out counter)!=0) { Reset(); return result; }
            }
            if(PdhCollectQueryData(query)!=0) { Reset(); return result; }
            if(!primed) { primed=true; return result; }
            const uint MoreData=0x800007D2,Format=0x200|0x8000;
            for(int attempt=0;attempt<3;attempt++) {
                uint size=0,count; uint status=PdhGetFormattedCounterArray(counter,Format,ref size,out count,IntPtr.Zero);
                if(status!=MoreData || size==0 || size>32*1024*1024) return result;
                IntPtr buffer=Marshal.AllocHGlobal((int)size);
                try {
                    status=PdhGetFormattedCounterArray(counter,Format,ref size,out count,buffer);
                    if(status==MoreData) continue;
                    if(status!=0) return result;
                    int stride=Marshal.SizeOf(typeof(Item)); if(count>size/stride) return result;
                    result.Available=true;
                    for(int i=0;i<count;i++) {
                        var item=(Item)Marshal.PtrToStructure(IntPtr.Add(buffer,i*stride),typeof(Item));
                        Add(result,Marshal.PtrToStringUni(item.Name),item.Value.Number,item.Value.Status);
                    }
                    return result;
                } finally { Marshal.FreeHGlobal(buffer); }
            }
            return result;
        } }
        internal static void Add(GpuSnapshot result,string instance,double value,uint status) {
            var match=Regex.Match(instance??"",@"^pid_(\d+)_(luid_.+?)(?:#\d+)?$"); int pid;
            if(!match.Success || !int.TryParse(match.Groups[1].Value,out pid)) return;
            if(status>1 || double.IsNaN(value) || double.IsInfinity(value)) { result.Invalid.Add(pid); string id=GpuAttributionWindow.AdapterId(match.Groups[2].Value); if(id!=null) result.InvalidAdapters.Add(id); return; }
            Dictionary<string,double> engines;
            if(!result.Engines.TryGetValue(pid,out engines)) { engines=new Dictionary<string,double>(); result.Engines[pid]=engines; }
            string engine=match.Groups[2].Value; double prior; engines.TryGetValue(engine,out prior);
            engines[engine]=Math.Min(100,prior+Math.Max(0,value));
        }
        internal static double? Busiest(IEnumerable<Dictionary<string,double>> processes) {
            var totals=Combine(processes);
            return totals==null?(double?)null:Math.Min(100,totals.Values.DefaultIfEmpty(0).Max());
        }
        internal static Dictionary<string,double> Combine(IEnumerable<Dictionary<string,double>> processes) {
            var totals=new Dictionary<string,double>();
            foreach(var engines in processes) {
                if(engines==null) return null;
                foreach(var e in engines) { double value; totals.TryGetValue(e.Key,out value); totals[e.Key]=value+e.Value; }
            }
            return totals;
        }
        void Reset() { if(query!=IntPtr.Zero) PdhCloseQuery(query); query=counter=IntPtr.Zero; primed=false; }
        public void Dispose() { lock(gate) { disposed=true; Reset(); } }
    }
}
