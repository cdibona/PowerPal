using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerPal {
    // Compare rendered text with Windows' own message HFONT, not Font.Size or
    // an offscreen bitmap: those both passed when the live text was doubled.
    internal static class FontTests {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct LogFont {
            public int Height,Width,Escapement,Orientation,Weight;
            public byte Italic,Underline,StrikeOut,CharacterSet,OutPrecision,ClipPrecision,Quality,PitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FaceName;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct NonClientMetrics {
            public uint Size;
            public int BorderWidth,ScrollWidth,ScrollHeight,CaptionWidth,CaptionHeight;
            public LogFont Caption;
            public int SmallCaptionWidth,SmallCaptionHeight;
            public LogFont SmallCaption;
            public int MenuWidth,MenuHeight;
            public LogFont Menu,Status,Message;
            public int PaddedBorderWidth;
        }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfoForDpi(uint action,uint size,ref NonClientMetrics metrics,uint flags,uint dpi);
        [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateFontIndirect(ref LogFont font);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int DrawText(IntPtr dc,string text,int length,ref Rect rect,uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
        [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern int GetObjectW(IntPtr obj,int size,out LogFont font);
        const string Sample="ÁÉÅ gjpqy Check updates 0123456789";
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        static IEnumerable<Control> Children(Control p) { foreach(Control c in p.Controls) { yield return c; foreach(var child in Children(c)) yield return child; } }
        static void Pump() { for(int i=0;i<8;i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); } }
        static Size WindowsText(Control control,int dpi,out LogFont system) {
            var metrics=new NonClientMetrics { Size=(uint)Marshal.SizeOf(typeof(NonClientMetrics)) };
            Check(SystemParametersInfoForDpi(0x29,metrics.Size,ref metrics,0,(uint)dpi),"Cannot read Windows message font");
            system=metrics.Message;
            IntPtr font=CreateFontIndirect(ref system); Check(font!=IntPtr.Zero,"Cannot create Windows message font");
            try {
                using(var g=control.CreateGraphics()) {
                    IntPtr dc=g.GetHdc(); IntPtr previous=SelectObject(dc,font);
                    try { var rect=new Rect(); Check(DrawText(dc,Sample,Sample.Length,ref rect,0x400|0x20|0x800)>0,"Cannot measure Windows message font"); return new Size(rect.Right,rect.Bottom); }
                    finally { SelectObject(dc,previous); g.ReleaseHdc(dc); }
                }
            } finally { DeleteObject(font); }
        }
        internal static void CheckSystemText(ScaledForm form,List<string> report) {
            LogFont system; Size expected=WindowsText(form,form.ContentDpi,out system);
            foreach(var c in Children(form).Where(c=>c.Visible && (c is Button || c is Label || c is TextBox || c is DataGridView))) {
                Size actual=UiText.Measure(c,Sample,c.Font);
                Check(Math.Abs(actual.Height-expected.Height)<=1 && Math.Abs(actual.Width-expected.Width)<=1,"Live text differs from Windows message font at "+form.ContentDpi+" DPI, HFONT "+system.Height+": "+c.GetType().Name+" "+c.Text+" "+actual+" vs "+expected);
                if(c is TextBox) {
                    LogFont native; Check(GetObjectW(SendMessage(c.Handle,0x31,IntPtr.Zero,IntPtr.Zero),Marshal.SizeOf(typeof(LogFont)),out native)>0,"Missing text-entry HFONT");
                    Check(native.Height==system.Height && native.FaceName==system.FaceName,"Text entry differs from Windows message font: "+native.Height+" vs "+system.Height);
                }
                var grid=c as DataGridView;
                if(grid!=null) Check(UiText.Measure(grid,Sample,grid.DefaultCellStyle.Font)==actual,"Table body differs from Windows message font");
            }
            report.Add("PASS: "+form.GetType().Name+" "+form.ContentDpi+" DPI matches Windows "+system.FaceName+" HFONT "+system.Height+" px; text sample "+expected);
        }
        public static void Run(string folder) {
            Directory.CreateDirectory(folder);
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-4)),"DPI awareness must be active before any drawing/font initialization");
            // Deliberately touch GDI+ before constructing the first Control, as the
            // production tray icon does. A config-only DPI opt-in is too late.
            using(var bitmap=new Bitmap(32,32)) using(var g=Graphics.FromImage(bitmap)) g.DrawString(Sample,SystemFonts.MessageBoxFont,Brushes.Black,0,0);
            DisplayScaling.Set(0);
            var report=new List<string>();
            using(var form=new Dashboard(new History(Path.Combine(folder,"History")))) {
                form.Opacity=0; form.ShowInTaskbar=false; var handle=form.Handle; Pump(); form.Open(); Pump();
                using(var settings=new SettingsDialog(new Preferences { StorageFolder=Path.Combine(folder,"Preferences") })) {
                    settings.Opacity=0; settings.Show(form); Pump();
                    report.Add("Actual Windows display: "+form.DeviceDpi+" DPI; background handle created before opening dashboard");
                    foreach(int percent in new[]{0,100,125,150,175,200,225,250,300,100,0}) {
                        DisplayScaling.Set(percent); Pump();
                        CheckSystemText(form,report);
                        for(int page=0;page<3;page++) { settings.ShowPage(page); CheckSystemText(settings,report); }
                    }
                    UiTests.CheckText(form); UiTests.CheckSettingsFlow(settings);
                }
            }
            File.WriteAllLines(Path.Combine(folder,"font-result.txt"),report);
        }
    }
}
