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
        public static Color Bg,Card,Muted,Text,Mint,Purple,Amber,Line,Selection;
        static Palette() { Use(false); }
        public static void Use(bool light) {
            Bg=light?Color.FromArgb(242,246,251):Color.FromArgb(13,19,31);
            Card=light?Color.White:Color.FromArgb(23,32,48);
            Muted=light?Color.FromArgb(83,103,125):Color.FromArgb(151,168,190);
            Text=light?Color.FromArgb(25,40,60):Color.FromArgb(235,241,249);
            Mint=light?Color.FromArgb(0,118,87):Color.FromArgb(108,239,190);
            Purple=light?Color.FromArgb(107,66,190):Color.FromArgb(178,160,255);
            Amber=light?Color.FromArgb(152,91,0):Color.FromArgb(255,204,112);
            Line=light?Color.FromArgb(211,221,232):Color.FromArgb(45,60,80);
            Selection=light?Color.FromArgb(209,237,227):Color.FromArgb(39,87,78);
        }
    }
    internal sealed class Dashboard : ScaledForm {
        readonly History history;
        readonly ActivityHistory activityHistory;
        readonly PowerEventLog eventLog;
        readonly SensorHistory sensorHistory;
        GpuPowerReport sensors=new GpuPowerReport();
        readonly ContextMenuStrip exportMenu=new ContextMenuStrip();
        readonly DataGridView processGrid=new DataGridView();
        readonly ListBox eventList=new ListBox();
        readonly TextBox search=new TextBox();
        readonly ToolTip eventTip=new ToolTip();
        readonly ActivityTrends trends=new ActivityTrends();
        string selectedProcess,sortColumn="share";
        bool descending=true,rebuilding;
        FormWindowState restoreState=FormWindowState.Normal;
        SettingsDialog activeSettings;
        List<Sample> current=new List<Sample>(), rows=new List<Sample>();
        List<Consumer> consumers=new List<Consumer>();
        string recording="Starting recorder...", activity="Warming up process counters...", updates="Checking release channel...";
        DateTime? lastSaved,activitySaved;
        string activityLogError;
        int hours=24, loadGeneration;
        bool recordingError,updateBusy;
        readonly Button[] ranges;
        readonly Button export,settings,update;
        int appLimit=20;
        public event Action SettingsRequested,UpdateRequested,ThemeRequested;
        public event Action HiddenToTray;
        public Dashboard(History h,ActivityHistory a=null,PowerEventLog events=null,SensorHistory sensorLog=null) {
            history=h; activityHistory=a; eventLog=events; sensorHistory=sensorLog; Text="PowerPal v"+ReleaseUpdater.Current.ToString(3)+" - your power, in focus"; StartPosition=FormStartPosition.CenterScreen;
            BackColor=Palette.Bg; ForeColor=Palette.Text; DoubleBuffered=true; Content.BackColor=Palette.Bg; Content.Paint+=PaintDashboard;
            Icon=Brand.MakeIcon(Palette.Mint);
            Content.MouseClick+=delegate(object sender,MouseEventArgs e) { if(new Rectangle(Px(24),Px(24),Px(44),Px(48)).Contains(e.Location) && ThemeRequested!=null) ThemeRequested(); };
            Content.MouseMove+=delegate(object sender,MouseEventArgs e) { bool over=new Rectangle(Px(24),Px(24),Px(44),Px(48)).Contains(e.Location); Content.Cursor=over?Cursors.Hand:Cursors.Default; string tip=over?"Theme: "+Theme.Mode+". Click for "+Theme.Next+".":""; if(eventTip.GetToolTip(Content)!=tip) eventTip.SetToolTip(Content,tip); };
            Theme.Changed+=ApplyTheme;
            exportMenu.Renderer=new ThemeRenderer();
            ranges=new[] { MakeButton("1 hour",delegate { SetRange(1); }),MakeButton("24 hours",delegate { SetRange(24); }),MakeButton("7 days",delegate { SetRange(168); }) };
            exportMenu.Items.Add("Battery history",null,delegate { Export(false); });
            exportMenu.Items.Add("App activity history",null,delegate { Export(true); }).Enabled=activityHistory!=null;
            exportMenu.Items.Add("GPU sensor history",null,delegate { ExportSensors(); }).Enabled=sensorHistory!=null;
            export=MakeButton("Export CSV",delegate { exportMenu.Show(export,new Point(0,export.Height)); }); settings=MakeButton("Settings",delegate { if(SettingsRequested!=null) SettingsRequested(); });
            update=MakeButton("Check updates",delegate { if(!updateBusy && UpdateRequested!=null) UpdateRequested(); });
            Content.Controls.AddRange(ranges); Content.Controls.AddRange(new Control[]{export,settings,update});
            ConfigureProcessGrid();
            search.BackColor=Palette.Bg; search.ForeColor=Palette.Text; search.BorderStyle=BorderStyle.FixedSingle; search.AccessibleName="Filter processes by name"; search.TextChanged+=delegate { RebuildProcesses(); };
            eventList.BackColor=Palette.Card; eventList.ForeColor=Palette.Text; eventList.BorderStyle=BorderStyle.None; eventList.DrawMode=DrawMode.OwnerDrawFixed; eventList.IntegralHeight=false; eventList.AccessibleName="Power and battery event log";
            eventList.DrawItem+=DrawEvent;
            eventList.SelectedIndexChanged+=delegate { var entry=eventList.SelectedItem as PowerEvent; eventTip.SetToolTip(eventList,entry==null?"":entry.Detail); };
            Content.Controls.AddRange(new Control[]{processGrid,eventList,search});
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; HideToTray(); } };
            Resize += delegate { if(WindowState==FormWindowState.Minimized) { HideToTray(); return; } restoreState=WindowState; PositionButtons(); Content.Invalidate(); };
            VisibleChanged += delegate { if(Visible) { RebuildProcesses(); RefreshHistory(); } };
            ContentLayout+=PositionButtons; ApplyTheme(); InitializeContent(new Size(1180,900),new Size(1080,850));
        }
        void ApplyTheme() {
            Theme.PaintControls(this);
            processGrid.BackgroundColor=Palette.Card; processGrid.GridColor=Palette.Line;
            processGrid.ColumnHeadersDefaultCellStyle.BackColor=Palette.Bg; processGrid.ColumnHeadersDefaultCellStyle.ForeColor=Palette.Muted; processGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Palette.Bg; processGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Palette.Text;
            processGrid.DefaultCellStyle.BackColor=Palette.Card; processGrid.DefaultCellStyle.ForeColor=Palette.Text;
            processGrid.DefaultCellStyle.SelectionBackColor=Palette.Selection; processGrid.DefaultCellStyle.SelectionForeColor=Palette.Text;
            PositionButtons(); SetUpdateBusy(updateBusy); Content.Invalidate(true);
        }
        Button MakeButton(string text,Action action) {
            var b=new Button { Text=text,FlatStyle=FlatStyle.Flat,BackColor=Palette.Card,ForeColor=Palette.Text,Cursor=Cursors.Hand,TabStop=true };
            b.FlatAppearance.BorderColor=Palette.Line; b.FlatAppearance.MouseOverBackColor=Palette.Selection; b.Click+=delegate { action(); }; return b;
        }
        void PositionButtons() {
            if(ranges==null || export==null) return; float d=ScaleFactor; int width=(int)(Content.ClientSize.Width/d);
            for(int i=0;i<ranges.Length;i++) { ranges[i].SetBounds((int)((28+i*94)*d),(int)(262*d),(int)(86*d),(int)(32*d)); ranges[i].BackColor=hours==(i==0?1:i==1?24:168)?Palette.Selection:Palette.Card; }
            export.SetBounds((int)((width-414)*d),(int)(28*d),(int)(120*d),(int)(34*d));
            update.SetBounds((int)((width-284)*d),(int)(28*d),(int)(144*d),(int)(34*d));
            settings.SetBounds((int)((width-130)*d),(int)(28*d),(int)(102*d),(int)(34*d));
            float left=(width-72)*0.64f,rx=44+left,right=width-left-72;
            processGrid.SetBounds((int)(48*d),(int)(665*d),(int)((left-40)*d),Math.Max(100,Content.ClientSize.Height-(int)(739*d)));
            search.SetBounds((int)((left-156)*d),(int)(612*d),(int)(164*d),(int)(26*d));
            eventList.SetBounds((int)((rx+20)*d),(int)(650*d),(int)((right-40)*d),Math.Max(115,Content.ClientSize.Height-(int)(724*d))); eventList.ItemHeight=(int)(54*d);
            processGrid.ColumnHeadersHeight=Px(30); processGrid.RowTemplate.Height=Px(28); foreach(DataGridViewRow row in processGrid.Rows) row.Height=Px(28);
            foreach(DataGridViewColumn column in processGrid.Columns) column.MinimumWidth=Px(column.Name=="name"?110:column.Name=="memory"?50:column.Name=="io"?80:68);
        }
        void ConfigureProcessGrid() {
            processGrid.ReadOnly=true; processGrid.AllowUserToAddRows=false; processGrid.AllowUserToDeleteRows=false; processGrid.AllowUserToResizeRows=false; processGrid.RowHeadersVisible=false; processGrid.MultiSelect=false;
            processGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; processGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; processGrid.BackgroundColor=Palette.Card; processGrid.BorderStyle=BorderStyle.None; processGrid.GridColor=Palette.Line;
            processGrid.EnableHeadersVisualStyles=false; processGrid.ColumnHeadersDefaultCellStyle.BackColor=Palette.Bg; processGrid.ColumnHeadersDefaultCellStyle.ForeColor=Palette.Muted; processGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Palette.Bg; processGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Palette.Text; processGrid.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.False; processGrid.ColumnHeadersHeight=30; processGrid.RowTemplate.Height=28;
            processGrid.DefaultCellStyle.BackColor=Palette.Card; processGrid.DefaultCellStyle.ForeColor=Palette.Text; processGrid.DefaultCellStyle.SelectionBackColor=Palette.Selection; processGrid.DefaultCellStyle.SelectionForeColor=Palette.Text; processGrid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
            foreach(var col in new[]{new[]{"name","App","24"},new[]{"share","Load %","14"},new[]{"watts","GPU W~","14"},new[]{"cpu","CPU %","12"},new[]{"gpu","GPU %","12"},new[]{"memory","MB","11"},new[]{"io","I/O MB/s","13"}}) {
                int i=processGrid.Columns.Add(col[0],col[1]); processGrid.Columns[i].FillWeight=int.Parse(col[2]); processGrid.Columns[i].SortMode=DataGridViewColumnSortMode.Programmatic;
            }
            processGrid.Columns["share"].ToolTipText="Activity share: CPU + GPU activity normalized across readable apps. This is NOT a share of system power.";
            processGrid.Columns["watts"].ToolTipText="Estimated GPU watts only, based on matched NVIDIA board power and activity over the sensor interval. Works on AC or battery. Excludes CPU, display and other components. -- means no current estimate.";
            processGrid.Columns["gpu"].ToolTipText="Busiest GPU engine for this app group (WDDM). Utilization, not watts.";
            processGrid.ColumnHeaderMouseClick+=delegate(object sender,DataGridViewCellMouseEventArgs e) { string key=processGrid.Columns[e.ColumnIndex].Name; descending=key==sortColumn?!descending:key!="name"; sortColumn=key; RebuildProcesses(); };
            processGrid.SelectionChanged+=delegate { if(!rebuilding && processGrid.SelectedRows.Count>0) { selectedProcess=(string)processGrid.SelectedRows[0].Tag; Content.Invalidate(); } };
            processGrid.AccessibleName="Power users - select a process for its resource history";
        }
        void RebuildProcesses() {
            if(IsDisposed) return;
            var filtered=consumers.Where(c=>c.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0);
            Func<Consumer,IComparable> key=c=>sortColumn=="name"?(IComparable)c.Name:sortColumn=="share"?c.PowerShare??-1:sortColumn=="watts"?c.GpuWatts??-1:sortColumn=="gpu"?c.Gpu??-1:sortColumn=="memory"?c.MemoryMb:sortColumn=="io"?c.DiskMb??-1:sortColumn=="count"?c.Processes:c.Cpu;
            var sorted=(descending?filtered.OrderByDescending(key):filtered.OrderBy(key)).ToList();
            if(selectedProcess==null && sorted.Count>0) selectedProcess=sorted[0].Name;
            int scroll=processGrid.FirstDisplayedScrollingRowIndex; rebuilding=true;
            try {
                processGrid.Rows.Clear();
                foreach(var c in sorted) { int i=processGrid.Rows.Add(c.Name,c.PowerShare.HasValue?c.PowerShare.Value.ToString("0.0"):"--",c.GpuWatts.HasValue?c.GpuWatts.Value.ToString("0.0"):"--",c.Cpu.ToString("0.0"),c.Gpu.HasValue?c.Gpu.Value.ToString("0.0"):"--",c.MemoryMb.ToString("0"),c.DiskMb.HasValue?c.DiskMb.Value.ToString("0.0"):"--"); processGrid.Rows[i].Tag=c.Name; }
                processGrid.ClearSelection(); foreach(DataGridViewRow row in processGrid.Rows) if((string)row.Tag==selectedProcess) row.Selected=true;
                if(scroll>=0 && processGrid.Rows.Count>scroll) processGrid.FirstDisplayedScrollingRowIndex=scroll;
                foreach(DataGridViewColumn col in processGrid.Columns) col.HeaderCell.SortGlyphDirection=col.Name==sortColumn?(descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
            } finally { rebuilding=false; }
            Content.Invalidate();
        }
        void DrawEvent(object sender,DrawItemEventArgs e) {
            if(e.Index<0) return; var entry=(PowerEvent)eventList.Items[e.Index]; float d=ScaleFactor;
            using(var b=new SolidBrush((e.State&DrawItemState.Selected)!=0?Palette.Selection:Palette.Card)) e.Graphics.FillRectangle(b,e.Bounds);
            using(var f=new Font("Segoe UI",12*d,FontStyle.Bold,GraphicsUnit.Pixel)) using(var muted=new Font("Segoe UI",11*d,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var ink=new SolidBrush(Palette.Text)) using(var sub=new SolidBrush(Palette.Muted)) using(var format=new StringFormat { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap }) {
                e.Graphics.DrawString(entry.Time.ToLocalTime().ToString("HH:mm:ss")+"  "+entry.Title,f,ink,new RectangleF(e.Bounds.X+3,e.Bounds.Y+4,e.Bounds.Width-10,22*d),format);
                e.Graphics.DrawString(entry.Detail,muted,sub,new RectangleF(e.Bounds.X+3,e.Bounds.Y+26*d,e.Bounds.Width-10,23*d),format);
            }
        }
        public DialogResult OpenSettings(Preferences preferences,Action<bool> applyStartup=null) {
            if(activeSettings!=null) { activeSettings.Activate(); return DialogResult.None; }
            Open(); using(var dialog=new SettingsDialog(preferences,applyStartup)) {
                activeSettings=dialog; try { return dialog.ShowDialog(this); } finally { activeSettings=null; }
            }
        }
        public void Open() { if(WindowState==FormWindowState.Minimized) WindowState=restoreState; if(!Visible) Show(); Activate(); }
        void HideToTray() { Hide(); if(HiddenToTray!=null) HiddenToTray(); }
        void SetRange(int value) { hours=value; PositionButtons(); RefreshHistory(); }
        DateTime Since() { return DateTime.UtcNow.AddHours(-hours); }
        public void UpdateLive(List<Sample> samples,string error) {
            current=samples; recordingError=error!=null;
            if(error==null) { lastSaved=DateTime.Now; recording="RECORDING  /  every 10 seconds"; } else recording=error;
            Content.Invalidate(); if(Visible) RefreshHistory();
        }
        public void UpdateActivity(List<Consumer> items,int skipped,int limit=20) { appLimit=limit; consumers=items; trends.Observe(DateTime.UtcNow,items); activity=items.Count+" app groups / live 2s / auto-log top "+appLimit+" every 10s"+(skipped>0 ? " / "+skipped+" inaccessible" : ""); if(Visible) RebuildProcesses(); }
        public void UpdateSensors(GpuPowerReport report) { sensors=report; Content.Invalidate(); }
        public void UpdateActivityLog(DateTime? saved,string error) { activitySaved=saved; activityLogError=error; Content.Invalidate(); }
        public void SetUpdateBusy(bool busy) { updateBusy=busy; update.Text=busy?(updates.StartsWith("Downloading")?"Downloading...":"Checking..."):"Check updates"; update.BackColor=busy?Palette.Selection:Palette.Card; update.ForeColor=busy?Palette.Mint:Palette.Text; update.Cursor=busy?Cursors.WaitCursor:Cursors.Hand; Content.Invalidate(); }
        public void UpdateRelease(string text) { updates=text; if(updateBusy) SetUpdateBusy(true); eventTip.SetToolTip(update,text); Content.Invalidate(); }
        public void RefreshStatus() { Content.Invalidate(); }
        internal void SeedPreview(List<Consumer> items) {
            for(int i=0;i<100;i++) trends.Observe(DateTime.UtcNow.AddSeconds((i-100)*2),items.Select(c=>new Consumer { Name=c.Name,Cpu=Math.Max(0,c.Cpu*(0.65+Math.Sin(i*0.25)*0.35)),MemoryMb=c.MemoryMb*(0.85+i/1000.0),Gpu=c.Gpu,PowerShare=c.PowerShare,GpuWatts=c.GpuWatts,DiskMb=c.DiskMb.HasValue?(double?)(c.DiskMb.Value*(0.6+Math.Cos(i*0.3)*0.4)):null }));
            UpdateActivity(items,3); activity="Preview data / select an app to inspect CPU, GPU and estimated power"; activitySaved=DateTime.UtcNow; updates="v"+ReleaseUpdater.Current.ToString(3)+" / automatic updates enabled";
            foreach(var entry in new[]{new PowerEvent { Time=DateTime.UtcNow,Title="Charging started",Detail="External power / 70% / 15.20 V / +22.0 W" },new PowerEvent { Time=DateTime.UtcNow.AddSeconds(-1),Title="External power connected",Detail="Battery-1 / 70% / 15.20 V" },new PowerEvent { Time=DateTime.UtcNow.AddMinutes(-4),Title="Battery level 69%",Detail="Running on battery / discharging / -12.0 W" },new PowerEvent { Time=DateTime.UtcNow.AddMinutes(-8),Title="Recording started",Detail="Battery and app activity logs started" }}) eventList.Items.Add(entry);
        }
        async void RefreshHistory() {
            int generation=++loadGeneration; DateTime since=Since();
            try { var loaded=await Task.Run(()=>history.Load(since)); var events=eventLog==null?null:await Task.Run(()=>eventLog.Recent(since)); if(IsDisposed || generation!=loadGeneration) return; rows=loaded;
                if(events!=null) { int top=eventList.TopIndex; eventList.BeginUpdate(); eventList.Items.Clear(); foreach(var entry in events) eventList.Items.Add(entry); if(top>0 && top<eventList.Items.Count) eventList.TopIndex=top; eventList.EndUpdate(); } Content.Invalidate(); }
            catch(Exception ex) { if(!IsDisposed) { recording="History unavailable: "+ex.Message; recordingError=true; Content.Invalidate(); } }
        }
        void PaintDashboard(object sender,PaintEventArgs e) {
            var g=e.Graphics; g.ScaleTransform(ScaleFactor,ScaleFactor); g.SmoothingMode=SmoothingMode.AntiAlias;
            float w=Content.ClientSize.Width/ScaleFactor,h=Content.ClientSize.Height/ScaleFactor;
            Brand.DrawBolt(g,new RectangleF(28,28,32,36),Palette.Mint);
            TextAt(g,"PowerPal",72,24,25,Palette.Text,true); TextAt(g,"v"+ReleaseUpdater.Current.ToString(3),240,39,11,Palette.Muted,false,125); TextAt(g,"A little clarity for every watt.",74,65,10,Palette.Muted,false);
            bool stale=lastSaved.HasValue && (DateTime.Now-lastSaved.Value).TotalSeconds>25;
            using(var b=new SolidBrush(recordingError || stale || activityLogError!=null ? Palette.Amber : Palette.Mint)) g.FillEllipse(b,w-15-12,78,7,7);
            TextAt(g,activityLogError!=null?"APP ACTIVITY LOG ERROR - battery recording continues":stale?"Reading delayed - last saved "+lastSaved.Value.ToString("HH:mm:ss"):recording, w-420,74,9,recordingError||stale||activityLogError!=null?Palette.Amber:Palette.Mint,false,390);
            TextAt(g,updates,w-590,94,9,updateBusy?Palette.Mint:updates.StartsWith("Update check failed")?Palette.Amber:Palette.Muted,false,560);
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
            TextAt(g,sensors.Summary(DateTime.UtcNow),330,255,11,Palette.Mint,true,w-360);
            TextAt(g,sensors.Detail(DateTime.UtcNow),330,278,8,Palette.Muted,false,w-360);
            float left=(w-72)*0.64f,right=w-left-72;
            Round(g,new RectangleF(28,310,left,268),Palette.Card);
            TextAt(g,"Your power story",48,326,15,Palette.Text,true); TextAt(g,"Battery %",48,355,9,Palette.Mint,false); TextAt(g,"Watts  (+ charge / - draw)",160,355,9,Palette.Amber,false);
            Plot(g,new RectangleF(68,385,left-65,55),true); Plot(g,new RectangleF(68,480,left-65,55),false);
            TextAt(g,rows.Count==0?"History starts with the first saved sample.":rows.Count+" samples / gaps mean no measurement",48,554,9,Palette.Muted,false,left-32);
            float rx=44+left; Round(g,new RectangleF(rx,310,right,268),Palette.Card);
            DrawDeepDive(g,new RectangleF(rx+20,326,right-40,230));
            float panelHeight=Math.Max(170,h-656);
            Round(g,new RectangleF(28,594,left,panelHeight),Palette.Card);
            TextAt(g,"Power users",48,610,15,Palette.Text,true);
            TextAt(g,"Filter",left-205,615,9,Palette.Muted,false,48);
            TextAt(g,"Auto-log top "+appLimit+" every 10s. GPU W~ is estimated GPU power only.",48,639,9,Palette.Muted,false,left-40);
            Round(g,new RectangleF(rx,594,right,panelHeight),Palette.Card);
            TextAt(g,"Power & battery events",rx+20,610,15,Palette.Text,true);
            TextAt(g,eventList.Items.Count==0?"Waiting for the first event...":"Newest first / scroll for earlier events",rx+20,634,9,Palette.Muted,false,right-40);
            bool activityStale=activitySaved.HasValue && (DateTime.UtcNow-activitySaved.Value).TotalSeconds>25;
            string logState=activityLogError??(activitySaved.HasValue ? "App log saved "+activitySaved.Value.ToLocalTime().ToString("HH:mm:ss")+(activityStale?" (delayed)":"") : "App log warming up");
            TextAt(g,logState,w-350,h-47,8,activityLogError!=null||activityStale?Palette.Amber:Palette.Mint,false,322);
            TextAt(g,activity,28,h-47,8,Palette.Muted,false,w-410); TextAt(g,"v"+ReleaseUpdater.Current.ToString(3)+" / appearance: "+Theme.Mode+" / close or minimize to keep recording",28,h-26,8,Palette.Muted,false,w-56);
        }
        void DrawDeepDive(Graphics g,RectangleF rect) {
            TextAt(g,selectedProcess??"App resource detail",rect.X,rect.Y,15,Palette.Text,true,rect.Width);
            var selected=consumers.FirstOrDefault(c=>c.Name==selectedProcess); var points=trends.Get(selectedProcess);
            TextAt(g,selected==null?"Select an app below":selected.Processes+" process(es) / "+selected.MemoryMb.ToString("0")+" MB / I/O "+(selected.DiskMb.HasValue?selected.DiskMb.Value.ToString("0.0")+" MB/s":"--"),rect.X,rect.Y+28,9,Palette.Muted,false,rect.Width);
            DrawResource(g,rect.X,rect.Y+57,rect.Width,"GPU watts~",selected==null || !selected.GpuWatts.HasValue?"-- W":selected.GpuWatts.Value.ToString("0.0")+" W",points,p=>p.Watts,Palette.Amber,null);
            DrawResource(g,rect.X,rect.Y+111,rect.Width,"CPU",selected==null?"--":selected.Cpu.ToString("0.0")+"%",points,p=>p.Cpu,Palette.Mint,100);
            DrawResource(g,rect.X,rect.Y+165,rect.Width,"GPU",selected==null || !selected.Gpu.HasValue?"--":selected.Gpu.Value.ToString("0.0")+"%",points,p=>p.Gpu,Palette.Purple,100);
            TextAt(g,selected!=null && selected.GpuEnergyWh.HasValue?"GPU energy~ "+selected.GpuEnergyWh.Value.ToString("0.000")+" Wh observed this run":"5 min / GPU power estimated / CPU watts unavailable",rect.X,rect.Y+218,8,Palette.Muted,false,rect.Width);
        }
        static void DrawResource(Graphics g,float x,float y,float width,string title,string value,List<ActivityPoint> points,Func<ActivityPoint,double?> get,Color color,double? fixedMax) {
            TextAt(g,title,x,y,9,Palette.Muted,false,100); TextAt(g,value,x,y+16,11,color,true,100);
            var rect=new RectangleF(x+108,y+2,width-108,34);
            using(var pen=new Pen(Palette.Line)) g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
            double maximum=fixedMax??Math.Max(1,points.Select(p=>get(p)??0).DefaultIfEmpty(1).Max()*1.1);
            DateTime end=DateTime.UtcNow,start=end.AddMinutes(-5); PointF? last=null; DateTime lastTime=DateTime.MinValue;
            using(var pen=new Pen(color,2)) foreach(var p in points) {
                var v=get(p); if(!v.HasValue) { last=null; continue; }
                var point=new PointF(rect.X+(float)((p.Time-start).TotalSeconds/300)*rect.Width,rect.Bottom-(float)Math.Min(1,v.Value/maximum)*rect.Height);
                if(point.X<rect.X) continue;
                if(last.HasValue && (p.Time-lastTime).TotalSeconds<8) g.DrawLine(pen,last.Value,point); else using(var dot=new SolidBrush(color)) g.FillEllipse(dot,point.X-1,point.Y-1,2,2);
                last=point; lastTime=p.Time;
            }
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
        void ExportSensors() {
            if(sensorHistory==null) return;
            using(var dialog=new SaveFileDialog { Filter="CSV files|*.csv",FileName="PowerPal-gpu-sensors.csv" }) {
                if(dialog.ShowDialog()!=DialogResult.OK) return;
                try {
                    string target=Path.GetFullPath(dialog.FileName);
                    if(target.StartsWith(Path.GetFullPath(Preferences.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an export location outside PowerPal's data folders.");
                    File.WriteAllLines(target,sensorHistory.Lines(Since()));
                } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Export failed"); }
            }
        }
        void Export(bool processes) {
            using(var dialog=new SaveFileDialog { Filter="CSV files|*.csv",FileName=processes?"PowerPal-app-activity.csv":"PowerPal-battery-history.csv" }) {
                if(dialog.ShowDialog()!=DialogResult.OK) return;
                try {
                    string target=Path.GetFullPath(dialog.FileName);
                    if(target.StartsWith(Path.GetFullPath(history.Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || activityHistory!=null && target.StartsWith(Path.GetFullPath(activityHistory.Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an export location outside PowerPal's original history folders.");
                    File.WriteAllLines(target,processes?activityHistory.Lines(Since()):new[]{History.Header}.Concat(history.Load(Since()).Select(s=>s.Csv())));
                } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Export failed"); }
            }
        }
        protected override void Dispose(bool disposing) { if(disposing) { Theme.Changed-=ApplyTheme; exportMenu.Dispose(); eventTip.Dispose(); if(Icon!=null) { Icon.Dispose(); Icon=null; } } base.Dispose(disposing); }
    }
}
