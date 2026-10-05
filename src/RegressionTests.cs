using System;
using System.IO;
using System.Security.Cryptography;

namespace PowerPal {
    internal static class RegressionTests {
        static void Check(bool condition,string name) { if(!condition) throw new Exception(name); }
        public static void Run(string folder) {
            Check(ActivityMonitor.CpuPercent(2000,2,4)==25,"CPU normalization");
            Check(ActivityMonitor.CpuPercent(-1,2,4)==0 && ActivityMonitor.CpuPercent(100,0,4)==0,"CPU resets");
            string digest=new string('a',64);
            string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v0.3.0\",\"assets\":[{\"name\":\"PowerPal-Setup-0.3.0-win-x64.exe\",\"url\":\"https://api.github.com/repos/cdibona/PowerPal/releases/assets/123\",\"size\":20,\"digest\":\"sha256:"+digest+"\"}]}";
            var release=ReleaseUpdater.Parse(json); Check(release.Version==new Version(0,3,0,0) && release.Digest==digest,"Release version");
            Check(ReleaseUpdater.Parse(json.Replace("\"prerelease\":false","\"prerelease\":true"))==null,"Skip prerelease");
            Check(ReleaseUpdater.Parse(json.Replace("v0.3.0","v0.3.0-beta"))==null,"Skip unstable tags");
            bool rejected=false; try { ReleaseUpdater.Parse(json.Replace("api.github.com","evil.example")); } catch(InvalidDataException) { rejected=true; } Check(rejected,"Reject untrusted update URL");
            rejected=false; try { ReleaseUpdater.Parse(json.Replace("sha256:","sha1:")); } catch(InvalidDataException) { rejected=true; } Check(rejected,"Require SHA-256");
            Check(!ReleaseUpdater.ValidDownload(new Uri("http://release-assets.githubusercontent.com/a")) && !ReleaseUpdater.ValidDownload(new Uri("https://api.github.com/repos/other/project/releases/assets/123")),"Reject insecure or other-repo download");
            string file=Path.Combine(folder,"digest.txt"); File.WriteAllText(file,"PowerPal fixture"); string expected;
            using(var sha=SHA256.Create()) expected=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","");
            Check(ReleaseUpdater.Verify(file,expected,new FileInfo(file).Length),"Good digest");
            Check(!ReleaseUpdater.Verify(file,new string('0',64),new FileInfo(file).Length),"Tampered digest");
            Check(!ReleaseUpdater.Verify(file,expected,999),"Wrong length");
            var history=new History(Path.Combine(folder,"repair")); var s=new Sample(); history.Append(new[]{s});
            File.AppendAllText(Directory.GetFiles(history.Folder)[0],"partial"); history.Append(new[]{s}); Check(history.Load(DateTime.UtcNow.AddDays(-1)).Count==2,"Recover torn CSV append");
        }
    }
}
