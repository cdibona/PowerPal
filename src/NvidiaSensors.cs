using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerPal {
    internal sealed class GraphicsAdapter {
        public string Id,Name;
        public uint Vendor,Device,Subsystem;
    }
    internal static class GraphicsAdapters {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Description {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;
            public uint Vendor,Device,Subsystem,Revision;
            public UIntPtr VideoMemory,SystemMemory,SharedMemory;
            public uint LuidLow,LuidHigh;
        }
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory(ref Guid iid,out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters(IntPtr self,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescription(IntPtr self,out Description description);
        static T Method<T>(IntPtr instance,int slot) { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T)); }
        public static List<GraphicsAdapter> Read() {
            var result=new List<GraphicsAdapter>(); IntPtr factory=IntPtr.Zero;
            try {
                var iid=new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                if(CreateDXGIFactory(ref iid,out factory)<0) return result;
                var enumerate=Method<EnumAdapters>(factory,7);
                for(uint i=0;i<32;i++) {
                    IntPtr adapter; if(enumerate(factory,i,out adapter)<0) break;
                    try {
                        Description d; if(Method<GetDescription>(adapter,8)(adapter,out d)<0) continue;
                        result.Add(new GraphicsAdapter { Id="luid_0x"+d.LuidHigh.ToString("x8")+"_0x"+d.LuidLow.ToString("x8"),Name=d.Name,Vendor=d.Vendor,Device=d.Device,Subsystem=d.Subsystem });
                    } finally { Marshal.Release(adapter); }
                }
            } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { } finally { if(factory!=IntPtr.Zero) Marshal.Release(factory); }
            return result;
        }
        internal static GraphicsAdapter Match(IEnumerable<GraphicsAdapter> adapters,uint pciDevice,uint subsystem) {
            var matches=adapters.Where(a=>a.Vendor==(pciDevice&0xffff) && a.Device==(pciDevice>>16) && a.Subsystem==subsystem).ToList();
            // Identical boards cannot be distinguished by these IDs. Never guess the LUID.
            return matches.Count==1?matches[0]:null;
        }
    }
    internal sealed class RawGpuPower {
        public string Id,Name;
        public ulong? Millijoules;
        public double? Watts;
    }
    internal interface IGpuPowerSource : IDisposable {
        IList<GraphicsAdapter> Adapters { get; }
        List<RawGpuPower> Read(ISet<string> active);
        void Pause();
    }
    // Read-only calls into the user's installed NVIDIA driver. No DLLs/drivers are shipped.
    internal sealed class NvidiaSensors : IGpuPowerSource {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Count(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Handle(uint index,out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Pci(IntPtr device,IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Energy(IntPtr device,out ulong value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Power(IntPtr device,out uint value);
        IntPtr library; Init shutdown; Energy energy; Power power;
        readonly Dictionary<string,IntPtr> devices=new Dictionary<string,IntPtr>();
        public IList<GraphicsAdapter> Adapters { get; private set; }
        public NvidiaSensors() { Adapters=GraphicsAdapters.Read().Where(a=>a.Vendor==0x10de).ToList(); }
        T Function<T>(string name,bool required=true) {
            IntPtr pointer=GetProcAddress(library,name);
            if(pointer==IntPtr.Zero) { if(required) throw new InvalidOperationException("Installed NVIDIA driver lacks "+name); return default(T); }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }
        void Open() {
            if(library!=IntPtr.Zero) return;
            // Absolute trusted driver locations, with dependencies restricted to that directory/System32.
            foreach(string path in new[]{Path.Combine(Environment.SystemDirectory,"nvml.dll"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"NVIDIA Corporation\NVSMI\nvml.dll")}) {
                if(File.Exists(path)) library=LoadLibraryEx(path,IntPtr.Zero,0x100|0x800);
                if(library!=IntPtr.Zero) break;
            }
            if(library==IntPtr.Zero) throw new InvalidOperationException("NVIDIA sensor library unavailable");
            try {
                var init=Function<Init>("nvmlInit_v2"); var end=Function<Init>("nvmlShutdown");
                if(init()!=0) throw new InvalidOperationException("NVIDIA sensors could not initialize");
                shutdown=end; energy=Function<Energy>("nvmlDeviceGetTotalEnergyConsumption",false); power=Function<Power>("nvmlDeviceGetPowerUsage",false);
                var count=Function<Count>("nvmlDeviceGetCount_v2"); var handle=Function<Handle>("nvmlDeviceGetHandleByIndex_v2"); var pci=Function<Pci>("nvmlDeviceGetPciInfo_v3");
                uint length; if(count(out length)!=0 || length>32) throw new InvalidOperationException("NVIDIA device enumeration failed");
                for(uint i=0;i<length;i++) {
                    IntPtr device; if(handle(i,out device)!=0) continue;
                    IntPtr info=Marshal.AllocHGlobal(68);
                    try {
                        if(pci(device,info)!=0) continue;
                        var adapter=GraphicsAdapters.Match(Adapters,unchecked((uint)Marshal.ReadInt32(info,28)),unchecked((uint)Marshal.ReadInt32(info,32)));
                        if(adapter!=null) devices[adapter.Id]=device;
                    } finally { Marshal.FreeHGlobal(info); }
                }
                if(devices.Count==0) throw new InvalidOperationException("NVIDIA adapter mapping unavailable or ambiguous");
            } catch { Pause(); throw; }
        }
        public List<RawGpuPower> Read(ISet<string> active) {
            Open(); var result=new List<RawGpuPower>();
            foreach(var adapter in Adapters.Where(a=>active.Contains(a.Id))) {
                IntPtr device; if(!devices.TryGetValue(adapter.Id,out device)) continue;
                ulong mj; uint mw;
                var reading=new RawGpuPower { Id=adapter.Id,Name=adapter.Name };
                if(energy!=null && energy(device,out mj)==0) reading.Millijoules=mj;
                if(power!=null && power(device,out mw)==0 && mw<=2000000) reading.Watts=mw/1000.0;
                if(reading.Millijoules.HasValue || reading.Watts.HasValue) result.Add(reading);
            }
            if(result.Count==0) throw new InvalidOperationException("GPU power readings unavailable on this driver/device");
            return result;
        }
        public void Pause() {
            devices.Clear(); if(shutdown!=null) { shutdown(); shutdown=null; }
            energy=null; power=null; if(library!=IntPtr.Zero) { FreeLibrary(library); library=IntPtr.Zero; }
        }
        public void Dispose() { Pause(); }
    }
}
