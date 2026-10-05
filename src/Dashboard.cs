using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PowerPal {
    internal static class Palette {
        public static readonly Color Bg=Color.FromArgb(13,19,31), Card=Color.FromArgb(23,32,48), Muted=Color.FromArgb(151,168,190), Text=Color.FromArgb(235,241,249), Mint=Color.FromArgb(108,239,190), Purple=Color.FromArgb(178,160,255), Amber=Color.FromArgb(255,204,112), Line=Color.FromArgb(45,60,80);
    }
    internal sealed class Dashboard : Form {
        readonly History history;
        List<Sample> current=new List<Sample>(), rows=new List<Sample>();
        List<Consumer> consumers=new List<Consumer>();
        string recording="Starting recorder...", activity="Warming up process counters...", updates="Checking release channel...";
        DateTime? lastSaved;
        int hours=24, loadGeneration;
        bool recordingError;
        readonly Button[] ranges;
        readonly Button export,settings,update;
        public event Action SettingsRequested,UpdateRequested;
        public event Action HiddenToTray;
        public Dashboard(History h) {
            history=h; Text="PowerPal - your power, in focus"; ClientSize=new Size(1120,800); MinimumSize=new Size(1080,830); StartPosition=FormStartPosition.CenterScreen;
            BackColor=Palette.Bg; ForeColor=Palette.Text; Font=new Font("Segoe UI",13,FontStyle.Regular,GraphicsUnit.Pixel); DoubleBuffered=true; AutoScaleMode=AutoScaleMode.Dpi;
            Icon=Brand.MakeIcon(Palette.Mint);
            ranges=new[] { MakeButton("1 hour",delegate { SetRange(1); }),MakeButton("24 hours",delegate { SetRange(24); }),MakeButton("7 days",delegate { SetRange(168); }) };
            export=MakeButton("Export CSV",Export); settings=MakeButton("Settings",delegate { if(SettingsRequested!=null) SettingsRequested(); });
            update=MakeButton("Check updates",delegate { if(UpdateRequested!=null) UpdateRequested(); });
            Controls.AddRange(ranges); Controls.AddRange(new Control[]{export,settings,update});
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; HideToTray(); } };
            Resize += delegate { if(WindowState==FormWindowState.Minimized) HideToTray(); PositionButtons(); Invalidate(); };
            VisibleChanged += delegate { if(Visible) RefreshHistory(); };
            PositionButtons();
        }
        Button MakeButton(string text,Action action) {
            var b=new Button { Text=text,FlatStyle=FlatStyle.Flat,BackColor=Palette.Card,ForeColor=Palette.Text,Cursor=Cursors.Hand,TabStop=true };
            b.FlatAppearance.BorderColor=Palette.Line; b.FlatAppearance.MouseOverBackColor=Color.FromArgb(40,60,74); b.Click+=delegate { action(); }; return b;
        }
        float ScaleFactor { get { return DeviceDpi/96f; } }
        void PositionButtons() {
            if(ranges==null) return; float d=ScaleFactor; int width=(int)(ClientSize.Width/d);
            for(int i=0;i<ranges.Length;i++) { ranges[i].SetBounds((int)((28+i*94)*d),(int)(262*d),(int)(86*d),(int)(32*d)); ranges[i].BackColor=hours==(i==0?1:i==1?24:168)?Color.FromArgb(39,87,78):Palette.Card; }
            export.SetBounds((int)((width-414)*d),(int)(28*d),(int)(120*d),(int)(34*d));
            update.SetBounds((int)((width-284)*d),(int)(28*d),(int)(144*d),(int)(34*d));
            settings.SetBounds((int)((width-130)*d),(int)(28*d),(int)(102*d),(int)(34*d));
        }
        public void Open() { Show(); WindowState=FormWindowState.Normal; Activate(); }
        void HideToTray() { Hide(); if(HiddenToTray!=null) HiddenToTray(); }
        void SetRange(int value) { hours=value; PositionButtons(); RefreshHistory(); }
        DateTime Since() { return DateTime.UtcNow.AddHours(-hours); }
        public void UpdateLive(List<Sample> samples,string error) {
            current=samples; recordingError=error!=null;
            if(error==null) { lastSaved=DateTime.Now; recording="RECORDING  /  every 10 seconds"; } else recording=error;
            Invalidate(); if(Visible) RefreshHistory();
        }
        public void UpdateActivity(List<Consumer> items,int skipped) { consumers=items; activity="CPU and I/O activity, refreshed every 2s"+(skipped>0 ? "  /  "+skipped+" protected processes unavailable" : ""); Invalidate(); }
        public void UpdateRelease(string text) { updates=text; Invalidate(); }
        public void RefreshStatus() { Invalidate(); }
        internal void SeedPreview(List<Consumer> items) { consumers=items; activity="Preview data - CPU and I/O activity"; updates="v0.2.0  /  automatic updates enabled"; }
        async void RefreshHistory() {
            int generation=++loadGeneration; DateTime since=Since();
            try { var loaded=await Task.Run(()=>history.Load(since)); if(IsDisposed || generation!=loadGeneration) return; rows=loaded; Invalidate(); }
            catch(Exception ex) { if(!IsDisposed) { recording="History unavailable: "+ex.Message; recordingError=true; Invalidate(); } }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.ScaleTransform(ScaleFactor,ScaleFactor); g.SmoothingMode=SmoothingMode.AntiAlias;
            float w=ClientSize.Width/ScaleFactor,h=ClientSize.Height/ScaleFactor;
            Brand.DrawBolt(g,new RectangleF(28,28,32,36),Palette.Mint);
            TextAt(g,"PowerPal",72,24,25,Palette.Text,true); TextAt(g,"A little clarity for every watt.",74,65,10,Palette.Muted,false);
            bool stale=lastSaved.HasValue && (DateTime.Now-lastSaved.Value).TotalSeconds>25;
            using(var b=new SolidBrush(recordingError || stale ? Palette.Amber : Palette.Mint)) g.FillEllipse(b,w-15-12,78,7,7);
            TextAt(g,stale?"Reading delayed - last saved "+lastSaved.Value.ToString("HH:mm:ss"):recording, w-420,74,9,recordingError||stale?Palette.Amber:Palette.Mint,false,390);
            float cw=(w-56-36)/4; var all=current;
            bool has=all.Count>0; var first=has?all[0]:null;
            double? watts=has && all.All(s=>s.Watts.HasValue) ? (double?)all.Sum(s=>s.Watts.Value) : null;
            string power=watts.HasValue ? Math.Abs(watts.Value).ToString("0.0")+" W" : "Unavailable";
            string flow=!watts.HasValue?"Waiting for a hardware reading":watts<0?"Leaving the battery":watts>0?"Going into the battery":"Battery is holding steady";
            Card(g,28,112,cw,126,"POWER SOURCE",has?first.Source:"Detecting...",has?string.Join(" / ",all.Select(s=>s.State).Distinct()):"Reading Windows power status",Palette.Mint);
            Card(g,40+cw,112,cw,126,"BATTERY FLOW",power,flow,Palette.Amber);
            string percent=has && all.Count==1 && first.Percent.HasValue ? first.Percent.Value.ToString("0")+"%" : all.Count>1 ? all.Count+" batteries" : "Unavailable";
            string reserve=has && all.All(s=>s.Wh.HasValue) ? all.Sum(s=>s.Wh.Value).ToString("0.0")+" Wh remaining" : "Capacity not reported";
            Card(g,52+2*cw,112,cw,126,"CHARGE LEFT",percent,reserve,Palette.Purple);
            Card(g,64+3*cw,112,cw,126,"BATTERY VOLTAGE",has && all.Count==1 && first.Volts.HasValue?first.Volts.Value.ToString("0.00")+" V":all.Count>1?"Multiple packs":"Unavailable","Charger input voltage: unavailable",Palette.Mint);
            TextAt(g,"Battery power is net charge / discharge. Total wall draw and per-app watts are not exposed here.",330,270,9,Palette.Muted,false,w-360);
            float left=(w-72)*0.59f,right=w-left-72;
            Round(g,new RectangleF(28,310,left,308),Palette.Card);
            TextAt(g,"Your power story",48,326,15,Palette.Text,true); TextAt(g,"Battery %",48,355,9,Palette.Mint,false); TextAt(g,"Watts  (+ charge / - draw)",160,355,9,Palette.Amber,false);
            Plot(g,new RectangleF(68,387,left-65,74),true); Plot(g,new RectangleF(68,496,left-65,74),false);
            TextAt(g,rows.Count==0?"History starts with the first saved sample.":rows.Count+" samples  /  gaps mean no measurement",48,590,9,Palette.Muted,false,left-32);
            float rx=44+left; Round(g,new RectangleF(rx,310,right,308),Palette.Card);
            TextAt(g,"Who's busy right now?",rx+20,326,15,Palette.Text,true); TextAt(g,"Activity ranking - not measured watts",rx+20,355,9,Palette.Muted,false);
            if(consumers.Count==0) TextAt(g,"Taking two readings to find current activity...",rx+20,407,10,Palette.Muted,false,right-40);
            for(int i=0;i<consumers.Count && i<6;i++) {
                var c=consumers[i]; float y=385+i*36;
                TextAt(g,c.Name,rx+20,y,10,Palette.Text,true,right-155); TextAt(g,c.Cpu.ToString("0.0")+"% CPU",rx+right-105,y,10,Palette.Mint,true,90);
                using(var b=new SolidBrush(Palette.Line)) g.FillRectangle(b,rx+20,y+30,right-40,2);
                using(var b=new SolidBrush(Palette.Mint)) g.FillRectangle(b,rx+20,y+30,(right-40)*(float)Math.Min(c.Cpu/100,1),2);
                TextAt(g,c.MemoryMb.ToString("0")+" MB   /   "+(c.DiskMb.HasValue?c.DiskMb.Value.ToString("0.0")+" MB/s I/O":"I/O unavailable"),rx+20,y+15,8,Palette.Muted,false,right-40);
            }
            Round(g,new RectangleF(28,634,left,Math.Max(96,h-693)),Palette.Card);
            TextAt(g,"Recent power changes",48,650,12,Palette.Text,true);
            var events=new List<Sample>(); string previous=null;
            foreach(var s in rows.GroupBy(s=>s.Time).Select(group=>group.First())) { string state=s.Source+" / "+s.State; if(previous!=state) { events.Add(s); previous=state; } }
            string eventText=events.Count==0 ? "Plug in or unplug power to start a timeline." : string.Join("     ",events.Skip(Math.Max(0,events.Count-2)).Select(s=>s.Time.ToLocalTime().ToString("HH:mm")+"  "+s.Source+" / "+s.State));
            TextAt(g,eventText,48,680,10,Palette.Muted,false,left-40);
            Round(g,new RectangleF(rx,634,right,Math.Max(96,h-693)),Palette.Card);
            TextAt(g,"Quietly keeping track",rx+20,650,12,Palette.Text,true);
            TextAt(g,lastSaved.HasValue?"Last saved "+lastSaved.Value.ToString("HH:mm:ss")+"  /  close or minimize to tray":"Waiting for the first saved sample",rx+20,680,9,Palette.Muted,false,right-40);
            TextAt(g,activity,28,h-47,8,Palette.Muted,false,w-56); TextAt(g,updates,28,h-26,8,Palette.Muted,false,w-56);
        }
        void Plot(Graphics g,RectangleF rect,bool percent) {
            var known=rows.Where(s=>(percent?s.Percent:s.Watts).HasValue).ToList();
            double min=percent?0:Math.Min(-5,known.Count==0?0:known.Min(s=>s.Watts.Value)),max=percent?100:Math.Max(5,known.Count==0?0:known.Max(s=>s.Watts.Value));
            using(var p=new Pen(Palette.Line)) { g.DrawLine(p,rect.Left,rect.Top,rect.Right,rect.Top); g.DrawLine(p,rect.Left,rect.Bottom,rect.Right,rect.Bottom); }
            TextAt(g,max.ToString("0"),rect.Left-33,rect.Top-7,8,Palette.Muted,false); TextAt(g,min.ToString("0"),rect.Left-33,rect.Bottom-7,8,Palette.Muted,false);
            if(known.Count==0) { TextAt(g,"No readings yet",rect.Left+12,rect.Top+25,10,Palette.Muted,false); return; }
            DateTime start=rows.Min(s=>s.Time),end=rows.Max(s=>s.Time); double duration=Math.Max(60,(end-start).TotalSeconds);
            foreach(var group in rows.GroupBy(s=>s.Device)) {
                PointF? last=null; DateTime lastTime=DateTime.MinValue;
                using(var pen=new Pen(percent?Palette.Mint:Palette.Amber,2)) foreach(var s in group.OrderBy(s=>s.Time)) {
                    double? value=percent?s.Percent:s.Watts; if(!value.HasValue) { last=null; continue; }
                    var point=new PointF(rect.Left+(float)((s.Time-start).TotalSeconds/duration)*rect.Width,rect.Bottom-(float)((value.Value-min)/(max-min))*rect.Height);
                    if(last.HasValue && (s.Time-lastTime).TotalSeconds<=30) g.DrawLine(pen,last.Value,point);
                    else using(var b=new SolidBrush(pen.Color)) g.FillEllipse(b,point.X-2,point.Y-2,4,4);
                    last=point; lastTime=s.Time;
                }
            }
            if(!percent) { TextAt(g,start.ToLocalTime().ToString("MM-dd HH:mm"),rect.Left,rect.Bottom+4,8,Palette.Muted,false); TextAt(g,end.ToLocalTime().ToString("MM-dd HH:mm"),rect.Right-100,rect.Bottom+4,8,Palette.Muted,false); }
        }
        static void Card(Graphics g,float x,float y,float w,float h,string title,string value,string note,Color accent) {
            Round(g,new RectangleF(x,y,w,h),Palette.Card); TextAt(g,title,x+18,y+16,9,accent,true,w-36); TextAt(g,value,x+18,y+44,value.Length>10?19:25,Palette.Text,true,w-36); TextAt(g,note,x+18,y+96,9,Palette.Muted,false,w-36);
        }
        internal static void TextAt(Graphics g,string text,float x,float y,float size,Color color,bool bold,float width=1000) {
            using(var f=new Font("Segoe UI",size*96f/72f,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel)) using(var b=new SolidBrush(color)) using(var format=new StringFormat { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap }) g.DrawString(text,f,b,new RectangleF(x,y,Math.Max(1,width),Math.Max(32,size*1.8f)),format);
        }
        static void Round(Graphics g,RectangleF r,Color color) {
            const float d=18; using(var p=new GraphicsPath()) { p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); using(var b=new SolidBrush(color)) g.FillPath(b,p); }
        }
        void Export() {
            using(var dialog=new SaveFileDialog { Filter="CSV files|*.csv",FileName="PowerPal-history.csv" }) {
                if(dialog.ShowDialog()!=DialogResult.OK) return;
                try { File.WriteAllLines(dialog.FileName,new[]{History.Header}.Concat(history.Load(Since()).Select(s=>s.Csv()))); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Export failed"); }
            }
        }
        protected override void Dispose(bool disposing) { if(disposing && Icon!=null) { Icon.Dispose(); Icon=null; } base.Dispose(disposing); }
    }
}
