using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PowerPal {
    internal static class WindowTests {
        static void Check(bool good,string message) { if(!good) throw new Exception(message); }
        static IEnumerable<Control> Descendants(Control parent) { foreach(Control child in parent.Controls) { yield return child; foreach(var c in Descendants(child)) yield return c; } }
        static bool OnScreen(Form form) { return Screen.FromControl(form).WorkingArea.Contains(form.Bounds); }
        public static void Run(string folder) {
            Directory.CreateDirectory(folder); var report=new List<string>(); int originalScale=DisplayScaling.Percent;
            var preferences=new Preferences { StorageFolder=Path.Combine(folder,"preferences") };
            try {
                DisplayScaling.Set(0);
                using(var form=new Dashboard(new History(Path.Combine(folder,"history")))) {
                    form.ShowInTaskbar=false; form.Opacity=0; form.Open(); Application.DoEvents();
                    Check(OnScreen(form),"Initial dashboard extends off the working area: "+form.Bounds);
                    Check(!Descendants(form).OfType<Button>().Any(b=>b.Text.Contains("Capture")),"Manual Capture button should be removed");
                    report.Add("PASS: initial dashboard fits working area; manual Capture button absent. Actual DPI: "+form.DeviceDpi);
                    form.WindowState=FormWindowState.Maximized; Application.DoEvents();
                    ExerciseSettings(form,preferences,folder,report);
                    var maximized=form.Bounds;
                    form.WindowState=FormWindowState.Minimized; Application.DoEvents(); Check(!form.Visible,"Minimize did not hide to tray");
                    form.Open(); Application.DoEvents(); Check(form.WindowState==FormWindowState.Maximized && form.Bounds==maximized,"Reopen from tray lost maximized state");
                    form.Close(); Check(!form.Visible,"Close did not hide to tray"); form.Open(); Application.DoEvents();
                    Check(form.WindowState==FormWindowState.Maximized && form.Bounds==maximized,"Reopen after close lost maximized state");
                    report.Add("PASS: maximized state retained through minimize, close and tray reopen.");
                    DisplayScaling.Set(150); var restored=form.RestoreBounds;
                    form.ApplyMonitorDpi(192,new Rectangle(100,100,900,700)); Application.DoEvents();
                    Check(form.WindowState==FormWindowState.Maximized && form.Bounds==maximized && form.RestoreBounds==restored && form.ContentDpi==144,"Monitor DPI change disturbed maximized/manual layout");
                    DisplayScaling.Set(0); form.ApplyMonitorDpi(120,form.Bounds);
                    Check(form.ContentDpi==120,"Follow-Windows mode ignored monitor DPI change");
                    form.ApplyMonitorDpi(form.DeviceDpi,form.Bounds);
                    report.Add("PASS: monitor-DPI handler respects manual override and follows Windows in automatic mode without restoring the window.");
                    form.WindowState=FormWindowState.Normal; Application.DoEvents();
                    var work=Screen.FromControl(form).WorkingArea;
                    form.Bounds=new Rectangle(work.Left+30,work.Top+30,Math.Min(1100,work.Width-60),Math.Min(860,work.Height-60));
                    ExerciseSettings(form,preferences,folder,report);
                    var bounds=form.Bounds; form.Close(); form.Open(); Application.DoEvents();
                    Check(form.WindowState==FormWindowState.Normal && form.Bounds==bounds,"Normal window bounds changed during tray reopen");
                    report.Add("PASS: normal window size/position survives Settings, all scale choices and tray reopen.");
                    var loaded=Preferences.LoadFrom(preferences.StorageFolder);
                    Check(loaded.DisplayScalePercent==0 && loaded.AppLimit==7,"Settings save did not persist scale/top-app selections");
                    report.Add("PASS: actual Settings controls save scale and top-app limit to isolated preferences.");
                }
                File.WriteAllLines(Path.Combine(folder,"window-result.txt"),report);
            } finally { DisplayScaling.Set(originalScale); }
        }
        static void ExerciseSettings(Dashboard form,Preferences preferences,string folder,List<string> report) {
            var state=form.WindowState; var bounds=form.Bounds; var restored=form.RestoreBounds;
            Exception failure=null; int choice=0,stage=0,ticks=0; SettingsDialog current=null;
            using(var driver=new Timer { Interval=80 }) {
                driver.Tick+=delegate {
                    try {
                        Check(++ticks<80,"Timed out exercising modal Settings");
                        current=Application.OpenForms.OfType<SettingsDialog>().FirstOrDefault(); if(current==null) return;
                        Check(form.WindowState==state && form.Bounds==bounds && form.RestoreBounds==restored,"Settings or scaling changed parent state/bounds: "+state+" -> "+form.WindowState);
                        Check(OnScreen(current),"Settings extends off the working area at "+DisplayScaling.Percent+"%: "+current.Bounds);
                        Check(current.ContentFits && form.ContentFits,"Controls overflow their scrollable canvas");
                        var scale=Descendants(current).OfType<ComboBox>().Single(c=>c.AccessibleName=="Display scaling");
                        if(stage==2) {
                            driver.Stop();
                            Descendants(current).OfType<NumericUpDown>().Single().Value=7;
                            Descendants(current).OfType<Button>().Single(b=>b.Text=="Save settings").PerformClick(); return;
                        }
                        if(stage==0) {
                            var nested=form.OpenSettings(preferences); int dialogs=Application.OpenForms.OfType<SettingsDialog>().Count(); Check(nested==DialogResult.None && dialogs==1,"Repeated Settings request result="+nested+"; dialog count="+dialogs);
                            stage=1;
                        }
                        if(choice>0) {
                            int chosen=DisplayScaling.Options[choice-1];
                            Check(form.ContentDpi==DisplayScaling.ResolveDpi(form.DeviceDpi,chosen) && current.ContentDpi==DisplayScaling.ResolveDpi(current.DeviceDpi,chosen),"Scale selector did not apply to both windows");
                            if(state==FormWindowState.Maximized && (chosen==125 || chosen==200)) current.SaveCanvas(Path.Combine(folder,"settings-"+chosen+".png"));
                        }
                        if(choice<DisplayScaling.Options.Length) { scale.SelectedIndex=choice++; return; }
                        scale.SelectedIndex=0; stage=2;
                    } catch(Exception ex) { failure=ex; driver.Stop(); if(current!=null) { current.DialogResult=DialogResult.Cancel; current.Close(); } }
                };
                driver.Start(); form.OpenSettings(preferences,delegate(bool enabled) { });
            }
            if(failure!=null) throw failure;
            Check(form.WindowState==state && form.Bounds==bounds,"Settings close changed dashboard bounds");
            report.Add("PASS: "+state+" dashboard preserves bounds while Settings cycles system scaling and 100/125/150/175/200/225/250/300%; dialog stays on screen.");
        }
    }
}
