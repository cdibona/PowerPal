using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PowerPal {
    // A standard button/menu avoids the independently DPI-scaled edit/list portions
    // of a native ComboBox. ToolStrip measures every option before painting it.
    internal sealed class SettingsChoice : Button {
        internal readonly ContextMenuStrip Menu=new ContextMenuStrip();
        public SettingsChoice() {
            FlatStyle=FlatStyle.Flat; TextAlign=ContentAlignment.MiddleLeft;
            Menu.Renderer=new ThemeRenderer();
        }
        public void Add(string text,Action action) { Menu.Items.Add(text,null,delegate { action(); }); }
        public void Select(int index,string label) {
            Text=label+"  ▾";
            for(int i=0;i<Menu.Items.Count;i++) ((ToolStripMenuItem)Menu.Items[i]).Checked=i==index;
        }
        protected override void OnClick(EventArgs e) {
            base.OnClick(e); Menu.Font=Font; Menu.BackColor=Palette.Card; Menu.ForeColor=Palette.Text;
            foreach(ToolStripItem item in Menu.Items) item.Font=Font;
            Menu.Show(this,new Point(0,Height));
        }
        protected override void Dispose(bool disposing) { if(disposing) Menu.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class SettingsNumber : Panel {
        internal readonly TextBox Entry=new TextBox { BorderStyle=BorderStyle.None,AutoSize=false,TextAlign=HorizontalAlignment.Center,Text="20",AccessibleName="Number of top apps to record" };
        readonly Button down=new Button { Text="−",AccessibleName="Log fewer apps",FlatStyle=FlatStyle.Flat };
        readonly Button up=new Button { Text="+",AccessibleName="Log more apps",FlatStyle=FlatStyle.Flat };
        float scale=1;
        public SettingsNumber() {
            Controls.AddRange(new Control[]{Entry,down,up});
            down.Click+=delegate { Step(-1); }; up.Click+=delegate { Step(1); };
            Entry.KeyPress+=delegate(object sender,KeyPressEventArgs e) { if(!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled=true; };
            Entry.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Up || e.KeyCode==Keys.Down) { Step(e.KeyCode==Keys.Up?1:-1); e.SuppressKeyPress=true; } };
            Entry.GotFocus+=delegate { Entry.SelectAll(); Invalidate(); }; Entry.LostFocus+=delegate { Invalidate(); };
            SizeChanged+=delegate { LayoutEntry(); };
        }
        internal bool TryValue(out int value) { return int.TryParse(Entry.Text,NumberStyles.None,CultureInfo.InvariantCulture,out value) && value>=1 && value<=100; }
        internal int Value { get { int value; return TryValue(out value)?value:20; } set { Entry.Text=Math.Max(1,Math.Min(100,value)).ToString(CultureInfo.InvariantCulture); } }
        void Step(int delta) { Value=Value+delta; }
        internal void ApplyScale(float value) { scale=value; LayoutEntry(); }
        void LayoutEntry() {
            int button=(int)Math.Round(30*scale),pad=(int)Math.Ceiling(5*scale),line=UiText.LineHeight(Entry,Entry.Font)+(int)Math.Ceiling(4*scale);
            down.SetBounds(0,0,button,Height); up.SetBounds(Math.Max(0,Width-button),0,button,Height);
            Entry.SetBounds(button+pad,Math.Max(0,(Height-line)/2),Math.Max(1,Width-2*(button+pad)),line);
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var p=new Pen(Entry.Focused?Palette.Mint:Palette.Line)) e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1); }
    }
    internal sealed class SettingsDialog : ScaledForm {
        readonly Preferences preferences;
        readonly Panel tabs=new Panel { Dock=DockStyle.Top },footer=new Panel { Dock=DockStyle.Bottom };
        readonly Button[] pages;
        readonly Control[][] groups;
        readonly SettingsChoice theme=new SettingsChoice { AccessibleName="Appearance" },scale=new SettingsChoice { AccessibleName="Display scaling" };
        readonly SettingsNumber appLimit=new SettingsNumber();
        readonly CheckBox startup=new CheckBox { Text="Start recording when I sign in" },automatic=new CheckBox { Text="Install updates automatically" },sensors=new CheckBox { Text="Read GPU power sensors",AccessibleName="GPU power sensors" };
        readonly Label themeLabel=Note("Theme"),themeHint=Note("Theme and scale changes save immediately."),scaleLabel=Note("Display scale"),scaleHint=Note(""),appLabel=Note("Top apps to log"),appHint=Note("History saves every 10 seconds, including in the tray."),sensorHint=Note("Requires NVIDIA. App GPU watts are estimates; CPU watts are unavailable."),updateHint=Note("Checks GitHub at startup and every six hours.\nUpdates install when PowerPal is in the tray."),version=Note("Installed version: "+ReleaseUpdater.Current.ToString(3)),error=Note("");
        readonly Button save=ActionButton("Save settings"),close=ActionButton("Close");
        int activePage;
        internal int ActivePage { get { return activePage; } }
        static Label Note(string text) { return new Label { Text=text,UseCompatibleTextRendering=false,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=false }; }
        static Button ActionButton(string text) { return new Button { Text=text,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand }; }
        public SettingsDialog(Preferences value,Action<bool> applyStartup=null) {
            preferences=value; FitContentOnScaleChange=true; FlexibleContentWidth=true; ShowInTaskbar=false;
            Text="PowerPal settings - v"+ReleaseUpdater.Current.ToString(3); StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            startup.Checked=Preferences.Startup; automatic.Checked=preferences.AutoUpdate; sensors.Checked=preferences.GpuSensors; appLimit.Value=preferences.AppLimit;
            pages=new[]{ActionButton("Appearance"),ActionButton("Recording"),ActionButton("Updates")};
            for(int i=0;i<pages.Length;i++) { int page=i; pages[i].Click+=delegate { ShowPage(page); }; pages[i].AccessibleName=pages[i].Text+" settings"; }
            tabs.Controls.AddRange(pages); footer.Controls.AddRange(new Control[]{save,close}); Controls.Add(tabs); Controls.Add(footer);
            groups=new[]{new Control[]{themeLabel,theme,themeHint,scaleLabel,scale,scaleHint},new Control[]{startup,appLabel,appLimit,appHint,sensors,sensorHint,error},new Control[]{automatic,updateHint,version}};
            foreach(var group in groups) Content.Controls.AddRange(group);
            foreach(string mode in new[]{"Auto","Light","Dark"}) {
                string selected=mode; theme.Add(mode=="Auto"?"Follow Windows":mode,delegate {
                    string prior=preferences.ThemeMode; preferences.ThemeMode=selected;
                    try { preferences.Save(); Theme.Set(selected); } catch(Exception ex) { preferences.ThemeMode=prior; MessageBox.Show(this,ex.Message,"Could not save appearance"); }
                });
            }
            foreach(int percent in DisplayScaling.Options) {
                int selected=percent; scale.Add(percent==0?"Follow Windows":DisplayScaling.Label(percent),delegate {
                    int prior=preferences.DisplayScalePercent; preferences.DisplayScalePercent=selected;
                    try { preferences.Save(); DisplayScaling.Set(selected); } catch(Exception ex) { preferences.DisplayScalePercent=prior; MessageBox.Show(this,ex.Message,"Could not save display scaling"); }
                });
            }
            save.Click+=delegate {
                int count; if(!appLimit.TryValue(out count)) { error.Text="Enter a number from 1 to 100."; ShowPage(1); appLimit.Entry.Focus(); return; }
                try { if(applyStartup!=null) applyStartup(startup.Checked); else Preferences.Startup=startup.Checked; preferences.AutoUpdate=automatic.Checked; preferences.TopAppCount=count; preferences.GpuSensors=sensors.Checked; preferences.Save(); DialogResult=DialogResult.OK; Close(); }
                catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not save settings"); }
            };
            close.Click+=delegate { DialogResult=DialogResult.Cancel; Close(); }; AcceptButton=save; CancelButton=close;
            ContentLayout+=LayoutSettings; Theme.Changed+=ApplyTheme;
            ShowPage(0); ApplyTheme(); InitializeContent(new Size(440,300),new Size(440,220));
        }
        internal void ShowPage(int index) {
            activePage=index;
            for(int i=0;i<groups.Length;i++) foreach(Control c in groups[i]) c.Visible=i==index;
            RefreshContentLayout(); PaintSelection();
        }
        void PaintSelection() { for(int i=0;i<pages.Length;i++) { pages[i].BackColor=i==activePage?Palette.Selection:Palette.Bg; pages[i].ForeColor=i==activePage?Palette.Mint:Palette.Text; } }
        void LayoutSettings() {
            int line=UiText.LineHeight(Content,Font),field=Math.Max(Px(30),line+Px(10)),margin=Px(16),gap=Px(8);
            tabs.Height=field+2*Px(10); footer.Height=field+2*Px(12);
            int tabWidth=Math.Max(1,(tabs.ClientSize.Width-2*margin-Px(8))/3);
            for(int i=0;i<pages.Length;i++) pages[i].SetBounds(margin+i*(tabWidth+Px(4)),Px(10),tabWidth,field);
            save.SetBounds(footer.Width-margin-Px(120),Px(12),Px(120),field); close.SetBounds(save.Left-gap-Px(76),Px(12),Px(76),field);
            int width=Math.Max(1,Content.Width-2*margin),y=Px(12);
            Action<Label> note=c=> { c.SetBounds(margin,y,width,UiText.WrappedHeight(c,width)+Px(4)); y=c.Bottom+gap; };
            Action<CheckBox> check=c=> { c.SetBounds(margin,y,width,UiText.WrappedHeight(c,Math.Max(1,width-Px(26)))+Px(8)); y=c.Bottom+gap; };
            Action<Label,Control> row=(label,control)=> {
                int labelWidth=Px(112);
                if(width<Px(350)) { label.SetBounds(margin,y,width,line+Px(4)); y=label.Bottom+Px(4); control.SetBounds(margin,y,width,field); }
                else { label.SetBounds(margin,y,labelWidth,field); control.SetBounds(margin+labelWidth+gap,y,width-labelWidth-gap,field); }
                y=control.Bottom+gap;
            };
            int windows=(int)Math.Round(DeviceDpi*100/96.0),chosen=DisplayScaling.Percent;
            theme.Select(Theme.Mode=="Light"?1:Theme.Mode=="Dark"?2:0,Theme.Mode=="Light"?"Light":Theme.Mode=="Dark"?"Dark":"Follow Windows");
            scale.Select(Array.IndexOf(DisplayScaling.Options,chosen),chosen==0?"Follow Windows ("+windows+"%)":DisplayScaling.Label(chosen));
            scaleHint.Text="Windows: "+windows+"%  /  PowerPal: "+(int)Math.Round(ScaleFactor*100)+"%\nManual values replace the Windows scale.";
            if(activePage==0) { row(themeLabel,theme); note(themeHint); y+=gap; row(scaleLabel,scale); note(scaleHint); }
            else if(activePage==1) { check(startup); row(appLabel,appLimit); appLimit.Width=Math.Min(appLimit.Width,Px(144)); appLimit.ApplyScale(ScaleFactor); note(appHint); check(sensors); note(sensorHint); if(error.Text.Length>0) note(error); else error.SetBounds(margin,y,width,0); }
            else { check(automatic); note(updateHint); y+=gap; note(version); }
            SetScrollHeight(y+Px(8)); PaintSelection();
        }
        void ApplyTheme() { Theme.PaintControls(this); theme.BackColor=scale.BackColor=Palette.Card; error.ForeColor=Palette.Amber; PaintSelection(); RefreshContentLayout(); Content.Invalidate(true); }
        internal void SaveDialog(string path) { using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(path); } }
        protected override void Dispose(bool disposing) { if(disposing) Theme.Changed-=ApplyTheme; base.Dispose(disposing); }
    }
}
