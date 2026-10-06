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
        static void Check(bool good,string message) { if(!good) throw new Exception(message); }
        static IEnumerable<Control> Children(Control parent) { foreach(Control child in parent.Controls) { yield return child; foreach(var c in Children(child)) yield return c; } }
        internal static void CheckText(ScaledForm form) {
            float scale=form.ContentDpi/96f;
            foreach(var c in Children(form)) {
                if(!c.Visible) continue;
                if(!(c is Button || c is Label || c is CheckBox || c is TextBox || c is ComboBox || c is NumericUpDown)) continue;
                int line=UiText.LineHeight(c,c.Font);
                Check(Math.Abs(c.Font.Size-form.FontPixels*scale)<.1,"Native font was scaled twice: "+c.GetType().Name+" "+c.Text);
                var label=c as Label;
                int needed=label!=null?UiText.WrappedHeight(c,c.ClientSize.Width):line;
                Check(c.ClientSize.Height>=needed,"Text height clips in "+c.GetType().Name+" at "+form.ContentDpi+": "+c.Text+" ("+c.ClientSize.Height+" < "+needed+")");
            }
            var grid=Children(form).OfType<ProcessGrid>().FirstOrDefault();
            if(grid!=null) {
                Check(grid.Rows.Cast<DataGridViewRow>().All(row=>row.Height>=UiText.LineHeight(grid,grid.DefaultCellStyle.Font)+grid.DefaultCellStyle.Padding.Vertical+2),"Process rows clip text");
                foreach(DataGridViewColumn col in grid.Columns) {
                    var header=(SortHeaderCell)col.HeaderCell; var rect=grid.GetCellDisplayRectangle(col.Index,-1,false);
                    var label=header.LabelBounds(rect); var arrow=header.ArrowBounds(rect);
                    var measured=UiText.Measure(grid,col.HeaderText,grid.ColumnHeadersDefaultCellStyle.Font);
                    Check(label.Height>=measured.Height && label.Width>=measured.Width,"Header text clips: "+col.HeaderText);
                    Check(!label.IntersectsWith(arrow) && rect.Contains(arrow),"Header arrow overlaps text: "+col.HeaderText);
                }
            }
        }
        internal static void CheckSettingsFlow(SettingsDialog settings) {
            int page=settings.ActivePage;
            try {
                for(int i=0;i<3;i++) {
                    settings.ShowPage(i); CheckText(settings);
                    var content=Children(settings).First(c=>c.Controls.OfType<SettingsChoice>().Any());
                    var items=content.Controls.Cast<Control>().Where(c=>c.Visible && c.Height>0).ToArray();
                    for(int a=0;a<items.Length;a++) for(int b=a+1;b<items.Length;b++) Check(!items[a].Bounds.IntersectsWith(items[b].Bounds),"Settings fields overlap: "+items[a].Text+" / "+items[b].Text);
                    foreach(var c in Children(settings).Where(c=>c.Visible && (c is Button || c is CheckBox))) {
                        int reserve=(int)Math.Ceiling((c is CheckBox?26:12)*settings.ContentDpi/96f);
                        if(c is Button) Check(UiText.Measure(c,c.Text,c.Font).Width<=c.ClientSize.Width-reserve,"Settings button clips horizontally: "+c.Text);
                        else Check(UiText.WrappedHeight(c,c.ClientSize.Width-reserve)<=c.ClientSize.Height,"Settings checkbox clips words: "+c.Text);
                    }
                    Check(!((ScrollableControl)content.Parent).HorizontalScroll.Visible,"Settings requires horizontal scrolling: "+content.Bounds+" / "+content.Parent.ClientSize+" / "+settings.ClientSize);
                }
                var number=Children(settings).OfType<SettingsNumber>().Single();
                Check(number.Entry.ClientSize.Height>=UiText.LineHeight(number.Entry,number.Entry.Font)+3,"Numeric entry lacks room for descenders");
                var save=Children(settings).OfType<Button>().Single(b=>b.Text=="Save settings");
                Check(settings.ClientRectangle.Contains(settings.RectangleToClient(save.RectangleToScreen(save.ClientRectangle))),"Save button is outside the window");
            } finally { settings.ShowPage(page); }
        }
        static void CheckMenus(SettingsDialog settings,string folder,string suffix) {
            settings.ShowPage(0);
            foreach(var choice in Children(settings).OfType<SettingsChoice>()) {
                choice.Menu.Opacity=0; choice.PerformClick(); Application.DoEvents();
                foreach(ToolStripItem item in choice.Menu.Items) {
                    var size=UiText.Measure(choice.Menu,item.Text,item.Font);
                    Check(Math.Abs(item.Font.Size-settings.Font.Size)<.1 && item.Font.Unit==GraphicsUnit.Pixel,"Popup text uses a different scale from Settings");
                    Check(item.Height>=size.Height+2 && item.Width>=size.Width+24,"Settings menu clips option: "+item.Text);
                }
                using(var bitmap=new Bitmap(choice.Menu.Width,choice.Menu.Height)) { choice.Menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(folder,choice.AccessibleName.Replace(" ","-")+"-"+suffix+".png")); }
                choice.Menu.Close();
            }
        }
        static void CheckSort(Dashboard form,List<Consumer> apps) {
            var grid=Children(form).OfType<ProcessGrid>().Single();
            foreach(DataGridViewColumn column in grid.Columns) for(int click=0;click<2;click++) {
                grid.ClickHeader(column.Index);
                bool descending=column.HeaderCell.SortGlyphDirection==SortOrder.Descending;
                Check(column.HeaderCell.SortGlyphDirection!=SortOrder.None,"Sort indicator missing after click");
                Func<Consumer,IComparable> key=c=>column.Name=="name"?(IComparable)c.Name:column.Name=="share"?c.PowerShare??-1:column.Name=="watts"?c.GpuWatts??-1:column.Name=="cpu"?c.Cpu:column.Name=="gpu"?c.Gpu??-1:column.Name=="memory"?c.MemoryMb:c.DiskMb??-1;
                string[] expected=(descending?apps.OrderByDescending(key):apps.OrderBy(key)).Select(c=>c.Name).ToArray();
                form.UpdateActivity(apps,0); // Real refresh path must retain sort, font and row sizes.
                Check(grid.Rows.Cast<DataGridViewRow>().Select(r=>(string)r.Tag).SequenceEqual(expected),"Incorrect sort after refresh: "+column.Name);
                Check(column.HeaderCell.SortGlyphDirection==(descending?SortOrder.Descending:SortOrder.Ascending),"Refresh lost sort direction");
                Check(grid.Columns.Cast<DataGridViewColumn>().Count(c=>c.HeaderCell.SortGlyphDirection!=SortOrder.None)==1,"Multiple active sort headers");
                using(var image=new Bitmap(grid.Width,grid.Height)) {
                    grid.DrawToBitmap(image,new Rectangle(Point.Empty,grid.Size));
                    var arrow=((SortHeaderCell)column.HeaderCell).ArrowBounds(grid.GetCellDisplayRectangle(column.Index,-1,false));
                    int top=0,bottom=0,muted=0;
                    for(int y=arrow.Top;y<arrow.Bottom;y++) for(int x=arrow.Left;x<arrow.Right;x++) {
                        int color=image.GetPixel(x,y).ToArgb();
                        if(color==Palette.Mint.ToArgb()) { if(y<arrow.Top+arrow.Height/2) top++; else bottom++; }
                        if(color==Palette.Muted.ToArgb()) muted++;
                    }
                    Check((descending?bottom>0 && top==0:top>0 && bottom==0) && muted>0,"Painted up/down arrow missing or wrong: "+column.Name);
                }
            }
            grid.ClickHeader(grid.Columns["share"].Index); // Restore the default descending load view.
        }
        public static void Run(string folder) {
            Directory.CreateDirectory(folder);
            var history=new History(Path.Combine(folder,"fixture-"+Guid.NewGuid().ToString("N"))); var samples=new List<Sample>();
            for(int i=0;i<60;i++) samples.Add(new Sample { Time=DateTime.UtcNow.AddSeconds((i-59)*10),Source="Battery",State="Discharging",Percent=80-i*.3,Volts=15.2,Watts=-110-10*Math.Sin(i*.12) });
            history.Append(samples);
            var apps=new List<Consumer> { new Consumer { Name="Space adventure",Cpu=22,Gpu=93,MemoryMb=6120,DiskMb=3 },new Consumer { Name="Browser - ÁÉÅ gjpqy",Cpu=4,Gpu=3,MemoryMb=1200,DiskMb=.4 },new Consumer { Name="Music",Cpu=.5,Gpu=0,MemoryMb=115,DiskMb=.1 },new Consumer { Name="PowerPal",Cpu=.1,Gpu=.1,MemoryMb=50,DiskMb=0 } };
            PowerEstimates.Apply(apps,PowerFrame.From(new[]{samples.Last()}),DateTime.UtcNow);
            var report=new List<string>();
            DisplayScaling.Set(125); // Exercise a manual override on the actual Windows DPI at launch.
            using(var form=new Dashboard(history)) using(var settings=new SettingsDialog(new Preferences { StorageFolder=Path.Combine(folder,"preferences"),DisplayScalePercent=125 })) {
                form.Opacity=0; form.ShowInTaskbar=false; form.Show(); settings.Opacity=0; settings.ShowInTaskbar=false; settings.Show(form);
                if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(form.Handle),new IntPtr(-4))) throw new Exception("Window did not opt into PerMonitorV2");
                report.Add("Actual Windows DPI: "+form.DeviceDpi+"; PerMonitorV2 confirmed; target: "+AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName);
                apps[0].GpuWatts=72.5; apps[0].GpuEnergyWh=.412; apps[1].GpuWatts=2.3;
                var sensors=new GpuPowerReport { Time=DateTime.UtcNow,Status="Fixture sensor" };
                sensors.Devices.Add(new GpuPowerReading { Id="fixture",Name="NVIDIA fixture",Watts=85,ObservedWh=.52,UnassignedWatts=10.2,Method="nvml-energy" });
                form.UpdateSensors(sensors); form.UpdateLive(new List<Sample>{samples.Last()},null); form.SeedPreview(apps);
                for(int j=0;j<20;j++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                CheckText(form); CheckText(settings); CheckSettingsFlow(settings);
                report.Add("PASS: initial 125% override on actual monitor; native text fits without double scaling");
                int[] firstWidths=null;
                foreach(string theme in new[]{"Light","Dark"}) foreach(int dpi in new[]{96,120,144,168,192,216,240,288,120,96}) {
                    Theme.Set(theme);
                    // Drive the saved scale through Settings. Directly overriding a
                    // form would race queued native DPI events that reapply the setting.
                    Children(settings).OfType<SettingsChoice>().Single(c=>c.AccessibleName=="Display scaling").Menu.Items[Array.IndexOf(DisplayScaling.Options,dpi*100/96)].PerformClick();
                    form.ClientSize=new Size(1180*dpi/96,900*dpi/96); Application.DoEvents();
                    Check(form.ContentDpi==dpi && settings.ContentDpi==dpi,"DPI notification changed the selected scale: "+dpi+" -> "+form.ContentDpi+" / "+settings.ContentDpi);
                    if(!form.ContentFits || !settings.ContentFits) throw new Exception("Controls overflow at "+dpi+" DPI");
                    CheckText(form); CheckText(settings); CheckSettingsFlow(settings); CheckSort(form,apps); CheckText(form);
                    var filter=Children(form).OfType<TextBox>().Single(c=>c.AccessibleName=="Filter processes by name");
                    filter.Text="ÁÉÅ gjpqy"; CheckText(form);
                    Check(Children(form).OfType<ProcessGrid>().Single().RowCount==1,"Filter no longer matches extended characters"); filter.Clear();
                    if(dpi==96) {
                        var widths=Children(form).OfType<ProcessGrid>().Single().Columns.Cast<DataGridViewColumn>().Select(c=>c.Width).ToArray();
                        if(firstWidths==null) firstWidths=widths; else Check(widths.Zip(firstWidths,(a,b)=>Math.Abs(a-b)<=1).All(v=>v),"Column widths drift after DPI/theme round trip: "+string.Join(",",firstWidths)+" -> "+string.Join(",",widths)+"; canvas parent "+form.ClientSize);
                    }
                    form.SaveCanvas(Path.Combine(folder,"dashboard-"+theme+"-"+dpi+".png"));
                    Check(settings.Width<=Math.Ceiling(440*dpi/96.0)+80 && settings.Height<=Math.Ceiling(300*dpi/96.0)+80,"Settings enlarged beyond its compact design");
                    for(int page=0;page<3;page++) { settings.ShowPage(page); settings.SaveDialog(Path.Combine(folder,"settings-"+page+"-"+theme+"-"+dpi+".png")); }
                    CheckMenus(settings,folder,theme+"-"+dpi);
                    report.Add("PASS: "+theme+" "+dpi+" DPI: text fits; compact Settings pages/menus do not clip or overlap; all 7 headers sort both ways, survive refresh and paint correct arrows; stable column widths");
                }
                form.SetUpdateBusy(true); form.UpdateRelease("Checking GitHub releases..."); form.SetContentDpi(96); form.ClientSize=new Size(1180,900);
                form.SaveCanvas(Path.Combine(folder,"update-checking.png"));
                form.UpdateRelease("Downloading PowerPal "+ReleaseUpdater.Current.ToString(3)+"..."); form.SaveCanvas(Path.Combine(folder,"update-downloading.png"));
                form.UpdateRelease("v"+ReleaseUpdater.Current.ToString(3)+" - up to date with GitHub releases"); form.SetUpdateBusy(false);
                // Return to a prior DPI to catch cumulative/double scaling.
                form.SetContentDpi(96); if(!form.ContentFits) throw new Exception("DPI round-trip layout failed");
            }
            File.WriteAllLines(Path.Combine(folder,"ui-result.txt"),report);
        }
    }
}
