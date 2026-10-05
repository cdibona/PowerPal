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
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PowerPal"); } }
        public static Preferences Load() { try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Root,"settings.json"))) ?? new Preferences(); } catch { return new Preferences(); } }
        public void Save() { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root,"settings.json"),new JavaScriptSerializer().Serialize(this)); }
        public static bool Startup {
            get { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key!=null && key.GetValue("PowerPal")!=null; }
            set { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(value) key.SetValue("PowerPal","\""+Application.ExecutablePath+"\" --background"); else key.DeleteValue("PowerPal",false); } }
        }
        public static string Token() {
            string path=Path.Combine(Root,"github.dat"); if(!File.Exists(path)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));
        }
        public static void SaveToken(string value) {
            Directory.CreateDirectory(Root); string path=Path.Combine(Root,"github.dat");
            if(string.IsNullOrWhiteSpace(value)) { if(File.Exists(path)) File.Delete(path); return; }
            File.WriteAllBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()),null,DataProtectionScope.CurrentUser));
        }
        public static async Task ImportGitLogin() {
            var start=new ProcessStartInfo("git","-c credential.interactive=never credential fill") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"]="0"; start.EnvironmentVariables["GCM_INTERACTIVE"]="never";
            using(var process=Process.Start(start)) {
                var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
                await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\npath=cdibona/PowerPal.git\n\n"); process.StandardInput.Close();
                var completed=Task.WhenAll(output,error);
                if(await Task.WhenAny(completed,Task.Delay(15000))!=completed) { try { process.Kill(); } catch(InvalidOperationException) { } throw new InvalidOperationException("Git sign-in timed out. Use a read-only token instead."); }
                await completed;
                string password=output.Result.Split('\n').FirstOrDefault(line=>line.StartsWith("password="));
                if(password==null) throw new InvalidOperationException("No saved GitHub login was available. Sign in with Git or enter a read-only token.");
                SaveToken(password.Substring(9).TrimEnd('\r'));
            }
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
                string token=null;
                using(var handler=new HttpClientHandler { AllowAutoRedirect=false }) using(var client=new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(3) }) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerPal/"+Current.ToString(3));
                    string json;
                    var latest=await Latest(client,null);
                    if(latest.StatusCode==HttpStatusCode.NotFound && !verifyPublicRelease) {
                        try { token=Preferences.Token(); } catch { token=null; }
                        if(!string.IsNullOrWhiteSpace(token)) { latest.Dispose(); latest=await Latest(client,token); }
                    }
                    using(var response=latest) {
                        if(response.StatusCode==HttpStatusCode.NotFound) { status("No release available, or private repository access needed. Open Settings to connect GitHub."); return null; }
                        if(response.StatusCode==HttpStatusCode.Unauthorized || response.StatusCode==HttpStatusCode.Forbidden) { status("GitHub access unavailable or rate limited. Check your connection in Settings."); return null; }
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
                            using(var request=Request(uri,token,true)) using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead)) {
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
        static async Task<HttpResponseMessage> Latest(HttpClient client,string token) {
            using(var request=Request(new Uri(Api+"releases/latest"),token,false)) return await client.SendAsync(request);
        }
        static HttpRequestMessage Request(Uri uri,string token,bool binary) {
            var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.Accept.ParseAdd(binary?"application/octet-stream":"application/vnd.github+json");
            // Never forward credentials to the signed asset storage redirect.
            if(uri.Host=="api.github.com" && !string.IsNullOrWhiteSpace(token)) request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
            return request;
        }
    }
    internal sealed class SettingsDialog : Form {
        public SettingsDialog(Preferences preferences) {
            Text="PowerPal settings"; ClientSize=new System.Drawing.Size(560,405); BackColor=Palette.Bg; ForeColor=Palette.Text; Font=new System.Drawing.Font("Segoe UI",13,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel); StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            var startup=new CheckBox { Text="Start recording in the tray when I sign in",Checked=Preferences.Startup,Left=22,Top=22,Width=510 };
            var automatic=new CheckBox { Text="Download and install GitHub release updates automatically",Checked=preferences.AutoUpdate,Left=22,Top=58,Width=520 };
            var label=new Label { Text="Private GitHub releases need a fine-grained token with Contents: read\nfor cdibona/PowerPal. It is encrypted for your Windows account.\nLeave blank to keep the saved connection.",Left=22,Top=105,Width=510,Height=70 };
            var token=new TextBox { Left=22,Top=185,Width=510,UseSystemPasswordChar=true };
            var forget=new CheckBox { Text="Forget the saved GitHub connection",Left=22,Top=230,Width=510 };
            var connect=new Button { Text="Use existing GitHub login",Left=22,Top=276,Width=230,Height=34,BackColor=Palette.Card,FlatStyle=FlatStyle.Flat };
            connect.Click+=async delegate { connect.Enabled=false; try { await Preferences.ImportGitLogin(); connect.Text="GitHub login connected"; } catch(Exception ex) { MessageBox.Show(this,ex.Message,"GitHub connection"); } finally { if(!IsDisposed) connect.Enabled=true; } };
            var save=new Button { Text="Save settings",Left=370,Top=346,Width=165,Height=34,BackColor=Palette.Card,FlatStyle=FlatStyle.Flat };
            save.Click+=delegate { try { Preferences.Startup=startup.Checked; preferences.AutoUpdate=automatic.Checked; preferences.Save(); if(forget.Checked) Preferences.SaveToken(null); else if(!string.IsNullOrWhiteSpace(token.Text)) Preferences.SaveToken(token.Text); DialogResult=DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not save settings"); } };
            Controls.AddRange(new Control[]{startup,automatic,label,token,forget,connect,save}); AcceptButton=save;
        }
    }
}
