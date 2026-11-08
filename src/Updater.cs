// Self-update from the GitHub releases of this project.
// Check: latest release tag (vX.Y.Z) newer than Updater.Version → download its ClaudeUsageTray.exe,
// verify it (size, "MZ" header, SHA-256 when GitHub reports one), swap it in and restart.
// The running exe can be renamed but not overwritten, so: exe → exe.old, new → exe, start new, exit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

public static class Updater
{
    public const string Version = "1.2.0"; // raise this for every release; the tag must be "v" + Version
    const string Repo = "drmohammedalmasri-pixel/claude-usage-tray";
    const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    const string AssetName = "ClaudeUsageTray.exe";

    public class Info
    {
        public string Version, Notes, Url, Sha256, PageUrl, Error;
        public bool Newer;
    }

    static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

    public static Info Check()
    {
        var info = new Info();
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(LatestUrl);
            req.Timeout = 20000;
            req.UserAgent = "claude-usage-tray/" + Version;
            req.Accept = "application/vnd.github+json";
            string resp;
            using (var r = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(r.GetResponseStream())) resp = sr.ReadToEnd();
            var j = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(resp);
            string tag = j.ContainsKey("tag_name") ? j["tag_name"] as string : null;
            info.Version = (tag ?? "").TrimStart('v', 'V');
            info.Notes = j.ContainsKey("body") ? j["body"] as string : null;
            info.PageUrl = j.ContainsKey("html_url") ? j["html_url"] as string : null;
            var assets = j.ContainsKey("assets") ? j["assets"] as object[] : null;
            if (assets != null)
                foreach (var o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(a["name"] as string, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    info.Url = a["browser_download_url"] as string;
                    string dg = a.ContainsKey("digest") ? a["digest"] as string : null; // "sha256:…"
                    if (dg != null && dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = dg.Substring(7).ToLowerInvariant();
                }
            info.Newer = Compare(info.Version, Version) > 0 && info.Url != null;
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
            Settings.Log("update check failed: " + ex.Message);
        }
        return info;
    }

    // "1.10.0" > "1.9.2"; anything unreadable counts as 0
    public static int Compare(string a, string b)
    {
        string[] x = (a ?? "").Split('.'), y = (b ?? "").Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p, q;
            if (i >= x.Length || !int.TryParse(x[i], out p)) p = 0;
            if (i >= y.Length || !int.TryParse(y[i], out q)) q = 0;
            if (p != q) return p.CompareTo(q);
        }
        return 0;
    }

    // Downloads and verifies the new exe next to the running one. Returns null on success, else an error text.
    public static string Download(Info info, out string newPath)
    {
        newPath = ExePath + ".new";
        try
        {
            if (File.Exists(newPath)) File.Delete(newPath);
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "claude-usage-tray/" + Version;
                wc.DownloadFile(info.Url, newPath);
            }
            string bad = Verify(newPath, info.Sha256);
            if (bad != null) { File.Delete(newPath); Settings.Log("update rejected: " + bad); return bad; }
            return null;
        }
        catch (Exception ex)
        {
            Settings.Log("update download failed: " + ex.Message);
            try { File.Delete(newPath); } catch { }
            return ex.Message;
        }
    }

    public static string Verify(string path, string sha256)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length < 50000 || fi.Length > 20000000) return "unexpected file size";
        byte[] all = File.ReadAllBytes(path);
        if (all[0] != 'M' || all[1] != 'Z') return "not a Windows program";
        if (sha256 != null)
        {
            string h;
            using (var s = SHA256.Create()) h = BitConverter.ToString(s.ComputeHash(all)).Replace("-", "").ToLowerInvariant();
            if (h != sha256) return "checksum does not match";
        }
        return null;
    }

    // Swaps the new exe in and starts it. The caller must exit right after (the new one waits for our mutex).
    public static string Install(string newPath)
    {
        string exe = ExePath, old = exe + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(newPath, exe); }
            catch { File.Move(old, exe); throw; } // put the working version back
            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
            Settings.Log("updated to a new version, restarting");
            return null;
        }
        catch (Exception ex)
        {
            Settings.Log("update install failed: " + ex.Message);
            return ex.Message;
        }
    }

    // On start: remove what the last update left behind (the old exe is unlocked once it has exited).
    public static void CleanUp()
    {
        try { string old = ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        try { string nw = ExePath + ".new"; if (File.Exists(nw)) File.Delete(nw); } catch { }
    }
}
