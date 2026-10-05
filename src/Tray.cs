using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PowerPal {
    internal static class Brand {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        public static void DrawBolt(Graphics g,RectangleF r,Color color) {
            using(var b=new SolidBrush(color)) g.FillPolygon(b,new[]{new PointF(r.X+r.Width*.56f,r.Y),new PointF(r.X+r.Width*.12f,r.Y+r.Height*.56f),new PointF(r.X+r.Width*.48f,r.Y+r.Height*.56f),new PointF(r.X+r.Width*.36f,r.Bottom),new PointF(r.Right,r.Y+r.Height*.36f),new PointF(r.X+r.Width*.62f,r.Y+r.Height*.36f)});
        }
        public static Icon MakeIcon(Color color) {
            using(var bitmap=new Bitmap(32,32)) using(var g=Graphics.FromImage(bitmap)) { g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent); DrawBolt(g,new RectangleF(3,2,25,28),color); IntPtr h=bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); } }
        }
    }
    internal sealed class Tray : ApplicationContext {
        readonly NotifyIcon icon; readonly Icon good=Brand.MakeIcon(Palette.Mint),bad=Brand.MakeIcon(Palette.Amber);
        readonly Dashboard window; readonly History history; readonly Preferences preferences=Preferences.Load(); readonly ActivityMonitor activity=new ActivityMonitor();
        readonly ToolStripMenuItem recordingItem=new ToolStripMenuItem("Recorder starting...") { Enabled=false };
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=2000 };
        bool busy,exiting,updating,notified; DateTime nextSample=DateTime.MinValue,nextUpdate=DateTime.MinValue; string pendingInstaller;
        public Tray(bool background,string testFolder=null) {
            if(testFolder!=null) { preferences.AutoUpdate=false; notified=true; }
            history=new History(testFolder??Path.Combine(Preferences.Root,"History")); window=new Dashboard(history);
            var menu=new ContextMenuStrip(); menu.Items.Add(recordingItem); menu.Items.Add("Open PowerPal",null,delegate { window.Open(); }); menu.Items.Add("Check for updates",null,delegate { CheckUpdates(); });
            menu.Items.Add("Settings",null,delegate { Settings(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit and stop recording",null,delegate { ExitThread(); });
            icon=new NotifyIcon { Icon=good,Text="PowerPal - starting recorder",Visible=true,ContextMenuStrip=menu }; icon.MouseClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) window.Open(); };
            window.SettingsRequested+=Settings; window.UpdateRequested+=CheckUpdates;
            window.HiddenToTray+=delegate { if(pendingInstaller!=null) ApplyUpdate(); else if(!notified) { icon.ShowBalloonTip(2500,"PowerPal is still recording","Look for the mint lightning bolt in the tray. Right-click it to stop recording.",ToolTipIcon.Info); notified=true; } };
            // Create a UI synchronization context even when starting hidden at sign-in.
            var handle=window.Handle; if(SynchronizationContextMissing()) System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            timer.Tick+=delegate { Tick(); }; timer.Start(); if(!background) window.Open(); Tick();
        }
        static bool SynchronizationContextMissing() { return System.Threading.SynchronizationContext.Current==null; }
        internal void TestWindowLifecycle() { if(window.Visible) throw new Exception("Background start was visible"); window.Open(); if(!window.Visible) throw new Exception("Dashboard did not open"); window.WindowState=FormWindowState.Minimized; if(window.Visible) throw new Exception("Minimize did not hide to tray"); window.Open(); window.Close(); if(window.Visible) throw new Exception("Close did not hide to tray"); }
        void Settings() { window.Open(); using(var dialog=new SettingsDialog(preferences)) if(dialog.ShowDialog(window)==DialogResult.OK) { nextUpdate=DateTime.MinValue; if(!preferences.AutoUpdate) pendingInstaller=null; window.UpdateRelease(preferences.AutoUpdate?"Automatic updates enabled":"Automatic updates paused"); } }
        async void Tick() {
            if(busy || exiting) return; busy=true;
            try {
                var consumers=await Task.Run(()=>activity.Read()); if(exiting) return; window.UpdateActivity(consumers,activity.Inaccessible);
                if(DateTime.UtcNow>=nextSample) {
                    nextSample=DateTime.UtcNow.AddSeconds(10); var samples=await Task.Run(()=>Battery.Read()); if(exiting) return;
                    string error=null; try { await Task.Run(()=>history.Append(samples)); } catch(Exception ex) { error="Recording failed: "+ex.Message; }
                    if(exiting) return; var s=samples[0];
                    string text=error!=null?"PowerPal - RECORDING ERROR":"PowerPal - REC | "+s.Source+" | "+(s.Percent.HasValue?s.Percent.Value.ToString("0")+"%":"N/A")+" | "+(s.Watts.HasValue?s.Watts.Value.ToString("0.0")+"W":"N/A");
                    icon.Text=text.Substring(0,Math.Min(63,text.Length)); icon.Icon=error==null?good:bad; recordingItem.Text=error??"Recording - saved "+DateTime.Now.ToString("HH:mm:ss"); window.UpdateLive(samples,error);
                }
                window.RefreshStatus();
            } catch(Exception ex) { if(!exiting) { icon.Icon=bad; icon.Text="PowerPal - measurement failed"; recordingItem.Text="Measurement failed"; window.UpdateLive(new List<Sample>(),"Measurement failed: "+ex.Message); } }
            finally { busy=false; }
            if(!exiting && preferences.AutoUpdate && DateTime.UtcNow>=nextUpdate) CheckUpdates();
            if(!exiting && pendingInstaller!=null && !window.Visible) ApplyUpdate();
        }
        async void CheckUpdates() {
            if(updating || exiting) return; updating=true; nextUpdate=DateTime.UtcNow.AddHours(6); bool automaticAtStart=preferences.AutoUpdate;
            try { var updater=new ReleaseUpdater(s=> { if(!exiting) window.UpdateRelease(s); }); string installer=await updater.Check(); if(exiting || automaticAtStart && !preferences.AutoUpdate) return; pendingInstaller=installer;
                if(installer!=null) { icon.ShowBalloonTip(3000,"PowerPal update ready","A verified update will install when PowerPal is in the tray.",ToolTipIcon.Info); if(!busy && !window.Visible) ApplyUpdate(); }
            } finally { updating=false; }
        }
        void ApplyUpdate() {
            if(busy || exiting || pendingInstaller==null) return;
            try { Process.Start(new ProcessStartInfo(pendingInstaller,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /POWERPALUPDATE") { UseShellExecute=true }); pendingInstaller=null; ExitThread(); }
            catch(Exception ex) { pendingInstaller=null; window.UpdateRelease("Could not start update: "+ex.Message); }
        }
        protected override void ExitThreadCore() { exiting=true; timer.Stop(); timer.Dispose(); icon.Visible=false; icon.Dispose(); good.Dispose(); bad.Dispose(); window.Dispose(); base.ExitThreadCore(); }
    }
}
