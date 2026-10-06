using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerPal {
    internal static class UiTests {
        [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
        public static void Run(string folder) {
            Directory.CreateDirectory(folder);
            var history=new History(Path.Combine(folder,"fixture-"+Guid.NewGuid().ToString("N"))); var samples=new List<Sample>();
            for(int i=0;i<60;i++) samples.Add(new Sample { Time=DateTime.UtcNow.AddSeconds((i-59)*10),Source="Battery",State="Discharging",Percent=80-i*.3,Volts=15.2,Watts=-110-10*Math.Sin(i*.12) });
            history.Append(samples);
            var apps=new List<Consumer> { new Consumer { Name="Space adventure",Cpu=22,Gpu=93,MemoryMb=6120,DiskMb=3 },new Consumer { Name="Browser",Cpu=4,Gpu=3,MemoryMb=1200,DiskMb=.4 },new Consumer { Name="Music",Cpu=.5,Gpu=0,MemoryMb=115,DiskMb=.1 },new Consumer { Name="PowerPal",Cpu=.1,Gpu=.1,MemoryMb=50,DiskMb=0 } };
            PowerEstimates.Apply(apps,PowerFrame.From(new[]{samples.Last()}),DateTime.UtcNow);
            var report=new List<string>();
            using(var form=new Dashboard(history)) using(var settings=new SettingsDialog(new Preferences())) {
                form.Opacity=0; form.ShowInTaskbar=false; form.Show(); settings.Opacity=0; settings.ShowInTaskbar=false; settings.Show(form);
                if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(form.Handle),new IntPtr(-4))) throw new Exception("Window did not opt into PerMonitorV2");
                report.Add("Actual Windows DPI: "+form.DeviceDpi+"; PerMonitorV2 confirmed; target: "+AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName);
                apps[0].GpuWatts=72.5; apps[0].GpuEnergyWh=.412; apps[1].GpuWatts=2.3;
                var sensors=new GpuPowerReport { Time=DateTime.UtcNow,Status="Fixture sensor" };
                sensors.Devices.Add(new GpuPowerReading { Id="fixture",Name="NVIDIA fixture",Watts=85,ObservedWh=.52,UnassignedWatts=10.2,Method="nvml-energy" });
                form.UpdateSensors(sensors); form.UpdateLive(new List<Sample>{samples.Last()},null); form.SeedPreview(apps);
                for(int j=0;j<20;j++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                foreach(string theme in new[]{"Light","Dark"}) foreach(int dpi in new[]{96,120,144,192}) {
                    Theme.Set(theme); form.SetContentDpi(dpi); settings.SetContentDpi(dpi); form.ClientSize=new Size(1180*dpi/96,900*dpi/96); settings.ClientSize=new Size(560*dpi/96,625*dpi/96); Application.DoEvents();
                    if(!form.ContentFits || !settings.ContentFits) throw new Exception("Controls overflow at "+dpi+" DPI");
                    form.SaveCanvas(Path.Combine(folder,"dashboard-"+theme+"-"+dpi+".png"));
                    settings.SaveCanvas(Path.Combine(folder,"settings-"+theme+"-"+dpi+".png"));
                    report.Add("PASS: "+theme+" "+dpi+" DPI: dashboard/settings bounds, scaled fonts and rows");
                }
                form.SetUpdateBusy(true); form.UpdateRelease("Checking GitHub releases..."); form.SetContentDpi(96); form.ClientSize=new Size(1180,900);
                form.SaveCanvas(Path.Combine(folder,"update-checking.png"));
                form.UpdateRelease("Downloading PowerPal 0.3.0..."); form.SaveCanvas(Path.Combine(folder,"update-downloading.png"));
                form.UpdateRelease("v"+ReleaseUpdater.Current.ToString(3)+" - up to date with GitHub releases"); form.SetUpdateBusy(false);
                // Return to a prior DPI to catch cumulative/double scaling.
                form.SetContentDpi(96); if(!form.ContentFits) throw new Exception("DPI round-trip layout failed");
            }
            File.WriteAllLines(Path.Combine(folder,"ui-result.txt"),report);
        }
    }
}
