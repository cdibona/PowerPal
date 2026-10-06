using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerPal {
    internal sealed class Preferences {
        public bool AutoUpdate=true;
        public string ThemeMode="Auto";
        public int DisplayScalePercent=0;
        public int TopAppCount=20;
        public bool GpuSensors=true;
        internal string StorageFolder;
        [ScriptIgnore] public int AppLimit { get { return Math.Max(1,Math.Min(100,TopAppCount)); } }
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PowerPal"); } }
        public static Preferences Load() { return LoadFrom(Root); }
        internal static Preferences LoadFrom(string folder) {
            Preferences loaded; try { loaded=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(folder,"settings.json"))) ?? new Preferences(); } catch { loaded=new Preferences(); }
            loaded.StorageFolder=folder; loaded.DisplayScalePercent=DisplayScaling.Normalize(loaded.DisplayScalePercent); loaded.TopAppCount=loaded.AppLimit; return loaded;
        }
        public void Save() { string folder=StorageFolder??Root; Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder,"settings.json"),new JavaScriptSerializer().Serialize(this)); }
        public static bool Startup {
            get { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key!=null && key.GetValue("PowerPal")!=null; }
            set { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(value) key.SetValue("PowerPal","\""+Application.ExecutablePath+"\" --background"); else key.DeleteValue("PowerPal",false); } }
        }
    }
    internal sealed class ReleaseAsset {
        public Version Version; public string Url,Digest,Name; public long Size;
    }
    internal sealed class ReleaseUpdater {
        public const string Api="https://api.github.com/repos/cdibona/PowerPal/";
        public static readonly Version Current=Assembly.GetExecutingAssembly().GetName().Version;
        readonly Action<string> status;
        public ReleaseUpdater(Action<string> statusCallback) { status=statusCallback; }
        internal static ReleaseAsset Parse(string json) {
            var release=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if(release==null || (bool)release["draft"] || (bool)release["prerelease"]) return null;
            string tag=(string)release["tag_name"]; Version version;
            if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+(\.\d+)?$") || !Version.TryParse(tag.TrimStart('v'),out version)) return null;
            version=new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
            string name="PowerPal-Setup-"+version.ToString(3)+"-win-x64.exe";
            foreach(var item in (System.Collections.IEnumerable)release["assets"]) {
                var asset=(Dictionary<string,object>)item;
                if((string)asset["name"]!=name) continue;
                string digest=asset.ContainsKey("digest")?asset["digest"] as string:null;
                string url=(string)asset["url"]; Uri parsed;
                if(digest==null || !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$") || !Uri.TryCreate(url,UriKind.Absolute,out parsed) || !IsApiAsset(parsed)) throw new InvalidDataException("Release installer has no valid GitHub SHA-256 digest or asset URL.");
                return new ReleaseAsset { Version=version,Name=name,Url=url,Digest=digest.Substring(7),Size=Convert.ToInt64(asset["size"]) };
            }
            throw new InvalidDataException("This release does not contain the Windows x64 installer.");
        }
        static bool IsApiAsset(Uri uri) { return uri.Scheme=="https" && uri.Host=="api.github.com" && uri.IsDefaultPort && Regex.IsMatch(uri.AbsolutePath,@"^/repos/cdibona/PowerPal/releases/assets/\d+$"); }
        internal static bool ValidDownload(Uri uri) { return IsApiAsset(uri) || uri.Scheme=="https" && uri.IsDefaultPort && (uri.Host=="release-assets.githubusercontent.com" || uri.Host=="objects.githubusercontent.com"); }
        internal static bool Verify(string file,string digest,long size) {
            if(new FileInfo(file).Length!=size) return false;
            using(var sha=SHA256.Create()) using(var stream=File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(digest,StringComparison.OrdinalIgnoreCase);
        }
        public async Task<string> Check(bool verifyPublicRelease=false) {
            status("Checking GitHub releases...");
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using(var handler=new HttpClientHandler { AllowAutoRedirect=false }) using(var client=new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(3) }) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerPal/"+Current.ToString(3));
                    string json;
                    var latest=await Latest(client);
                    using(var response=latest) {
                        if(response.StatusCode==HttpStatusCode.NotFound) { status("No public release is available yet. Recording continues."); return null; }
                        if(response.StatusCode==HttpStatusCode.Unauthorized || response.StatusCode==HttpStatusCode.Forbidden) { status("GitHub is unavailable or rate limited. Try again later; recording continues."); return null; }
                        response.EnsureSuccessStatusCode(); json=await response.Content.ReadAsStringAsync();
                    }
                    var asset=Parse(json); if(asset==null || asset.Version<=(verifyPublicRelease?new Version(0,0,0,0):Current)) { status("v"+Current.ToString(3)+" - up to date with GitHub releases"); return null; }
                    if(asset.Size<=0 || asset.Size>200*1024*1024) throw new InvalidDataException("Installer size is outside the supported limit.");
                    string dir=Path.Combine(Preferences.Root,"Updates",asset.Version.ToString(3)); Directory.CreateDirectory(dir);
                    string destination=Path.Combine(dir,asset.Name),partial=destination+".download";
                    status("Downloading PowerPal "+asset.Version.ToString(3)+"...");
                    try {
                        var uri=new Uri(asset.Url); bool downloaded=false;
                        for(int redirects=0;redirects<5;redirects++) {
                            if(!ValidDownload(uri)) throw new InvalidDataException("Release download redirected to an unsupported host.");
                            using(var request=Request(uri,true)) using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead)) {
                                if((int)response.StatusCode>=300 && (int)response.StatusCode<400 && response.Headers.Location!=null) { uri=new Uri(uri,response.Headers.Location); continue; }
                                response.EnsureSuccessStatusCode();
                                using(var source=await response.Content.ReadAsStreamAsync()) using(var target=File.Create(partial)) {
                                    var buffer=new byte[65536]; long total=0; int read;
                                    while((read=await source.ReadAsync(buffer,0,buffer.Length))>0) { total+=read; if(total>asset.Size) throw new InvalidDataException("Installer exceeds expected size."); await target.WriteAsync(buffer,0,read); }
                                }
                                downloaded=true; break;
                            }
                        }
                        if(!downloaded || !Verify(partial,asset.Digest,asset.Size)) throw new InvalidDataException("Installer checksum verification failed.");
                        if(File.Exists(destination)) File.Delete(destination); File.Move(partial,destination);
                        status("Update "+asset.Version.ToString(3)+" verified - installs when you minimize or close to tray"); return destination;
                    } finally { if(File.Exists(partial)) File.Delete(partial); }
                }
            } catch(Exception ex) { status("Update check failed (recording continues): "+ex.Message); return null; }
        }
        static async Task<HttpResponseMessage> Latest(HttpClient client) {
            using(var request=Request(new Uri(Api+"releases/latest"),false)) return await client.SendAsync(request);
        }
        static HttpRequestMessage Request(Uri uri,bool binary) {
            var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.Accept.ParseAdd(binary?"application/octet-stream":"application/vnd.github+json");
            return request;
        }
    }
    internal sealed class SettingsDialog : ScaledForm {
        public SettingsDialog(Preferences preferences,Action<bool> applyStartup=null) {
            FitContentOnScaleChange=true; ShowInTaskbar=false;
            Text="PowerPal settings - v"+ReleaseUpdater.Current.ToString(3); StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            var startup=new CheckBox { Text="Start recording in the tray when I sign in",Checked=Preferences.Startup,Bounds=new System.Drawing.Rectangle(22,22,510,26) };
            var automatic=new CheckBox { Text="Automatically install updates from GitHub Releases",Checked=preferences.AutoUpdate,Bounds=new System.Drawing.Rectangle(22,62,510,26) };
            var info=new Label { Text="PowerPal checks the public release at startup and every six hours.\nUpdates install while the dashboard is in the tray. No login needed.",Bounds=new System.Drawing.Rectangle(22,100,510,48) };
            var themeLabel=new Label { Text="Appearance",Bounds=new System.Drawing.Rectangle(22,173,120,26) };
            var theme=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new System.Drawing.Rectangle(160,170,260,30),AccessibleName="Appearance" };
            theme.Items.AddRange(new object[]{"Auto (follow Windows)","Light","Dark"});
            theme.SelectedIndex=Theme.Mode=="Light"?1:Theme.Mode=="Dark"?2:0;
            theme.SelectedIndexChanged+=delegate { preferences.ThemeMode=theme.SelectedIndex==1?"Light":theme.SelectedIndex==2?"Dark":"Auto"; Theme.Set(preferences.ThemeMode); try { preferences.Save(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not save appearance"); } };
            var hint=new Label { Text="You can also click the dashboard lightning bolt to cycle themes.",Bounds=new System.Drawing.Rectangle(22,218,510,38) };
            var scaleLabel=new Label { Text="Display scaling",Bounds=new System.Drawing.Rectangle(22,269,120,26) };
            var scale=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new System.Drawing.Rectangle(160,266,372,30),AccessibleName="Display scaling" };
            scale.Items.AddRange(DisplayScaling.Options.Select(p=>(object)DisplayScaling.Label(p)).ToArray());
            scale.SelectedIndex=Array.IndexOf(DisplayScaling.Options,DisplayScaling.Normalize(preferences.DisplayScalePercent));
            scale.SelectedIndexChanged+=delegate {
                int chosen=DisplayScaling.Options[scale.SelectedIndex],prior=preferences.DisplayScalePercent;
                if(chosen==prior) return;
                preferences.DisplayScalePercent=chosen;
                try { preferences.Save(); DisplayScaling.Set(chosen); }
                catch(Exception ex) { preferences.DisplayScalePercent=prior; scale.SelectedIndex=Array.IndexOf(DisplayScaling.Options,prior); MessageBox.Show(this,ex.Message,"Could not save display scaling"); }
            };
            var scaleHint=new Label { Text="Applies immediately to PowerPal. Manual scales replace Windows scaling.\nThe dashboard stays maximized; smaller windows scroll when needed.",Bounds=new System.Drawing.Rectangle(22,310,510,48) };
            var appLabel=new Label { Text="Automatically log top apps",Bounds=new System.Drawing.Rectangle(22,377,300,26) };
            var appLimit=new NumericUpDown { Minimum=1,Maximum=100,Value=preferences.AppLimit,Bounds=new System.Drawing.Rectangle(370,372,162,30),AccessibleName="Number of top apps to record" };
            var appHint=new Label { Text="Ranked by CPU + GPU activity. History saves every 10 seconds,\nincluding while PowerPal is in the tray.",Bounds=new System.Drawing.Rectangle(22,414,510,48) };
            var sensors=new CheckBox { Text="Read GPU power sensors while the GPU is active",Checked=preferences.GpuSensors,AccessibleName="GPU power sensors",Bounds=new System.Drawing.Rectangle(22,473,510,26) };
            var sensorHint=new Label { Text="NVIDIA driver required. Polls about every 5 seconds.\nApp GPU watts are estimates; CPU watts are unavailable.",Bounds=new System.Drawing.Rectangle(22,506,510,48) };
            var save=new Button { Text="Save settings",Bounds=new System.Drawing.Rectangle(370,567,165,34),FlatStyle=FlatStyle.Flat };
            save.Click+=delegate { try { if(applyStartup!=null) applyStartup(startup.Checked); else Preferences.Startup=startup.Checked; preferences.AutoUpdate=automatic.Checked; preferences.TopAppCount=(int)appLimit.Value; preferences.GpuSensors=sensors.Checked; preferences.Save(); DialogResult=DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not save settings"); } };
            var controls=new Control[]{startup,automatic,info,themeLabel,theme,hint,scaleLabel,scale,scaleHint,appLabel,appLimit,appHint,sensors,sensorHint,save}; Content.Controls.AddRange(controls); AcceptButton=save;
            var bounds=controls.Select(c=>c.Bounds).ToArray();
            ContentLayout+=delegate { for(int i=0;i<controls.Length;i++) { var r=bounds[i]; controls[i].SetBounds(Px(r.X),Px(r.Y),Px(r.Width),Px(r.Height)); } };
            Theme.Changed+=ApplyTheme; ApplyTheme(); InitializeContent(new System.Drawing.Size(560,625),new System.Drawing.Size(560,625));
        }
        void ApplyTheme() { Theme.PaintControls(this); Content.Invalidate(true); }
        protected override void Dispose(bool disposing) { if(disposing) Theme.Changed-=ApplyTheme; base.Dispose(disposing); }
    }
}
