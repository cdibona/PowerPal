using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PowerPal {
    // A single scale for native controls and custom drawing. Scroll on small
    // desktops instead of silently shrinking the user's chosen Windows scale.
    internal class ScaledForm : Form {
        sealed class CanvasPanel : Panel { public CanvasPanel() { DoubleBuffered=true; } }
        readonly Panel viewport=new Panel { Dock=DockStyle.Fill,AutoScroll=true };
        protected readonly Panel Content=new CanvasPanel();
        Size designSize,minimumContent;
        bool ready,layingOut,initialSizeApplied;
        protected bool FitContentOnScaleChange;
        readonly Dictionary<int,Font> fonts=new Dictionary<int,Font>();
        protected float ScaleFactor { get; private set; }
        protected event Action ContentLayout;
        protected ScaledForm() {
            AutoScaleMode=AutoScaleMode.None; ScaleFactor=1;
            Controls.Add(viewport); viewport.Controls.Add(Content);
            viewport.SizeChanged+=delegate { LayoutCanvas(); };
            DisplayScaling.Changed+=ApplyDisplayScaling;
        }
        protected void InitializeContent(Size design,Size minimum) {
            designSize=design; minimumContent=minimum; ready=true;
            SetContentDpi(DisplayScaling.ResolveDpi(DeviceDpi,DisplayScaling.Percent)); FitToDisplay(true);
        }
        protected int Px(float logical) { return (int)Math.Round(logical*ScaleFactor); }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e); Theme.PaintFrame(this);
            if(ready) { SetContentDpi(DisplayScaling.ResolveDpi(DeviceDpi,DisplayScaling.Percent)); FitToDisplay(!initialSizeApplied); initialSizeApplied=true; }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); FitToDisplay(false); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) {
            // Disable framework scaling: it would multiply our explicit bounds twice.
            e.Cancel=true; base.OnDpiChanged(e);
            ApplyMonitorDpi(e.DeviceDpiNew,e.SuggestedRectangle);
        }
        internal void ApplyMonitorDpi(int dpi,Rectangle suggested) {
            SetContentDpi(DisplayScaling.ResolveDpi(dpi,DisplayScaling.Percent));
            if(WindowState==FormWindowState.Normal) {
                if(DisplayScaling.Percent!=0) suggested.Size=Size;
                Bounds=suggested;
            }
            FitToDisplay(false);
        }
        void ApplyDisplayScaling() {
            if(!ready || IsDisposed) return;
            SetContentDpi(DisplayScaling.ResolveDpi(DeviceDpi,DisplayScaling.Percent));
            FitToDisplay(FitContentOnScaleChange);
        }
        void FitToDisplay(bool initial) {
            Rectangle work=Screen.FromControl(this).WorkingArea;
            // Content scrolls independently. Changing its scale must not enlarge a
            // maximized window's restore bounds or force a normal window to grow.
            MinimumSize=new Size(Math.Min(Math.Min(640,minimumContent.Width),work.Width),Math.Min(Math.Min(440,minimumContent.Height),work.Height));
            if(WindowState!=FormWindowState.Normal) { LayoutCanvas(); return; }
            Size wanted=initial?SizeFromClientSize(new Size(Px(designSize.Width),Px(designSize.Height))):Size;
            Size=new Size(Math.Min(wanted.Width,work.Width),Math.Min(wanted.Height,work.Height));
            if(IsHandleCreated) Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
            LayoutCanvas();
        }
        internal void SetContentDpi(int dpi) {
            ScaleFactor=Math.Max(96,dpi)/96f;
            Font scaled; if(!fonts.TryGetValue(dpi,out scaled)) { scaled=new Font("Segoe UI",13*ScaleFactor,FontStyle.Regular,GraphicsUnit.Pixel); fonts[dpi]=scaled; }
            Font=scaled;
            LayoutCanvas(); Content.Invalidate(true);
        }
        void LayoutCanvas() {
            if(!ready || layingOut) return; layingOut=true;
            try {
                Content.Size=new Size(Math.Max(Px(minimumContent.Width),viewport.ClientSize.Width),Math.Max(Px(minimumContent.Height),viewport.ClientSize.Height));
                if(ContentLayout!=null) ContentLayout();
                Content.Invalidate();
            } finally { layingOut=false; }
        }
        internal void SaveCanvas(string path) {
            using(var bitmap=new Bitmap(Content.Width,Content.Height)) { Content.DrawToBitmap(bitmap,new Rectangle(Point.Empty,Content.Size)); bitmap.Save(path); }
        }
        internal int ContentDpi { get { return (int)Math.Round(ScaleFactor*96); } }
        internal bool ContentFits { get { return Content.Controls.Count==0 || AllControlsFit(); } }
        bool AllControlsFit() { foreach(Control c in Content.Controls) if(c.Left<0 || c.Top<0 || c.Right>Content.Width || c.Bottom>Content.Height) return false; return true; }
        protected override void Dispose(bool disposing) { if(disposing) DisplayScaling.Changed-=ApplyDisplayScaling; base.Dispose(disposing); if(disposing) { foreach(var font in fonts.Values) font.Dispose(); fonts.Clear(); } }
    }
}
