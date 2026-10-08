using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Microsoft.Win32.SafeHandles;

namespace PowerPal {
    internal sealed class Sample {
        public DateTime Time = DateTime.UtcNow;
        public string Source = "Unknown", Device = "System", State = "Unknown";
        public double? Percent, Volts, Watts, Wh;
        public static string Number(double? n) { return n.HasValue ? n.Value.ToString("0.###", CultureInfo.InvariantCulture) : ""; }
        public string Csv() { return string.Join(",", new[] { Time.ToString("o"), Source, Device, State, Number(Percent), Number(Volts), Number(Watts), Number(Wh) }); }
        public static Sample Parse(string line) {
            var p = line.Split(','); if (p.Length != 8) throw new FormatException();
            return new Sample { Time = DateTime.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(), Source=p[1], Device=p[2], State=p[3], Percent=Read(p[4]), Volts=Read(p[5]), Watts=Read(p[6]), Wh=Read(p[7]) };
        }
        static double? Read(string s) { return s == "" ? (double?)null : double.Parse(s, CultureInfo.InvariantCulture); }
    }

    internal static class Battery {
        [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AC, Flags, Percent, Saver; public uint Life, FullLife; }
        [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr h, uint f);
        [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid g, uint i, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr dev);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, int insize, byte[] output, int outsize, out uint count, IntPtr ov);
        static byte[] Query(SafeFileHandle h, uint code, byte[] input, int size) {
            var output = new byte[size]; uint count;
            return DeviceIoControl(h, code, input, input.Length, output, size, out count, IntPtr.Zero) && count >= size ? output : null;
        }
        internal static double? Voltage(uint v) { return v == uint.MaxValue || v == 0 ? (double?)null : v / 1000.0; }
        internal static double? Rate(int r, bool relative) { return relative || r == int.MinValue ? (double?)null : r / 1000.0; }
        public static List<Sample> Read() {
            PowerStatus ps; bool known = GetSystemPowerStatus(out ps);
            string source = !known || ps.AC == 255 ? "Unknown" : ps.AC == 1 ? "External power" : "Battery";
            var result = new List<Sample>();
            var guid = new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set != new IntPtr(-1)) {
                try {
                    for (uint i=0; ; i++) {
                        var d = new InterfaceData { Size = Marshal.SizeOf(typeof(InterfaceData)) };
                        if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref d)) break;
                        uint size; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out size, IntPtr.Zero);
                        if (size < 8) continue;
                        IntPtr ptr = Marshal.AllocHGlobal((int)size);
                        try {
                            Marshal.WriteInt32(ptr, IntPtr.Size == 8 ? 8 : 6);
                            if (!SetupDiGetDeviceInterfaceDetail(set, ref d, ptr, size, out size, IntPtr.Zero)) continue;
                            string path = Marshal.PtrToStringUni(IntPtr.Add(ptr, 4));
                            using (var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                                if (h.IsInvalid) continue;
                                var tag = Query(h, 0x294040, new byte[4], 4);
                                if (tag == null || BitConverter.ToUInt32(tag,0) == 0) continue;
                                var infoIn = new byte[12]; Array.Copy(tag, infoIn, 4);
                                var info = Query(h, 0x294044, infoIn, 36);
                                var statusIn = new byte[20]; Array.Copy(tag, statusIn, 4);
                                var status = Query(h, 0x29404c, statusIn, 16);
                                if (info == null || status == null) continue;
                                uint caps=BitConverter.ToUInt32(info,0), full=BitConverter.ToUInt32(info,16), capacity=BitConverter.ToUInt32(status,4), state=BitConverter.ToUInt32(status,0);
                                if ((caps & 0x80000000) == 0) continue;
                                bool relative = (caps & 0x40000000) != 0;
                                result.Add(new Sample { Source=source, Device=Identity(path), State=(state & 2)!=0 ? "Discharging" : (state & 4)!=0 ? "Charging" : "Idle",
                                    Percent=capacity!=uint.MaxValue && full!=0 && full!=uint.MaxValue ? (double?)Math.Min(100,100.0*capacity/full) : null,
                                    Volts=Voltage(BitConverter.ToUInt32(status,8)), Watts=Rate(BitConverter.ToInt32(status,12),relative), Wh=!relative && capacity!=uint.MaxValue ? (double?)(capacity/1000.0) : null });
                            }
                        } finally { Marshal.FreeHGlobal(ptr); }
                    }
                } finally { SetupDiDestroyDeviceInfoList(set); }
            }
            if (result.Count == 0) result.Add(new Sample { Source=source, State=known && (ps.Flags & 128)!=0 && ps.Flags!=255 ? "No battery" : "Details unavailable", Percent=known && ps.Percent<=100 ? (double?)ps.Percent : null });
            return result;
        }
        static string Identity(string path) {
            // Deterministic device key; no machine device path is written to history.
            uint hash=2166136261; foreach(char c in path.ToUpperInvariant()) hash=unchecked((hash ^ c)*16777619);
            return "Battery-" + hash.ToString("X8");
        }
    }

    internal sealed class History {
        public const string Header = "utc,source,device,state,percent,battery_volts,battery_watts,remaining_wh";
        public readonly string Folder;
        public History(string folder) { Folder=folder; Directory.CreateDirectory(folder); }
        public void Append(IEnumerable<Sample> samples) {
            foreach (var group in samples.GroupBy(s => s.Time.ToString("yyyy-MM-dd"))) {
                string path=Path.Combine(Folder,group.Key+".csv");
                using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)) {
                    bool needsNewline=false;
                    if(stream.Length>0) { stream.Seek(-1,SeekOrigin.End); needsNewline=stream.ReadByte()!=10; }
                    stream.Seek(0,SeekOrigin.End);
                    using(var w=new StreamWriter(stream)) { if(stream.Length==0) w.WriteLine(Header); else if(needsNewline) w.WriteLine(); foreach(var s in group) w.WriteLine(s.Csv()); }
                }
            }
        }
        public List<Sample> Load(DateTime since) {
            var rows = new List<Sample>();
            foreach(string path in Directory.GetFiles(Folder,"????-??-??.csv").OrderBy(p=>p)) {
                DateTime day; if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out day) || day < since.Date) continue;
                using(var reader=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))) {
                    reader.ReadLine(); string line;
                    while((line=reader.ReadLine())!=null) try { var s=Sample.Parse(line); if(s.Time>=since) rows.Add(s); } catch(FormatException) { /* Ignore interrupted final rows. */ }
                }
            }
            return rows;
        }
    }

    internal static class Program {
        internal static bool BackgroundOnStart(string[] args) { return !args.Contains("--show") || args.Contains("--background"); }
        [STAThread] static int Main(string[] args) {
            if(args.Contains("--dpi-test") || args.Contains("--window-test") || args.Contains("--font-test")) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>1 && args[0]=="--font-test") { try { FontTests.Run(args[1]); return 0; } catch(Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"font-result.txt"),ex.ToString()); return 1; } }
            var preferences=Preferences.Load(); Theme.Set(preferences.ThemeMode); DisplayScaling.Set(preferences.DisplayScalePercent);
            if(args.Length>1 && args[0]=="--window-test") { try { WindowTests.Run(args[1]); return 0; } catch(Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"window-result.txt"),ex.ToString()); return 1; } }
            if(args.Length>1 && args[0]=="--dpi-test") { try { UiTests.Run(args[1]); return 0; } catch(Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"ui-result.txt"),ex.ToString()); return 1; } }
            if(args.Length>1 && args[0]=="--sensor-probe") { try { SensorTests.Probe(args[1]); return 0; } catch(Exception ex) { File.WriteAllText(args[1],ex.ToString()); return 1; } }
            if(args.Length>1 && args[0]=="--activity-probe") {
                using(var monitor=new ActivityMonitor()) { monitor.Read(); Thread.Sleep(2200); var watch=System.Diagnostics.Stopwatch.StartNew(); var apps=monitor.Read(); var frame=PowerFrame.From(Battery.Read()); PowerEstimates.Apply(apps,frame,DateTime.UtcNow); var lines=new List<string> { "GPU available: "+monitor.GpuAvailable+"; app groups: "+apps.Count+"; read ms: "+watch.ElapsedMilliseconds }; lines.AddRange(apps.OrderByDescending(c=>c.PowerShare??c.Cpu).Take(10).Select(c=>c.Name+" CPU="+c.Cpu+" GPU="+c.Gpu+" estimated share="+c.PowerShare+" estimated watts="+c.EstimatedWatts)); File.WriteAllLines(args[1],lines); return apps.Count>0?0:1; }
            }
            if(args.Length>0 && args[0]=="--self-test") return Test();
            if(args.Length>1 && args[0]=="--verify-public-release") {
                var messages=new List<string>(); var updater=new ReleaseUpdater(message=>messages.Add(message));
                string installer=updater.Check(true).GetAwaiter().GetResult();
                if(installer!=null) messages.Add("PASS: public release downloaded and verified by the production updater: "+installer);
                File.WriteAllLines(args[1],messages); return installer==null?1:0;
            }
            if(args.Length>1 && args[0]=="--write-icon") { using(var icon=Brand.MakeIcon(Color.FromArgb(108,239,190))) using(var file=File.Create(args[1])) icon.Save(file); return 0; }
            if(args.Length>1 && args[0]=="--runtime-test") {
                string result=Path.Combine(args[1],"runtime-result.txt"); Directory.CreateDirectory(args[1]);
                new Preferences { StorageFolder=Path.Combine(args[1],"History","Preferences"),TopAppCount=3 }.Save();
                bool background=BackgroundOnStart(args);
                using(var app=new Tray(background,Path.Combine(args[1],"History"))) using(var end=new System.Windows.Forms.Timer { Interval=20000 }) {
                    end.Tick+=delegate { end.Stop(); try { app.TestWindowLifecycle(background); var saved=new History(Path.Combine(args[1],"History")).Load(DateTime.UtcNow.AddMinutes(-2)); if(saved.Count<2) throw new Exception("Expected two battery samples"); var activityRows=new ActivityHistory(Path.Combine(args[1],"History","Activity")).Lines(DateTime.UtcNow.AddMinutes(-2)).Skip(1).ToList(); if(activityRows.Select(row=>row.Split(',')[0]).Distinct().Count()<2) throw new Exception("Expected two persisted app activity snapshots"); if(activityRows.GroupBy(row=>row.Split(',')[0]).Any(g=>g.Count()>3)) throw new Exception("Automatic app limit was not respected"); File.WriteAllText(result,"PASS: automatic top-3 app history, "+(background?"hidden":"visible")+" start, battery and app activity recording, reopen, minimize to tray, close to tray. Battery samples: "+saved.Count+"; activity rows: "+activityRows.Count); } catch(Exception ex) { File.WriteAllText(result,ex.ToString()); } app.ExitThread(); };
                    end.Start(); Application.Run(app);
                }
                return File.ReadAllText(result).StartsWith("PASS")?0:1;
            }
            if(args.Length>1 && args[0]=="--ui-test") {
                string folder=Path.Combine(Path.GetTempPath(),"PowerPal-ui-"+Guid.NewGuid());
                try {
                    var h=new History(folder); var samples=new List<Sample>();
                    for(int i=0;i<60;i++) samples.Add(new Sample { Time=DateTime.UtcNow.AddSeconds((i-60)*10),Source=i<40 ? "Battery" : "External power",State=i<40 ? "Discharging" : "Charging",Percent=i<40 ? 80-i/2.0 : 60+(i-40)/2.0,Volts=15.2,Watts=i<40 ? -12 : 22 });
                    h.Append(samples);
                    using(var form=new Dashboard(h)) { form.Show(); form.UpdateLive(new List<Sample>{samples.Last()},null); form.SeedPreview(new List<Consumer> { new Consumer { Name="Video editor",Cpu=18.4,MemoryMb=2216,DiskMb=14.2 },new Consumer { Name="Browser",Cpu=6.1,MemoryMb=1438,DiskMb=0.8 },new Consumer { Name="Music",Cpu=1.2,MemoryMb=183,DiskMb=0.1 },new Consumer { Name="PowerPal",Cpu=0.1,MemoryMb=36,DiskMb=0 } }); for(int j=0;j<30;j++) { Application.DoEvents(); Thread.Sleep(20); } using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); } }
                } finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
                return 0;
            }
            if(args.Length>1 && args[0]=="--probe") { File.WriteAllLines(args[1],new[]{History.Header}.Concat(Battery.Read().Select(s=>s.Csv()))); return 0; }
            bool created; using(var mutex=new Mutex(true,"Local\\PowerPal.Tray",out created)) {
                if(!created) { MessageBox.Show("PowerPal is already running in the system tray."); return 0; }
                try { Application.Run(new Tray(BackgroundOnStart(args))); } catch(Exception ex) { MessageBox.Show(ex.Message,"PowerPal could not start"); return 1; }
            }
            return 0;
        }
        static int Test() {
            string folder=Path.Combine(Path.GetTempPath(),"PowerPal-test-"+Guid.NewGuid());
            try {
                if(Battery.Voltage(uint.MaxValue)!=null || Battery.Rate(int.MinValue,false)!=null || Battery.Rate(15000,true)!=null || Battery.Rate(-15000,false)!=-15) throw new Exception("Units/sentinels failed");
                var s=new Sample { Percent=55.5,Volts=12.1,Watts=-8.25,Source="Battery" };
                var prior=CultureInfo.CurrentCulture; Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
                var round=Sample.Parse(s.Csv()); Thread.CurrentThread.CurrentCulture=prior;
                if(round.Watts!=-8.25 || round.Wh!=null || round.Time!=s.Time) throw new Exception("CSV round trip failed");
                var h=new History(folder); h.Append(new[]{s});
                File.AppendAllText(Directory.GetFiles(folder)[0],"partial,row\n");
                if(h.Load(DateTime.UtcNow.AddMinutes(-1)).Count!=1 || h.Load(DateTime.UtcNow.AddDays(1)).Count!=0) throw new Exception("Persistence failed");
                RegressionTests.Run(folder);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: units, missing values, relative rates, CSV culture/round trip, persistence, malformed row recovery, date filter, CPU normalization, release parsing, release asset URL restrictions, digest verification, interrupted-row append recovery, startup modes, activity CSV persistence/export, power-event transitions, bounded resource histories, GPU adapter mapping and attribution, energy counters/reset/gaps, sensor polling/idle/failure behavior, source history, stale/AC/missing reading suppression, automatic CPU/GPU ranking, scale preferences, legacy activity export, system theme overrides.");
                return 0;
            } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),ex.ToString()); return 1; }
            finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
        }
    }
}
