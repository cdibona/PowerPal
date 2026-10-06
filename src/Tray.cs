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
using Microsoft.Win32;

namespace PowerPal {
    internal static class Brand {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        public static void DrawBolt(Graphics g,RectangleF r,Color color) {
            using(var b=new SolidBrush(color)) g.FillPolygon(b,new[]{new PointF(r.X+r.Width*.56f,r.Y),new PointF(r.X+r.Width*.12f,r.Y+r.Height*.56f),new PointF(r.X+r.Width*.48f,r.Y+r.Height*.56f),new PointF(r.X+r.Width*.36f,r.Bottom),new PointF(r.Right,r.Y+r.Height*.36f),new PointF(r.X+r.Width*.62f,r.Y+r.Height*.36f)});
        }
        public static Icon MakeIcon(Color color) {
            using(var bitmap=new Bitmap(32,32)) using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using(var background=new SolidBrush(Color.FromArgb(13,19,31))) g.FillEllipse(background,0,0,31,31);
                using(var border=new Pen(color,2)) g.DrawEllipse(border,1,1,29,29);
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using(var font=new Font("Segoe UI",16,FontStyle.Bold,GraphicsUnit.Pixel)) using(var ink=new SolidBrush(color))
                using(var format=new StringFormat(StringFormat.GenericTypographic) { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap }) g.DrawString("pp",font,ink,new RectangleF(9,0,22,30),format);
                DrawBolt(g,new RectangleF(3,6,8,18),color);
                IntPtr h=bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); }
            }
        }
    }
    internal sealed class Tray : ApplicationContext {
        readonly NotifyIcon icon; readonly Icon good=Brand.MakeIcon(Color.FromArgb(108,239,190)),bad=Brand.MakeIcon(Color.FromArgb(255,204,112));
        readonly Dashboard window; readonly History history; readonly ActivityHistory activityHistory; readonly PowerEventLog eventLog; readonly Preferences preferences=Preferences.Load(); readonly ActivityMonitor activity=new ActivityMonitor();
        readonly SessionCapture capture; PowerFrame power=new PowerFrame();
        readonly ToolStripMenuItem stopCaptureItem=new ToolStripMenuItem("Stop session capture") { Enabled=false };
        readonly ToolStripMenuItem recordingItem=new ToolStripMenuItem("Recorder starting...") { Enabled=false };
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=2000 };
        bool busy,exiting,updating,notified; DateTime nextSample=DateTime.MinValue,nextUpdate=DateTime.MinValue,nextActivitySample=DateTime.MinValue; string pendingInstaller,activityError,batteryError; DateTime? activitySaved;
        public Tray(bool background,string testFolder=null) {
            if(testFolder!=null) { preferences.AutoUpdate=false; notified=true; }
            history=new History(testFolder??Path.Combine(Preferences.Root,"History"));
            activityHistory=new ActivityHistory(testFolder==null?Path.Combine(Preferences.Root,"Activity"):Path.Combine(testFolder,"Activity"));
            eventLog=new PowerEventLog(testFolder==null?Path.Combine(Preferences.Root,"Events"):Path.Combine(testFolder,"Events"));
            capture=new SessionCapture(testFolder==null?Path.Combine(Preferences.Root,"Captures"):Path.Combine(testFolder,"Captures"));
            window=new Dashboard(history,activityHistory,eventLog);
            window.CaptureRequested+=ToggleCapture;
            var menu=new ContextMenuStrip { Renderer=new ThemeRenderer() }; menu.Items.Add(new ToolStripMenuItem("PowerPal v"+ReleaseUpdater.Current.ToString(3)) { Enabled=false }); menu.Items.Add(recordingItem); menu.Items.Add("Open PowerPal",null,delegate { window.Open(); }); menu.Items.Add("Check for updates",null,delegate { CheckUpdates(); });
            stopCaptureItem.Click+=delegate { ToggleCapture(null); }; menu.Items.Add(stopCaptureItem);
            menu.Items.Add("Settings",null,delegate { Settings(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit and stop recording",null,delegate { ExitThread(); });
            icon=new NotifyIcon { Icon=good,Text="PowerPal - starting recorder",Visible=true,ContextMenuStrip=menu }; icon.MouseClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) window.Open(); };
            window.ThemeRequested+=delegate { preferences.ThemeMode=Theme.Next; Theme.Set(preferences.ThemeMode); try { preferences.Save(); } catch(Exception ex) { window.UpdateRelease("Theme changed, but saving failed: "+ex.Message); } };
            SystemEvents.UserPreferenceChanged+=SystemThemeChanged;
            window.SettingsRequested+=Settings; window.UpdateRequested+=CheckUpdates;
            window.HiddenToTray+=delegate { if(pendingInstaller!=null) ApplyUpdate(); else if(!notified) { icon.ShowBalloonTip(2500,"PowerPal is still recording","Look for the mint pp icon in the tray. Battery and app activity logging continue.",ToolTipIcon.Info); notified=true; } };
            // Create a UI synchronization context even when starting hidden at sign-in.
            var handle=window.Handle; if(SynchronizationContextMissing()) System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            timer.Tick+=delegate { Tick(); }; timer.Start(); if(!background) window.Open(); Tick();
        }
        void SystemThemeChanged(object sender,UserPreferenceChangedEventArgs e) { if(!exiting && window.IsHandleCreated) try { window.BeginInvoke(new Action(Theme.RefreshSystem)); } catch(InvalidOperationException) { } }
        static bool SynchronizationContextMissing() { return System.Threading.SynchronizationContext.Current==null; }
        internal void TestStartCapture() { capture.Start("PowerPal"); window.UpdateCapture("PowerPal",null); }
        internal void TestWindowLifecycle(bool background=true) { if(window.Visible==background) throw new Exception("Launch visibility did not match arguments"); window.Open(); if(!window.Visible) throw new Exception("Dashboard did not open"); window.WindowState=FormWindowState.Minimized; if(window.Visible) throw new Exception("Minimize did not hide to tray"); window.Open(); window.Close(); if(window.Visible) throw new Exception("Close did not hide to tray"); }
        void ToggleCapture(string selected) {
            try {
                if(capture.Active) { string app=capture.App,path=capture.Stop(); eventLog.Add("Session capture saved",app+" / "+Path.GetFileName(path)); stopCaptureItem.Enabled=false; window.UpdateCapture(null,"Saved capture: "+Path.GetFileName(path)); }
                else { capture.Start(selected); stopCaptureItem.Enabled=true; eventLog.Add("Session capture started",selected+" / CPU + GPU estimates every 2 seconds; continues in the tray"); window.UpdateCapture(selected,null); }
            } catch(Exception ex) { window.UpdateCapture(capture.App,"Capture failed: "+ex.Message); }
        }
        void Settings() { window.Open(); using(var dialog=new SettingsDialog(preferences)) if(dialog.ShowDialog(window)==DialogResult.OK) { nextUpdate=DateTime.MinValue; if(!preferences.AutoUpdate) pendingInstaller=null; window.UpdateRelease(preferences.AutoUpdate?"Automatic updates enabled":"Automatic updates paused"); } }
        async void Tick() {
            if(busy || exiting) return; busy=true;
            try {
                if(DateTime.UtcNow>=nextSample) {
                    nextSample=DateTime.UtcNow.AddSeconds(10); var samples=await Task.Run(()=>Battery.Read()); if(exiting) return;
                    power=PowerFrame.From(samples);
                    string error=null; try { await Task.Run(()=> { history.Append(samples); eventLog.Observe(samples); }); } catch(Exception ex) { error="Recording failed: "+ex.Message; }
                    batteryError=error;
                    if(exiting) return; var s=samples[0];
                    string text=error!=null?"PowerPal - RECORDING ERROR":(capture.Active?"PowerPal - CAP | ":"PowerPal - REC | ")+s.Source+" | "+(s.Percent.HasValue?s.Percent.Value.ToString("0")+"%":"N/A")+" | "+(s.Watts.HasValue?s.Watts.Value.ToString("0.0")+"W":"N/A");
                    icon.Text=text.Substring(0,Math.Min(63,text.Length)); recordingItem.Text=error??"Recording - saved "+DateTime.Now.ToString("HH:mm:ss"); window.UpdateLive(samples,error);
                }
                try {
                    var consumers=await Task.Run(()=>activity.Read()); if(exiting) return; PowerEstimates.Apply(consumers,power,DateTime.UtcNow);
                    consumers=consumers.OrderByDescending(c=>c.PowerShare??c.Cpu).ThenByDescending(c=>c.MemoryMb).ToList();
                    window.UpdateActivity(consumers,activity.Inaccessible);
                    if(capture.Active) try { capture.Append(DateTime.UtcNow,consumers,power); } catch(Exception ex) { capture.Stop(); stopCaptureItem.Enabled=false; window.UpdateCapture(null,"Capture stopped: "+ex.Message); eventLog.Add("Session capture failed",ex.Message); }
                    if(DateTime.UtcNow>=nextActivitySample && consumers.Count>0) {
                        DateTime time=DateTime.UtcNow; nextActivitySample=time.AddSeconds(10);
                        bool saved=await Task.Run(()=>activityHistory.Append(time,consumers.Take(20).ToList(),activity.IntervalSeconds,activity.Inaccessible,power));
                        if(exiting) return; if(saved) activitySaved=time; activityError=null;
                    }
                } catch(Exception ex) { activityError="Activity log failed: "+ex.Message; }
                if(exiting) return; window.UpdateActivityLog(activitySaved,activityError);
                icon.Icon=activityError==null && batteryError==null?good:bad;
                if(activityError!=null) { icon.Text="PowerPal - activity logging error"; recordingItem.Text=activityError; }
                window.RefreshStatus();
            } catch(Exception ex) { if(!exiting) { icon.Icon=bad; icon.Text="PowerPal - measurement failed"; recordingItem.Text="Measurement failed"; window.UpdateLive(new List<Sample>(),"Measurement failed: "+ex.Message); } }
            finally { busy=false; }
            if(!exiting && preferences.AutoUpdate && DateTime.UtcNow>=nextUpdate) CheckUpdates();
            if(!exiting && pendingInstaller!=null && !window.Visible && !capture.Active) ApplyUpdate();
        }
        async void CheckUpdates() {
            if(updating || exiting) return; updating=true; window.SetUpdateBusy(true); nextUpdate=DateTime.UtcNow.AddHours(6); bool automaticAtStart=preferences.AutoUpdate;
            try { var updater=new ReleaseUpdater(s=> { if(!exiting) window.UpdateRelease(s); }); string installer=await updater.Check(); if(exiting || automaticAtStart && !preferences.AutoUpdate) return; pendingInstaller=installer;
                if(installer!=null) { icon.ShowBalloonTip(3000,"PowerPal update ready","A verified update will install in the tray after any active session capture ends.",ToolTipIcon.Info); if(!busy && !window.Visible && !capture.Active) ApplyUpdate(); }
            } finally { updating=false; if(!exiting) window.SetUpdateBusy(false); }
        }
        void ApplyUpdate() {
            if(busy || exiting || pendingInstaller==null || capture.Active) return;
            try { Process.Start(new ProcessStartInfo(pendingInstaller,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /POWERPALUPDATE") { UseShellExecute=true }); pendingInstaller=null; ExitThread(); }
            catch(Exception ex) { pendingInstaller=null; window.UpdateRelease("Could not start update: "+ex.Message); }
        }
        protected override void ExitThreadCore() { exiting=true; SystemEvents.UserPreferenceChanged-=SystemThemeChanged; timer.Stop(); timer.Dispose(); activity.Dispose(); try { if(capture.Active) { string app=capture.App; capture.Stop(); eventLog.Add("Session capture ended",app+" / PowerPal exited; captured CSV is saved."); } eventLog.Add("Recording stopped","PowerPal exited; battery and app activity logging stopped."); } catch(IOException) { } catch(UnauthorizedAccessException) { } icon.Visible=false; icon.Dispose(); good.Dispose(); bad.Dispose(); window.Dispose(); base.ExitThreadCore(); }
    }
}
