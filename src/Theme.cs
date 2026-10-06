using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerPal {
    internal static class Theme {
        public static string Mode { get; private set; }
        public static bool Light { get; private set; }
        public static event Action Changed;
        public static string Normalize(string mode) { return mode=="Light" || mode=="Dark"?mode:"Auto"; }
        public static bool Resolve(string mode,bool windowsLight) { return mode=="Light" || mode!="Dark" && windowsLight; }
        static bool WindowsLight() {
            try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key==null || Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))!=0; } catch { return true; }
        }
        public static void Set(string mode) {
            Mode=Normalize(mode); Light=Resolve(Mode,WindowsLight()); Palette.Use(Light);
            if(Changed!=null) Changed();
        }
        public static void RefreshSystem() { if(Mode=="Auto") Set("Auto"); }
        public static string Next { get { return Mode=="Auto"?"Light":Mode=="Light"?"Dark":"Auto"; } }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        public static void PaintFrame(Form form) {
            if(!form.IsHandleCreated) return;
            int dark=Light?0:1; DwmSetWindowAttribute(form.Handle,20,ref dark,4);
            int caption=ColorTranslator.ToWin32(Palette.Bg),text=ColorTranslator.ToWin32(Palette.Text);
            DwmSetWindowAttribute(form.Handle,35,ref caption,4); DwmSetWindowAttribute(form.Handle,36,ref text,4);
        }
        public static void PaintControls(Control parent) {
            var form=parent as Form; if(form!=null) PaintFrame(form);
            parent.BackColor=Palette.Bg; parent.ForeColor=Palette.Text;
            foreach(Control c in parent.Controls) {
                PaintControls(c);
                if(c is Button || c is ListBox) c.BackColor=Palette.Card;
                var button=c as Button; if(button!=null) { button.FlatAppearance.BorderColor=Palette.Line; button.FlatAppearance.MouseOverBackColor=Palette.Selection; }
            }
        }
    }
    internal sealed class ThemeRenderer : ToolStripProfessionalRenderer {
        sealed class Colors : ProfessionalColorTable {
            public override Color ToolStripDropDownBackground { get { return Palette.Card; } }
            public override Color MenuItemSelected { get { return Palette.Selection; } }
            public override Color MenuItemBorder { get { return Palette.Line; } }
            public override Color ImageMarginGradientBegin { get { return Palette.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Palette.Card; } }
            public override Color ImageMarginGradientEnd { get { return Palette.Card; } }
        }
        public ThemeRenderer() : base(new Colors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor=e.Item.Enabled?Palette.Text:Palette.Muted; base.OnRenderItemText(e); }
    }
}
