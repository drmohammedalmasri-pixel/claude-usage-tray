// Claude Usage Tray: tray icon that compares weekly Claude usage with elapsed week time.
// Build: build.ps1 (uses the C# compiler that ships with Windows, C# 5 syntax only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        // optional 3rd argument for --png / --settings checks: language code
        L.Set(args.Length >= 3 ? args[2] : Settings.Load().Language);
        if (args.Length >= 2 && args[0] == "--png") { TrayApp.Preview(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--test") { History.SelfTest(args[1]); return; }
        if (args.Length == 1 && args[0] == "--update-now") // check + install without the tray (for tests); exit code 0 = updated
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var u = Updater.Check(); string np;
            Environment.Exit(u.Error != null ? 3 : !u.Newer ? 1 : (Updater.Download(u, out np) ?? Updater.Install(np)) == null ? 0 : 2);
        }
        if (args.Length == 2 && args[0] == "--ico") { TrayApp.SaveIco(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--settings")
        {
            using (var f = new SettingsForm(new Settings()))
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, 0);
                f.Show(); Application.DoEvents();
                if (args.Length == 4) { f.ScrollToEnd(); Application.DoEvents(); }
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(args[1]); }
            }
            return;
        }
        bool created;
        bool updated = args.Length >= 1 && args[0] == "--updated";
        using (var m = new Mutex(true, "ClaudeUsageTray_SingleInstance", out created))
        {
            if (!created && updated)
            {
                // started by the old version during an update: wait until it has exited
                try { created = m.WaitOne(20000); } catch (AbandonedMutexException) { created = true; }
            }
            if (!created) { Settings.Log("second start ignored, already running: " + OtherInstances()); return; }
            if (RestartOutsideRedirect()) { m.ReleaseMutex(); return; }
            Settings.Log("start v" + Updater.Version + " pid " + System.Diagnostics.Process.GetCurrentProcess().Id +" args [" + string.Join(" ", args) + "] Windows up " + (Environment.TickCount / 60000) + " min");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Settings.Log("crash: " + e.ExceptionObject);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Settings.Log("error (kept running): " + e.Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => { Settings.Log("task error: " + e.Exception.GetBaseException()); e.SetObserved(); };
            SystemEvents.SessionEnding += (s, e) => Settings.Log("Windows " + (e.Reason == SessionEndReasons.SystemShutdown ? "shutdown" : "sign-out"));
            StartWatchdog();
            Updater.CleanUp();
            try { SetProcessDPIAware(); } catch { }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var app = new TrayApp();
            if (updated) app.AnnounceUpdate();
            Stage = "running";
            Application.Run(app);
            Settings.Log("exit");
        }
    }

    // Started from inside a packaged app (the Claude desktop app: a build, a sign-in window), Windows quietly
    // redirects AppData writes to %LOCALAPPDATA%\Packages\<app>\LocalCache\Roaming. Then settings, history and
    // log go to a second copy and the next normal start sees none of it. Detect this with a marker file and start
    // again through Explorer, which runs outside the redirect. Returns true when this process should exit.
    static bool RestartOutsideRedirect()
    {
        try
        {
            string dir = Settings.Dir, id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".where"), id);
            string pk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(pk)) return false;
            bool redirected = false;
            foreach (var p in Directory.GetDirectories(pk))
            {
                string f = Path.Combine(p, @"LocalCache\Roaming\ClaudeUsageTray\.where");
                try { if (File.Exists(f) && File.ReadAllText(f) == id) { redirected = true; break; } } catch { }
            }
            if (!redirected) return false;
            // guard against a loop if the Explorer start were redirected too
            string guard = Path.Combine(dir, ".restart");
            if (File.Exists(guard) && (DateTime.Now - File.GetLastWriteTime(guard)).TotalMinutes < 2)
            { Settings.Log("data folder is redirected by a packaged app, restart did not help -> running anyway"); return false; }
            File.WriteAllText(guard, DateTime.Now.ToString("o"));
            Settings.Log("data folder is redirected by a packaged app -> starting again through Explorer");
            System.Diagnostics.Process.Start("explorer.exe", "\"" + Application.ExecutablePath + "\"");
            return true;
        }
        catch (Exception ex) { Settings.Log("redirect check: " + ex.Message); return false; }
    }

    // --- watchdog: notes in the log when the window thread stops responding (the app then looks frozen) ---
    public static long Beat = DateTime.UtcNow.Ticks;   // set by a UI timer
    public static volatile string Stage = "starting";   // last startup step reached

    static void StartWatchdog()
    {
        var t = new Thread(() =>
        {
            bool reported = false;
            long woke = DateTime.UtcNow.Ticks;
            while (true)
            {
                Thread.Sleep(30000);
                long now = DateTime.UtcNow.Ticks;
                bool slept = (now - woke) > TimeSpan.TicksPerMinute; // PC was asleep: the UI had no chance to beat yet
                woke = now;
                if (slept) { Interlocked.Exchange(ref Beat, now); continue; }
                double min = (DateTime.UtcNow.Ticks - Interlocked.Read(ref Beat)) / (double)TimeSpan.TicksPerMinute;
                if (min >= 3 && !reported) { Settings.Log("not responding for " + Math.Round(min) + " min (step: " + Stage + ")"); reported = true; }
                else if (min < 1 && reported) { Settings.Log("responding again"); reported = false; }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    static string OtherInstances()
    {
        var sb = new StringBuilder();
        try
        {
            int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
            {
                if (p.Id == me) continue;
                string since = "?"; try { since = p.StartTime.ToString("HH:mm"); } catch { }
                string resp = "?"; try { resp = p.Responding ? "responding" : "NOT responding"; } catch { }
                sb.Append("pid " + p.Id + " since " + since + " " + resp + "; ");
            }
        }
        catch (Exception ex) { sb.Append(ex.Message); }
        return sb.Length > 0 ? sb.ToString() : "(none found)";
    }
}

public class Settings
{
    public bool Show5h = true;
    public bool FirstRunDone = false;
    public double YellowAt = 3;   // usage ahead of time by more than this → yellow
    public double RedAt = 10;     // … more than this → red
    public int ResetDay = 6;      // Saturday (fallback when the server gives no reset time)
    public int ResetHour = 17;
    // optional features (each can be switched in the settings window)
    public bool ShowSessionsLeft = true;   // "N full sessions left"
    public bool LongSessions = true;       // keep/average counted sessions for SessionWeeks weeks (off = 3 days)
    public int SessionWeeks = 8;
    public double MinFive = 35;            // smallest 5h usage that counts as a session
    public bool WeeksTab = true;           // tab with finished weeks in the history window
    public bool ShowOpus = true;           // separate bar for the biggest model, when the server reports it
    public bool BlueIcon = true;           // blue icon when far behind the clock
    public double BlueAt = 15;
    public bool IconNumber = true;         // difference number inside the icon
    public bool IconNumberBars = true;     // ...together with the two small bars
    public int PollMinutes = 10;
    public bool ExportMenu = true;         // "export to Excel" menu item
    public double? LastOpusPct;
    public double? ManualPercent;
    public string ManualAt;
    public double? LastWeekPct;
    public string LastWeekReset;
    public double? LastFivePct;
    public string LastFiveReset;
    public string LastFetch;
    public string Language;               // "" = Windows language; new install = "en"; null in old files = Arabic (first versions were Arabic only)
    public bool NoFive;
    public bool AutoUpdate = true;        // install new GitHub releases by itself
    public bool AutoSignIn;               // open Claude Code's sign-in window by itself when the saved sign-in has expired
    public string LastUpdateCheck;                   // last fetch had no 5-hour limit (plans with weekly limit only)

    public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray"); } }
    static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) { var x = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)); if (x.Language == null) x.Language = "ar"; return x; } }
        catch { }
        return new Settings { Language = "en" };   // new installs start in English (owner 08.11)
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this)); } catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string p = Path.Combine(Dir, "log.txt");
            if (File.Exists(p) && new FileInfo(p).Length > 100000) File.Delete(p);
            File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // the normal log could not be written: keep the line and the reason in a second file
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ClaudeUsageTray-log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [log.txt failed: " + ex.Message + "]  " + msg + Environment.NewLine); }
            catch { }
        }
    }

    public static string D2S(DateTime d) { return d.ToString("o", CultureInfo.InvariantCulture); }
    public static DateTime? S2D(string s)
    {
        DateTime d;
        if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d.ToLocalTime();
        return null;
    }
}

public class View
{
    public bool HasData;
    public double Usage, Elapsed;
    public string Remaining, Status, Budget, Updated, Note, Error;
    public Color StatusColor;
    public bool Show5h;
    public double? Five;
    public string FiveReset;
    public string Ratio, RatioNow, SessionsLeft;
    public double? Opus;
    public string IconText;
    public bool LoginNeeded; // popup shows the "sign in again" button
}

// ---------- data access (no model calls, so no usage cost) ----------
static class Api
{
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    static readonly string[] TokenUrls = { "https://platform.claude.com/v1/oauth/token", "https://console.anthropic.com/v1/oauth/token" };
    const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    static string CredPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json"); } }
    static DateTime lastRefreshTry = DateTime.MinValue;
    static string lastTriedToken, deadRefreshToken;
    static bool loggedKeys;
    public static string CredFile { get { return CredPath; } }

    // short error code from an OAuth error reply, for the log (never the whole reply)
    static string ErrorText(string resp)
    {
        try
        {
            var j = new JavaScriptSerializer().DeserializeObject(resp) as Dictionary<string, object>;
            if (j == null) return "";
            object e; j.TryGetValue("error", out e);
            var ed = e as Dictionary<string, object>;
            if (ed != null) { object t; ed.TryGetValue("type", out t); e = t; }
            object d; j.TryGetValue("error_description", out d);
            string s = (e ?? "") + (d != null ? ": " + d : "");
            return s.Length > 150 ? s.Substring(0, 150) : s;
        }
        catch { return ""; }
    }

    public class Result
    {
        public double? Week; public DateTime? WeekReset;
        public double? Five; public DateTime? FiveReset; public double? Opus;
        public string Error; public bool Busy, LoginNeeded;
    }

    static int Http(string method, string url, string body, Dictionary<string, string> headers, out string resp)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = method;
        req.Timeout = 20000;
        req.UserAgent = "claude-usage-tray/" + Updater.Version;
        req.Accept = "application/json";
        if (headers != null) foreach (var kv in headers) req.Headers[kv.Key] = kv.Value;
        if (body != null)
        {
            req.ContentType = "application/json";
            byte[] b = Encoding.UTF8.GetBytes(body);
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
        }
        try
        {
            using (var r = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(r.GetResponseStream())) { resp = sr.ReadToEnd(); return (int)r.StatusCode; }
        }
        catch (WebException ex)
        {
            var r = ex.Response as HttpWebResponse;
            if (r == null) throw;
            using (r) using (var sr = new StreamReader(r.GetResponseStream())) { resp = sr.ReadToEnd(); return (int)r.StatusCode; }
        }
    }

    static Dictionary<string, object> LoadCreds(out Dictionary<string, object> root)
    {
        root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(CredPath));
        return (Dictionary<string, object>)root["claudeAiOauth"];
    }

    static double? Num(object o)
    {
        if (o == null) return null;
        try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return null; }
    }

    static DateTime? Time(object o)
    {
        var s = o as string;
        DateTime d;
        if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d.ToLocalTime();
        return null;
    }

    // Returns null on success, otherwise an Arabic error text.
    static string Refresh()
    {
        Dictionary<string, object> root;
        var c = LoadCreds(out root);
        string rt = c.ContainsKey("refreshToken") ? c["refreshToken"] as string : null;
        if (string.IsNullOrEmpty(rt)) return L.T("no_login");
        // A rejected refresh token stays dead: never send it again (refresh tokens are shared with Claude Code,
        // so only a new sign-in by Claude Code helps; the file watcher retries as soon as that is written).
        if (rt == deadRefreshToken) return L.T("login_expired");
        // the 15 min pause is for the same token only; a new token from Claude Code is tried at once
        if (rt == lastTriedToken && (DateTime.Now - lastRefreshTry).TotalMinutes < 15) return L.T("refresh_wait");
        lastRefreshTry = DateTime.Now;
        lastTriedToken = rt;
        var ser = new JavaScriptSerializer();
        string body = ser.Serialize(new Dictionary<string, object> { { "grant_type", "refresh_token" }, { "refresh_token", rt }, { "client_id", ClientId } });
        int lastCode = 0;
        foreach (var url in TokenUrls)
        {
            string resp;
            int code;
            try { code = Http("POST", url, body, null, out resp); }
            catch (Exception ex) { Settings.Log("refresh " + url + " net error: " + ex.Message); continue; }
            lastCode = code;
            if (code == 200)
            {
                var j = (Dictionary<string, object>)ser.DeserializeObject(resp);
                c["accessToken"] = j["access_token"];
                if (j.ContainsKey("refresh_token") && j["refresh_token"] != null) c["refreshToken"] = j["refresh_token"];
                double exp = Num(j.ContainsKey("expires_in") ? j["expires_in"] : null) ?? 3600;
                c["expiresAt"] = (long)(DateTimeOffset.UtcNow.AddSeconds(exp).ToUnixTimeMilliseconds());
                string bak = CredPath + ".bak-usage-tray";
                if (!File.Exists(bak)) File.Copy(CredPath, bak);
                string tmp = CredPath + ".tmp-usage-tray";
                File.WriteAllText(tmp, ser.Serialize(root));
                File.Copy(tmp, CredPath, true);
                File.Delete(tmp);
                Settings.Log("token refreshed via " + url);
                return null;
            }
            Settings.Log("refresh " + url + " -> HTTP " + code + " " + ErrorText(resp));
            if (code == 400 || code == 401) break; // the token itself was rejected, the other address would say the same
        }
        if (lastCode == 429) return L.T("refresh_429");
        if (lastCode == 400 || lastCode == 401) { deadRefreshToken = rt; return L.T("login_expired"); }
        return L.T("refresh_fail");
    }

    public static Result Fetch()
    {
        var res = new Result();
        try
        {
            if (!File.Exists(CredPath)) { res.Error = L.T("no_cred"); return res; }
            Dictionary<string, object> root;
            var c = LoadCreds(out root);
            double exp = Num(c.ContainsKey("expiresAt") ? c["expiresAt"] : null) ?? 0;
            if (exp < DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds())
            {
                string e = Refresh();
                if (e != null) { res.Error = e; res.Busy = true; res.LoginNeeded = e == L.T("login_expired") || e == L.T("no_login"); return res; }
                c = LoadCreds(out root);
            }
            string resp = null;
            int code = 0;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var h = new Dictionary<string, string> { { "Authorization", "Bearer " + c["accessToken"] }, { "anthropic-beta", "oauth-2025-04-20" } };
                code = Http("GET", UsageUrl, null, h, out resp);
                if (code != 401 || attempt == 1) break;
                string e = Refresh();
                if (e != null) { res.Error = e; res.Busy = true; res.LoginNeeded = e == L.T("login_expired") || e == L.T("no_login"); return res; }
                c = LoadCreds(out root);
            }
            if (code == 429) { res.Error = L.T("busy"); res.Busy = true; return res; }
            if (code != 200) { Settings.Log("usage HTTP " + code); res.Error = L.T("read_fail"); return res; }
            var j = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(resp);
            var w = j.ContainsKey("seven_day") ? j["seven_day"] as Dictionary<string, object> : null;
            var f = j.ContainsKey("five_hour") ? j["five_hour"] as Dictionary<string, object> : null;
            if (w != null) { res.Week = Num(w.ContainsKey("utilization") ? w["utilization"] : null); res.WeekReset = Time(w.ContainsKey("resets_at") ? w["resets_at"] : null); }
            if (f != null) { res.Five = Num(f.ContainsKey("utilization") ? f["utilization"] : null); res.FiveReset = Time(f.ContainsKey("resets_at") ? f["resets_at"] : null); }
            var o = j.ContainsKey("seven_day_opus") ? j["seven_day_opus"] as Dictionary<string, object> : null;
            if (o != null) res.Opus = Num(o.ContainsKey("utilization") ? o["utilization"] : null);
            if (!loggedKeys)
            {
                loggedKeys = true;
                var names = new List<string>();
                foreach (var kv in j) if (kv.Value != null) names.Add(kv.Key);
                Settings.Log("usage keys: " + string.Join(",", names.ToArray()));
                foreach (var k in new[] { "seven_day_breakdown", "weekly_scoped_shares" }) if (j.ContainsKey(k)) Settings.Log(k + " = " + new JavaScriptSerializer().Serialize(j[k]));
            }
            if (res.Week == null) { res.Week = 0; }
        }
        catch (Exception ex)
        {
            Settings.Log("fetch error: " + ex.Message);
            res.Error = L.T("offline");
        }
        return res;
    }
}

// ---------- hover popup ----------
public class Popup : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public View V;
    Font fTitle = new Font("Segoe UI", 10.5f, FontStyle.Bold);
    Font fBody = new Font("Segoe UI", 9.5f);
    Font fBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    Font fSmall = new Font("Segoe UI", 8.5f);
    Color bg = Color.FromArgb(32, 32, 32), fg = Color.FromArgb(240, 240, 240), dim = Color.FromArgb(160, 160, 160);
    Graphics g; float s; bool draw; int y, pad, W;

    public Popup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = bg;
        TopMost = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x8; // no-activate, tool window, topmost
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        dwmBorder = Theme.RoundAndBorder(Handle, Color.FromArgb(70, 70, 70), false);
    }
    bool dwmBorder;

    // reading start / end side for the current language
    static TextFormatFlags Start { get { return L.Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left; } }
    static TextFormatFlags End { get { return L.Rtl ? TextFormatFlags.Left : TextFormatFlags.Right; } }

    int S(float px) { return (int)Math.Round(px * s); }
    int H(Font f) { return TextRenderer.MeasureText(g, "Ag", f).Height; }

    // long lines wrap (they used to be cut off with "…")
    void TextR(string t, Font f, Color c)
    {
        if (string.IsNullOrEmpty(t)) return;
        TextFormatFlags fl = Start | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
        int h = Math.Max(H(f), TextRenderer.MeasureText(g, t, f, new Size(W - 2 * pad, 0), fl).Height);
        if (draw) TextRenderer.DrawText(g, t, f, new Rectangle(pad, y, W - 2 * pad, h), c, fl);
        y += h + S(3);
    }

    void Row(string label, string val, Font f, Color c)
    {
        if (draw)
        {
            var r = new Rectangle(pad, y, W - 2 * pad, H(f));
            TextRenderer.DrawText(g, label, f, r, c, Start | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, val, fBold, r, c, End | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        y += H(f) + S(4);
    }

    void Bar(double pct, Color c, int h)
    {
        if (draw)
        {
            int w = W - 2 * pad;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var tb = new SolidBrush(Color.FromArgb(62, 62, 62))) FillRound(new RectangleF(pad, y, w, h), tb);
            float fw = (float)(w * Math.Max(0, Math.Min(100, pct)) / 100.0);
            if (fw > 0.5f) using (var fb = new SolidBrush(c)) FillRound(new RectangleF(L.Rtl ? pad + w - fw : pad, y, fw, h), fb); // fills from the reading start
        }
        y += h;
    }

    void FillRound(RectangleF r, Brush b)
    {
        float rad = Math.Min(r.Height, r.Width);
        using (var p = new GraphicsPath())
        {
            p.AddArc(r.X, r.Y, rad, r.Height, 90, 180);
            p.AddArc(r.Right - rad, r.Y, rad, r.Height, 270, 180);
            p.CloseFigure();
            g.FillPath(b, p);
        }
    }

    void Sep()
    {
        y += S(4);
        if (draw) using (var pen = new Pen(Color.FromArgb(60, 60, 60))) g.DrawLine(pen, pad, y, W - pad, y);
        y += S(8);
    }

    static string Pct(double v) { return L.Pct(v); }

    // small "sign in again" button under the error, only when the saved sign-in has expired
    public event EventHandler SignInClick;
    Rectangle btnRect = Rectangle.Empty;
    bool btnHover;
    void SignInButton(View v)
    {
        if (!v.LoginNeeded) { if (draw) btnRect = Rectangle.Empty; return; }
        string t = L.T("signin");
        var tf = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | (L.Rtl ? TextFormatFlags.RightToLeft : 0);
        int bw = TextRenderer.MeasureText(g, t, fSmall, Size.Empty, tf).Width + S(26), bh = H(fSmall) + S(10);
        y += S(3);
        var r = new Rectangle(L.Rtl ? W - pad - bw : pad, y, bw, bh);
        if (draw)
        {
            btnRect = r;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(btnHover ? Theme.AccentHover : Theme.Accent))
            using (var p = Theme.Round(r, bh / 2f)) g.FillPath(b, p);
            TextRenderer.DrawText(g, t, fSmall, r, Color.White, tf);
        }
        y += bh + S(3);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool h = btnRect.Contains(e.Location);
        Cursor = h ? Cursors.Hand : Cursors.Default;
        if (h != btnHover) { btnHover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (btnHover) { btnHover = false; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && btnRect.Contains(e.Location) && SignInClick != null) SignInClick(this, EventArgs.Empty);
    }

    int Render(Graphics gr, bool doDraw)
    {
        g = gr; draw = doDraw; s = gr.DpiX / 96f; pad = S(14); W = S(310); y = pad;
        var v = V ?? new View();
        if (draw)
        {
            var r = new Rectangle(pad, y, W - 2 * pad, H(fTitle));
            TextRenderer.DrawText(g, v.Updated ?? "", fSmall, r, dim, End | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        TextR(L.T("title"), fTitle, fg);
        y += S(6);
        if (!v.HasData)
        {
            TextR(v.Error ?? L.T("loading"), fBody, Color.FromArgb(230, 120, 110));
            SignInButton(v);
            return y + pad;
        }
        Row(L.T("usage"), Pct(v.Usage), fBody, fg);
        Bar(v.Usage, v.StatusColor, S(9));
        y += S(10);
        Row(L.T("time"), Pct(v.Elapsed), fBody, fg);
        Bar(v.Elapsed, Color.FromArgb(110, 140, 190), S(9));
        y += S(6);
        TextR(v.Remaining, fSmall, dim);
        Sep();
        TextR(v.Status, fBold, v.StatusColor);
        TextR(v.Budget, fBody, fg);
        if (v.Show5h && v.Five.HasValue)
        {
            Sep();
            Row(L.T("five"), Pct(v.Five.Value), fSmall, fg);
            Bar(v.Five.Value, Color.FromArgb(150, 125, 200), S(5));
            y += S(5);
            if (!string.IsNullOrEmpty(v.FiveReset)) TextR(v.FiveReset, fSmall, dim);
        }
        if (v.Opus.HasValue)
        {
            Sep();
            Row(L.T("opus"), Pct(v.Opus.Value), fSmall, fg);
            Bar(v.Opus.Value, Color.FromArgb(210, 130, 80), S(5));
            y += S(5);
        }
        if (!string.IsNullOrEmpty(v.Ratio) || !string.IsNullOrEmpty(v.RatioNow) || !string.IsNullOrEmpty(v.SessionsLeft))
        {
            Sep();
            if (!string.IsNullOrEmpty(v.SessionsLeft)) TextR(v.SessionsLeft, fBold, fg);
            if (!string.IsNullOrEmpty(v.Ratio)) TextR(v.Ratio, fSmall, fg);
            if (!string.IsNullOrEmpty(v.RatioNow)) TextR(v.RatioNow, fSmall, dim);
        }
        if (!string.IsNullOrEmpty(v.Note)) TextR(v.Note, fSmall, Color.FromArgb(215, 170, 70));
        if (!string.IsNullOrEmpty(v.Error)) TextR(v.Error, fSmall, Color.FromArgb(230, 120, 110));
        SignInButton(v);
        return y + pad - S(3);
    }

    public Bitmap Snapshot()
    {
        int h;
        using (var tmp = new Bitmap(1, 1)) using (var gr = Graphics.FromImage(tmp)) h = Render(gr, false);
        var bmp = new Bitmap(W, h);
        using (var gr = Graphics.FromImage(bmp)) { gr.Clear(bg); Render(gr, true); }
        return bmp;
    }

    public void ShowAt(Point cursor)
    {
        using (var gr = CreateGraphics())
        {
            int h = Render(gr, false);
            Size = new Size(W, h);
        }
        var wa = Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Max(wa.Left + 8, Math.Min(cursor.X - Width / 2, wa.Right - Width - 8));
        int yy = (cursor.Y > wa.Bottom - 4) ? wa.Bottom - Height - 10 : Math.Max(wa.Top + 8, cursor.Y - Height - 16);
        Location = new Point(x, yy);
        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(bg);
        Render(e.Graphics, true);
        if (!dwmBorder) using (var pen = new Pen(Color.FromArgb(70, 70, 70))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

// ---------- manual entry ----------
public class ManualForm : Form
{
    public Stepper Num;
    public ManualForm(double current)
    {
        float s = Theme.Scale(this);
        Func<float, int> S = px => (int)Math.Round(px * s);
        Text = L.T("manual_title");
        Icon = IconArt.AppIcon;
        RightToLeft = L.Dir;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 10f);
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        Theme.DarkWindow(this);
        int W = S(360), pad = S(20);
        ClientSize = new Size(W, S(176));
        var lbl = new Label { Text = L.T("manual_label"), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(pad, S(16), W - 2 * pad, S(28)), BackColor = Theme.Bg };
        Num = new Stepper((int)Math.Round(Math.Max(0, Math.Min(100, current))), 0, 100) { Font = new Font("Segoe UI", 11f), Bounds = new Rectangle(W - pad - S(132), S(54), S(132), S(36)) };
        var pct = new Label { Text = L.Rtl ? "٪" : "%", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(W - pad - S(132) - S(34), S(54), S(28), S(36)), ForeColor = Theme.Dim, BackColor = Theme.Bg };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = S(64), BackColor = Color.FromArgb(27, 27, 27) };
        bar.Paint += (o, e) => { using (var p = new Pen(Color.FromArgb(48, 48, 48))) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        var ok = new ModernButton(L.T("save"), true) { DialogResult = DialogResult.OK, Bounds = new Rectangle(W - pad - S(110), S(14), S(110), S(36)) };
        var cancel = new ModernButton(L.T("cancel"), false) { DialogResult = DialogResult.Cancel, Bounds = new Rectangle(W - pad - S(110) - S(10) - S(110), S(14), S(110), S(36)) };
        bar.Controls.Add(ok); bar.Controls.Add(cancel);
        Controls.Add(lbl); Controls.Add(Num); Controls.Add(pct); Controls.Add(bar);
        if (!L.Rtl) // left-to-right languages: number on the left, Cancel at the right edge
        {
            Num.Left = pad; pct.Left = Num.Right + S(6);
            cancel.Left = W - pad - cancel.Width; ok.Left = cancel.Left - S(10) - ok.Width;
        }
        AcceptButton = ok; CancelButton = cancel;
        ActiveControl = Num;
    }
}

// ---------- settings window (Windows 11 style: sections of cards with switches) ----------
public class SettingsForm : Form
{
    class Row { public Panel P; public Label Title, Sub; public Control[] Ctrls; }
    class Sec { public Label Head; public CardPanel Card; public List<Row> Rows = new List<Row>(); }

    Settings st;
    float s;
    int S(float px) { return (int)Math.Round(px * s); }
    Font fTitle, fSub, fHead;
    Panel scroll, inner, bottom;
    List<Sec> secs = new List<Sec>();
    Sec cur;
    double pUsage, pElapsed;
    Panel preview;

    ToggleSwitch tFive, tSessLeft, tOpus, tLong, tWeeks, tExport, tNum, tBars, tBlue, tAuto, tUpd, tSignIn;
    Stepper nWeeks, nMin, nYellow, nRed, nBlue, nPoll;
    Picker cLang;
    public bool Autostart { get { return tAuto.Checked; } }

    public SettingsForm(Settings set, double usage = 62, double elapsed = 55, bool autostart = true)
    {
        st = set; pUsage = usage; pElapsed = elapsed;
        s = Theme.Scale(this);
        Text = L.T("settings");
        Icon = IconArt.AppIcon;
        RightToLeft = L.Dir;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 10f);
        fTitle = Font; fSub = new Font("Segoe UI", 8.75f); fHead = new Font("Segoe UI Semibold", 10.5f);
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        Theme.DarkWindow(this);

        inner = new Panel { BackColor = Theme.Bg, RightToLeft = RightToLeft.No, Location = Point.Empty };
        scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, RightToLeft = RightToLeft.No };
        scroll.Controls.Add(inner);

        Section(L.T("sec_popup"));
        tFive = Toggle(set.Show5h); Add(L.T("five"), L.T("five_sub"), tFive);
        tSessLeft = Toggle(set.ShowSessionsLeft); Add(L.T("sessleft"), L.T("sessleft_sub"), tSessLeft);
        tOpus = Toggle(set.ShowOpus); Add(L.T("opus"), L.T("opus_sub"), tOpus);

        Section(L.T("sec_sessions"));
        tLong = Toggle(set.LongSessions); nWeeks = Step(set.SessionWeeks, 1, 26, tLong);
        Add(L.T("longs"), L.T("longs_sub"), tLong, nWeeks);
        nMin = Step(set.MinFive, 5, 90, null); Add(L.T("minfive"), L.T("minfive_sub"), nMin);
        tWeeks = Toggle(set.WeeksTab); Add(L.T("weekstab"), L.T("weekstab_sub"), tWeeks);
        tExport = Toggle(set.ExportMenu); Add(L.T("export"), L.T("export_sub"), tExport);

        Section(L.T("sec_icon"));
        preview = new Panel { BackColor = Color.Transparent, RightToLeft = RightToLeft.No };
        preview.Paint += PaintPreview;
        Add(L.T("preview"), L.T("preview_sub"), preview);
        tNum = Toggle(set.IconNumber); Add(L.T("num"), L.T("num_sub"), tNum);
        tBars = Toggle(set.IconNumberBars); Add(L.T("bars"), L.T("bars_sub"), tBars);
        tBars.Enabled = tNum.Checked; tNum.CheckedChanged += delegate { tBars.Enabled = tNum.Checked; };
        nYellow = Step(set.YellowAt, 0, 100, null); Add(L.T("yellow"), L.T("yellow_sub"), nYellow);
        nRed = Step(set.RedAt, 0, 100, null); Add(L.T("red"), null, nRed);
        tBlue = Toggle(set.BlueIcon); nBlue = Step(set.BlueAt, 1, 100, tBlue); Add(L.T("blue"), L.T("blue_sub"), tBlue, nBlue);
        foreach (Control c in new Control[] { tNum, tBars, tBlue }) ((ToggleSwitch)c).CheckedChanged += delegate { preview.Invalidate(); };
        foreach (var n in new[] { nYellow, nRed, nBlue }) n.ValueChanged += delegate { preview.Invalidate(); };

        Section(L.T("sec_general"));
        tAuto = Toggle(autostart); Add(L.T("autostart"), L.T("autostart_sub"), tAuto);
        nPoll = Step(set.PollMinutes, 1, 60, null); Add(L.T("poll"), L.T("poll_sub"), nPoll);
        tUpd = Toggle(set.AutoUpdate); Add(L.T("upd_auto"), L.T("upd_auto_sub", Updater.Version), tUpd);
        tSignIn = Toggle(set.AutoSignIn); Add(L.T("signin_auto"), L.T("signin_auto_sub"), tSignIn);
        cLang = new Picker { Font = fTitle, Size = new Size(S(210), S(32)) };
        cLang.Items.Add(L.T("lang_auto")); foreach (var nm in L.Names) cLang.Items.Add(nm);
        cLang.SelectedIndex = string.IsNullOrEmpty(set.Language) ? 0 : Math.Max(0, Array.IndexOf(L.Codes, set.Language) + 1);
        Add(L.T("language"), L.T("language_sub"), cLang);

        bottom = new Panel { Dock = DockStyle.Bottom, Height = S(68), BackColor = Color.FromArgb(27, 27, 27), RightToLeft = RightToLeft.No };
        bottom.Paint += (o, e) => { using (var p = new Pen(Color.FromArgb(48, 48, 48))) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0); };
        var ok = new ModernButton(L.T("save"), true) { DialogResult = DialogResult.OK, Size = new Size(S(120), S(36)) };
        var cancel = new ModernButton(L.T("cancel"), false) { DialogResult = DialogResult.Cancel, Size = new Size(S(120), S(36)) };
        bottom.Controls.Add(ok); bottom.Controls.Add(cancel);
        bottom.Resize += delegate
        {
            if (L.Rtl)
            {
                ok.Location = new Point(bottom.Width - S(22) - ok.Width, (bottom.Height - ok.Height) / 2);
                cancel.Location = new Point(ok.Left - S(10) - cancel.Width, ok.Top);
            }
            else
            {
                cancel.Location = new Point(bottom.Width - S(22) - cancel.Width, (bottom.Height - cancel.Height) / 2);
                ok.Location = new Point(cancel.Left - S(10) - ok.Width, cancel.Top);
            }
        };
        Controls.Add(scroll);
        Controls.Add(bottom);
        AcceptButton = ok; CancelButton = cancel;
    }

    public void ScrollToEnd() { scroll.AutoScrollPosition = new Point(0, inner.Height); }

    // ----- building blocks -----
    ToggleSwitch Toggle(bool on) { return new ToggleSwitch { Checked = on, Size = new Size(S(44), S(24)) }; }
    Stepper Step(double v, int min, int max, ToggleSwitch owner)
    {
        var n = new Stepper((int)Math.Round(v), min, max) { Size = new Size(S(112), S(32)), Font = fTitle };
        if (owner != null) { n.Enabled = owner.Checked; owner.CheckedChanged += delegate { n.Enabled = owner.Checked; }; }
        return n;
    }
    void Section(string title)
    {
        cur = new Sec { Head = new Label { Text = title, Font = fHead, ForeColor = Theme.Text, BackColor = Theme.Bg, AutoSize = false, TextAlign = ContentAlignment.BottomLeft, RightToLeft = L.Dir, UseMnemonic = false }, Card = new CardPanel { RightToLeft = RightToLeft.No } };
        inner.Controls.Add(cur.Head); inner.Controls.Add(cur.Card);
        secs.Add(cur);
    }
    // ctrls: first one sits at the far (left) edge, the next one to its right
    void Add(string title, string sub, params Control[] ctrls)
    {
        var r = new Row { P = new Panel { BackColor = Color.Transparent, RightToLeft = RightToLeft.No }, Ctrls = ctrls };
        r.Title = new Label { Text = title, Font = fTitle, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.TopLeft, RightToLeft = L.Dir, UseMnemonic = false };
        r.P.Controls.Add(r.Title);
        if (!string.IsNullOrEmpty(sub))
        {
            r.Sub = new Label { Text = sub, Font = fSub, ForeColor = Theme.Dim, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.TopLeft, RightToLeft = L.Dir, UseMnemonic = false };
            r.P.Controls.Add(r.Sub);
        }
        foreach (var c in ctrls) r.P.Controls.Add(c);
        var tg = ctrls.Length > 0 ? ctrls[0] as ToggleSwitch : null;
        if (tg != null)
        {
            EventHandler click = delegate { if (tg.Enabled) tg.Checked = !tg.Checked; };
            r.Title.Click += click; if (r.Sub != null) r.Sub.Click += click;
            r.Title.Cursor = Cursors.Hand; if (r.Sub != null) r.Sub.Cursor = Cursors.Hand;
            EventHandler dim = delegate { r.Title.ForeColor = tg.Enabled ? Theme.Text : Theme.Faint; if (r.Sub != null) r.Sub.ForeColor = tg.Enabled ? Theme.Dim : Theme.Faint; };
            tg.EnabledChanged += dim;
        }
        cur.Card.Controls.Add(r.P);
        cur.Rows.Add(r);
    }

    // places everything for the given content width, returns the content height
    int DoLayout(int width)
    {
        int pad = S(22), y = S(6), cardW = width - 2 * pad, inPad = S(18), gap = S(10);
        foreach (var sec in secs)
        {
            sec.Head.Bounds = new Rectangle(pad + S(4), y, cardW - S(8), S(34));
            y += S(40);
            int ry = 0;
            foreach (var r in sec.Rows)
            {
                int cw = 0;
                foreach (var c in r.Ctrls) cw += c.Width + (cw > 0 ? gap : 0);
                if (r.Ctrls.Length > 0 && r.Ctrls[0] == preview) { preview.Size = new Size(S(132), S(44)); cw = preview.Width; }
                int tw = cardW - 2 * inPad - cw - S(24);
                int th = Measure(r.Title.Text, fTitle, tw), sh = r.Sub != null ? Measure(r.Sub.Text, fSub, tw) : 0;
                int textH = th + (sh > 0 ? S(2) + sh : 0);
                int ch = 0; foreach (var c in r.Ctrls) ch = Math.Max(ch, c.Height);
                int h = Math.Max(S(60), Math.Max(textH, ch) + S(26));
                r.P.Bounds = new Rectangle(1, ry, cardW - 2, h);
                int ty = (h - textH) / 2;
                // RTL: text on the right, controls from the left edge; LTR: mirrored
                int tx = L.Rtl ? cardW - 2 - inPad - tw : inPad;
                r.Title.Bounds = new Rectangle(tx, ty, tw, th);
                if (r.Sub != null) r.Sub.Bounds = new Rectangle(tx, ty + th + S(2), tw, sh);
                int x = inPad;
                foreach (var c in r.Ctrls)
                {
                    int cx = L.Rtl ? x : cardW - 2 - x - c.Width;
                    c.Location = new Point(cx, (h - c.Height) / 2); x += c.Width + gap;
                }
                ry += h;
            }
            sec.Card.Bounds = new Rectangle(pad, y, cardW, ry);
            y += ry + S(14);
        }
        return y + S(8);
    }

    static int Measure(string t, Font f, int w)
    {
        return TextRenderer.MeasureText(t, f, new Size(w, 0), TextFormatFlags.WordBreak | (L.Rtl ? TextFormatFlags.RightToLeft : 0)).Height;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.DarkScrollbars(scroll);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int W = S(600), maxH = wa.Height - S(80) - bottom.Height;
        int h = DoLayout(W);
        if (h > maxH) h = DoLayout(W - SystemInformation.VerticalScrollBarWidth);
        inner.Size = new Size(W - (h > maxH ? SystemInformation.VerticalScrollBarWidth : 0), h);
        ClientSize = new Size(W, Math.Min(h, maxH) + bottom.Height);
        CenterToScreen();
    }

    void PaintPreview(object o, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, preview.Width - 1.5f, preview.Height - 1.5f);
        Theme.Fill(g, r, S(6), Color.FromArgb(28, 28, 28));
        Theme.Stroke(g, r, S(6), Theme.CardLine, 1f);
        double d = pUsage - pElapsed;
        double yel = nYellow.Value, red = Math.Max(nRed.Value, yel);
        Color c = d > red ? Theme.Red : d > yel ? Theme.Yellow : (tBlue.Checked && d < -nBlue.Value) ? Theme.Blue : Theme.Green;
        int[] sizes = { 16, 24, 32 };
        int x = S(12);
        foreach (int sz in sizes)
        {
            int n = S(sz);
            using (var b = IconArt.Tray(n, true, pUsage, pElapsed, c, IconArt.DiffText(d), tNum.Checked, tBars.Checked))
                g.DrawImage(b, x, (preview.Height - n) / 2, n, n);
            x += n + S(12);
        }
    }

    public void Apply()
    {
        st.Show5h = tFive.Checked;
        st.ShowSessionsLeft = tSessLeft.Checked;
        st.LongSessions = tLong.Checked; st.SessionWeeks = nWeeks.Value;
        st.MinFive = nMin.Value;
        st.WeeksTab = tWeeks.Checked;
        st.ShowOpus = tOpus.Checked;
        st.ExportMenu = tExport.Checked;
        st.YellowAt = nYellow.Value; st.RedAt = Math.Max(nRed.Value, st.YellowAt);
        st.BlueIcon = tBlue.Checked; st.BlueAt = nBlue.Value;
        st.IconNumber = tNum.Checked; st.IconNumberBars = tBars.Checked;
        st.PollMinutes = nPoll.Value;
        st.AutoUpdate = tUpd.Checked;
        st.AutoSignIn = tSignIn.Checked;
        st.Language = cLang.SelectedIndex <= 0 ? "" : L.Codes[cLang.SelectedIndex - 1];
    }
}

// ---------- tray app ----------
public class TrayApp : ApplicationContext
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ClaudeUsageTray";

    NotifyIcon ni = new NotifyIcon();
    Popup pop = new Popup();
    Settings st = Settings.Load();
    Control invoker = new Control();
    System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    System.Windows.Forms.Timer hide = new System.Windows.Forms.Timer();
    System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
    Point lastMovePos = new Point(-9999, -9999);
    DateTime lastMove = DateTime.MinValue, lastSuccess = DateTime.MinValue;
    string lastError;
    bool busy;
    DateTime busySince;
    string loggedError; // last error written to log.txt (same error is logged again only every 30 min)
    DateTime loggedErrorAt;
    int failCount;
    History hist = History.Load();
    HistoryForm histForm;
    ToolStripMenuItem miFive, miAuto, miExport, miUpdate;
    Updater.Info pendingUpdate; // newer version found while automatic updates are off
    bool updBusy;
    DateTime started = DateTime.Now;

    static readonly Color Green = Theme.Green, Yellow = Theme.Yellow, Red = Theme.Red, Blue = Theme.Blue;

    public TrayApp() : this(false) { }

    TrayApp(bool preview)
    {
        if (preview) return;
        invoker.CreateControl();
        var ignore = invoker.Handle;
        lastSuccess = Settings.S2D(st.LastFetch) ?? DateTime.MinValue;

        Program.Stage = "settings";
        ApplySettings();
        Program.Stage = "autostart check";
        if (!st.FirstRunDone) { SetAutostart(true); st.FirstRunDone = true; st.Save(); }
        else EnsureAutostart();

        Program.Stage = "menu";
        ni.ContextMenuStrip = BuildMenu();
        ni.Text = "";
        ni.MouseMove += delegate { lastMove = DateTime.Now; lastMovePos = Cursor.Position; if (!pop.Visible) { UpdateUi(); pop.ShowAt(Cursor.Position); } };
        ni.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) { pop.Hide(); ShowHistory(); } };
        ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { lastMove = DateTime.Now.AddSeconds(4); UpdateUi(); pop.ShowAt(Cursor.Position); } };

        hide.Interval = 200;
        hide.Tick += delegate
        {
            Interlocked.Exchange(ref Program.Beat, DateTime.UtcNow.Ticks);
            if (!pop.Visible) return;
            var c = Cursor.Position;
            // mouse resting on the icon sends no more moves: keep the popup while the cursor stays where the last icon move was
            if (Math.Abs(c.X - lastMovePos.X) <= 2 && Math.Abs(c.Y - lastMovePos.Y) <= 2) { lastMove = DateTime.Now; return; }
            if ((DateTime.Now - lastMove).TotalMilliseconds > 700 && !pop.Bounds.Contains(c)) pop.Hide();
        };
        hide.Start();

        poll.Interval = PollMs(false);
        poll.Tick += delegate { Refresh(); };
        poll.Start();

        tick.Interval = 60 * 1000; // elapsed time moves even without new data
        tick.Tick += delegate { hist.CloseExpired(DateTime.Now); UpdateUi(); AutoCheckUpdate(); };
        tick.Start();

        SystemEvents.PowerModeChanged += (s, e) =>
        {
            if (e.Mode == PowerModes.Resume) Settings.Log("resume from sleep");
            if (e.Mode == PowerModes.Resume) invoker.BeginInvoke((MethodInvoker)delegate
            {
                var t = new System.Windows.Forms.Timer { Interval = 20000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); Refresh(); };
                t.Start();
            });
        };

        WatchCredentials();
        pop.SignInClick += delegate { pop.Hide(); SignIn(); };

        Program.Stage = "icon";
        UpdateUi();
        ni.Visible = true;
        Program.Stage = "first fetch";
        Refresh();
        Settings.Log("ready, icon shown");
    }

    ToolStripLabel miHead;

    ContextMenuStrip BuildMenu()
    {
        float sc; using (var g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
        int ic = (int)Math.Round(16 * sc);
        var rnd = new ModernMenuRenderer();
        var menu = new ContextMenuStrip
        {
            RightToLeft = L.Dir, Renderer = rnd, Font = new Font("Segoe UI", 10f),
            ImageScalingSize = new Size(ic, ic), ShowCheckMargin = false, ShowImageMargin = true,
            BackColor = Theme.MenuBg, ForeColor = Theme.Text, Padding = new Padding(0, (int)(5 * sc), 0, (int)(5 * sc))
        };
        menu.HandleCreated += delegate { rnd.DwmBorder = Theme.RoundAndBorder(menu.Handle, Theme.MenuLine, false); };
        Padding ip = new Padding(0, (int)(5 * sc), 0, (int)(5 * sc));
        Func<string, char, EventHandler, ToolStripMenuItem> item = (text, glyph, click) =>
        {
            var m = new ToolStripMenuItem(text, Theme.Glyph(glyph, ic, Theme.Text), click) { Padding = ip };
            menu.Items.Add(m);
            return m;
        };
        string room = new string(' ', 16); // blank shortcut text = room for the switch

        miHead = new ToolStripLabel(L.T("head")) { Tag = "head", Padding = new Padding((int)(8 * sc), (int)(4 * sc), (int)(8 * sc), (int)(6 * sc)), Font = new Font("Segoe UI Semibold", 9f) };
        menu.Items.Add(miHead);
        item(L.T("refresh"), '', delegate { Refresh(); });
        miUpdate = item(L.T("upd_check"), '', delegate { CheckUpdate(true); });
        item(L.T("history"), '', delegate { ShowHistory(); });
        miExport = item(L.T("export") + "…", '', delegate { ExportHistory(); });
        miExport.Visible = st.ExportMenu;
        item(L.T("manual_title") + "…", '', delegate { ManualEntry(); });
        item(L.T("open_page"), '', delegate { try { System.Diagnostics.Process.Start("https://claude.ai/settings/usage"); } catch { } });
        menu.Items.Add(new ToolStripSeparator());
        miFive = item(L.T("five"), '', delegate { st.Show5h = miFive.Checked; st.Save(); UpdateUi(); });
        miFive.CheckOnClick = true; miFive.Checked = st.Show5h; miFive.ShortcutKeyDisplayString = room;
        miAuto = item(L.T("autostart"), '', delegate { SetAutostart(miAuto.Checked); miAuto.Checked = IsAutostart(); });
        miAuto.CheckOnClick = true; miAuto.Checked = IsAutostart(); miAuto.ShortcutKeyDisplayString = room;
        item(L.T("settings") + "…", '', delegate { ShowSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        item(L.T("exit"), '', delegate { ni.Visible = false; Application.Exit(); });
        menu.Opening += delegate
        {
            pop.Hide();
            var v = Compute();
            miHead.Text = v.HasData
                ? L.T("head_data", L.Pct(v.Usage), L.Pct(v.Elapsed))
                : L.T("head_nodata");
            miFive.Checked = st.Show5h;
            miFive.Visible = !st.NoFive;
            miAuto.Checked = IsAutostart();
        };
        return menu;
    }

    // ---------- new sign-in through Claude Code ----------
    bool loginNeeded, autoSignInDone;
    System.Diagnostics.Process signInProc;

    static string ClaudeCli()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(p)) return p;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                foreach (var n in new[] { "claude.exe", "claude.cmd" })
                {
                    string f = Path.Combine(dir.Trim().Trim('"'), n);
                    if (File.Exists(f)) return f;
                }
            }
            catch { }
        }
        return null;
    }

    // Opens a console with "claude auth login": the browser asks to confirm, then the window closes by itself.
    // The file watcher sees the new sign-in and fetches again.
    void SignIn()
    {
        try
        {
            if (signInProc != null && !signInProc.HasExited) return; // already open
            string cli = ClaudeCli();
            if (cli == null)
            {
                Settings.Log("sign-in: claude CLI not found");
                ni.ShowBalloonTip(6000, "Claude Usage Tray", L.T("signin_missing"), ToolTipIcon.Warning);
                return;
            }
            // on failure the window stays open so the message can be read
            // cmd /c "…": cmd strips only the outer quote pair, so the inner quoted path stays intact
            string cmd = "/c \"title Claude Code & \"" + cli + "\" auth login || pause\"";
            signInProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true });
            Settings.Log("sign-in window opened: " + cli);
        }
        catch (Exception ex) { Settings.Log("sign-in start failed: " + ex.Message); }
    }

    // When Claude Code writes a new sign-in, retry at once instead of waiting for the slow error poll.
    FileSystemWatcher credWatch;
    System.Windows.Forms.Timer credDelay;
    void WatchCredentials()
    {
        try
        {
            string dir = Path.GetDirectoryName(Api.CredFile);
            if (!Directory.Exists(dir)) return;
            credDelay = new System.Windows.Forms.Timer { Interval = 3000 }; // the file is written in several steps
            credDelay.Tick += delegate
            {
                credDelay.Stop();
                if (lastError == null) return; // working fine, nothing to fix
                Settings.Log("sign-in file changed -> retry now");
                Refresh();
            };
            credWatch = new FileSystemWatcher(dir, Path.GetFileName(Api.CredFile));
            credWatch.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            FileSystemEventHandler h = (s, e) => invoker.BeginInvoke((MethodInvoker)delegate { credDelay.Stop(); credDelay.Start(); });
            credWatch.Changed += h; credWatch.Created += h;
            credWatch.Renamed += (s, e) => h(s, e);
            credWatch.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Settings.Log("sign-in file watch failed: " + ex.Message); }
    }

    void Refresh()
    {
        if (busy)
        {
            // a fetch that never returns must not stop all later updates
            if ((DateTime.Now - busySince).TotalMinutes < 2) return;
            Settings.Log("fetch still running after 2 min -> starting a new one");
        }
        busy = true;
        busySince = DateTime.Now;
        Task.Factory.StartNew(() => Api.Fetch()).ContinueWith(t =>
        {
            // a crash inside Fetch must not leave "busy" stuck (that would stop all later updates)
            var r = t.IsFaulted ? new Api.Result { Error = L.T("read_fail") } : t.Result;
            if (t.IsFaulted) Settings.Log("fetch crashed: " + t.Exception.GetBaseException().Message);
            invoker.BeginInvoke((MethodInvoker)delegate
            {
                busy = false;
                if (r.Error == null)
                {
                    st.LastWeekPct = r.Week;
                    st.LastWeekReset = r.WeekReset.HasValue ? Settings.D2S(r.WeekReset.Value) : null;
                    st.LastFivePct = r.Five;
                    st.LastFiveReset = r.FiveReset.HasValue ? Settings.D2S(r.FiveReset.Value) : null;
                    lastSuccess = DateTime.Now;
                    st.LastFetch = Settings.D2S(lastSuccess);
                    lastError = null;
                    loginNeeded = false; autoSignInDone = false;
                    if (failCount > 0) Settings.Log("fetch ok again after " + failCount + " failed tries");
                    failCount = 0; loggedError = null;
                    st.LastOpusPct = r.Opus;
                    st.NoFive = r.Five == null && r.Week != null;
                    st.Save();
                    hist.Add(lastSuccess, r.Week ?? 0, r.Five, r.FiveReset, r.WeekReset);
                    poll.Interval = PollMs(false);
                }
                else
                {
                    lastError = r.Error;
                    loginNeeded = r.LoginNeeded;
                    // automatic option: open the sign-in once per expiry, not on every poll
                    if (loginNeeded && st.AutoSignIn && !autoSignInDone) { autoSignInDone = true; SignIn(); }
                    failCount++;
                    if (r.Error != loggedError || (DateTime.Now - loggedErrorAt).TotalMinutes >= 30)
                    {
                        Settings.Log("fetch failed (" + failCount + "): " + r.Error + (r.Busy ? " [slow retry]" : ""));
                        loggedError = r.Error; loggedErrorAt = DateTime.Now;
                    }
                    poll.Interval = PollMs(r.Busy);
                }
                UpdateUi();
            });
        });
    }

    // ---------- self-update ----------
    public void AnnounceUpdate()
    {
        ni.ShowBalloonTip(5000, "Claude Usage Tray", L.T("upd_done", Updater.Version), ToolTipIcon.Info);
    }

    // minute tick: one check a day (first one 2 min after start), install by itself when switched on
    void AutoCheckUpdate()
    {
        if (updBusy || (DateTime.Now - started).TotalMinutes < 2) return;
        DateTime last = Settings.S2D(st.LastUpdateCheck) ?? DateTime.MinValue;
        if ((DateTime.Now - last).TotalHours < 24) return;
        CheckUpdate(false);
    }

    void CheckUpdate(bool manual)
    {
        if (updBusy) return;
        if (manual && pendingUpdate != null) { AskInstall(pendingUpdate); return; }
        updBusy = true;
        Task.Factory.StartNew(() => Updater.Check()).ContinueWith(t =>
        {
            var info = t.IsFaulted ? new Updater.Info { Error = t.Exception.GetBaseException().Message } : t.Result;
            invoker.BeginInvoke((MethodInvoker)delegate
            {
                updBusy = false;
                if (info.Error == null) { st.LastUpdateCheck = Settings.D2S(DateTime.Now); st.Save(); }
                if (info.Error != null)
                {
                    if (manual) MessageBox.Show(L.T("upd_fail", info.Error), "Claude Usage Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!info.Newer)
                {
                    if (manual) MessageBox.Show(L.T("upd_latest", Updater.Version), "Claude Usage Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Settings.Log("update found: " + info.Version);
                if (manual) AskInstall(info);
                else if (st.AutoUpdate && openDialog == null) Install(info, false);
                else
                {
                    pendingUpdate = info;
                    miUpdate.Text = L.T("upd_install", info.Version);
                    ni.ShowBalloonTip(5000, "Claude Usage Tray", L.T("upd_found", info.Version), ToolTipIcon.Info);
                }
            });
        });
    }

    void AskInstall(Updater.Info info)
    {
        string notes = (info.Notes ?? "").Trim();
        if (notes.Length > 700) notes = notes.Substring(0, 700) + "…";
        var opts = L.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        if (MessageBox.Show(L.T("upd_ask", info.Version, Updater.Version, notes), "Claude Usage Tray",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, opts) == DialogResult.Yes)
            Install(info, true);
    }

    void Install(Updater.Info info, bool manual)
    {
        updBusy = true;
        Task.Factory.StartNew(() => { string p; string e = Updater.Download(info, out p); return e ?? Updater.Install(p); }).ContinueWith(t =>
        {
            string err = t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result;
            invoker.BeginInvoke((MethodInvoker)delegate
            {
                updBusy = false;
                if (err == null) { ni.Visible = false; Application.Exit(); return; } // the new version is already starting
                if (manual) MessageBox.Show(L.T("upd_fail", err), "Claude Usage Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                {
                    pendingUpdate = info; // let the user try from the menu
                    miUpdate.Text = L.T("upd_install", info.Version);
                }
            });
        });
    }

    int PollMs(bool slow) { return Math.Max(1, st.PollMinutes) * (slow ? 2 : 1) * 60 * 1000; }

    void ApplySettings()
    {
        History.MinFive = st.MinFive;
        History.KeepDays = st.LongSessions ? Math.Max(1, st.SessionWeeks) * 7 : 3;
        if (miExport != null) miExport.Visible = st.ExportMenu;
        poll.Interval = PollMs(false);
    }

    Form openDialog; // only one settings / manual window at a time

    bool FocusOpenDialog()
    {
        if (openDialog == null || openDialog.IsDisposed) return false;
        openDialog.Activate();
        return true;
    }

    void ShowSettings()
    {
        pop.Hide();
        if (FocusOpenDialog()) return;
        var v = Compute();
        bool auto = IsAutostart();
        using (var f = new SettingsForm(st, v.HasData ? v.Usage : 62, v.HasData ? v.Elapsed : 55, auto))
        {
            openDialog = f;
            var res = f.ShowDialog();
            openDialog = null;
            if (res != DialogResult.OK) return;
            string oldLang = st.Language;
            f.Apply();
            st.Save();
            if (st.Language != oldLang) ApplyLanguage();
            if (f.Autostart != auto) SetAutostart(f.Autostart);
            if (miFive != null) miFive.Checked = st.Show5h;
            if (miAuto != null) miAuto.Checked = IsAutostart();
            ApplySettings();
            UpdateUi();
        }
    }

    void ExportHistory()
    {
        using (var d = new SaveFileDialog { Title = L.T("export_title"), Filter = L.T("csv_filter"), FileName = "claude-usage-" + DateTime.Now.ToString("yyyy-MM-dd") })
        {
            if (d.ShowDialog() != DialogResult.OK) return;
            try
            {
                string b = Path.Combine(Path.GetDirectoryName(d.FileName), Path.GetFileNameWithoutExtension(d.FileName));
                var files = hist.ExportCsv(b);
                MessageBox.Show(L.T("saved3") + "\n" + string.Join("\n", files), L.T("export_title"), MessageBoxButtons.OK, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, L.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
            }
            catch (Exception ex) { MessageBox.Show(L.T("save_fail") + ex.Message); }
        }
    }

    DateTime NextFallbackReset(DateTime now)
    {
        var d = now.Date.AddHours(st.ResetHour);
        while ((int)d.DayOfWeek != st.ResetDay || d <= now) d = d.AddDays(1);
        return d;
    }

    View Compute()
    {
        var now = DateTime.Now;
        var v = new View { Show5h = st.Show5h };
        DateTime? apiReset = Settings.S2D(st.LastWeekReset);
        DateTime end = apiReset ?? NextFallbackReset(now);
        while (end <= now) end = end.AddDays(7);
        DateTime start = end.AddDays(-7);

        // pick the newest valid value: server cache or manual entry
        double? usage = null; DateTime when = DateTime.MinValue; bool manual = false;
        DateTime fetched = Settings.S2D(st.LastFetch) ?? DateTime.MinValue;
        if (st.LastWeekPct.HasValue && fetched >= start && (!apiReset.HasValue || apiReset.Value > now)) { usage = st.LastWeekPct; when = fetched; }
        DateTime? mAt = Settings.S2D(st.ManualAt);
        if (st.ManualPercent.HasValue && mAt.HasValue && mAt.Value >= start && mAt.Value > when) { usage = st.ManualPercent; when = mAt.Value; manual = true; }

        v.Error = lastError;
        v.LoginNeeded = loginNeeded && lastError != null;
        if (!usage.HasValue) return v;
        v.HasData = true;
        v.Usage = usage.Value;
        v.Elapsed = Math.Max(0, Math.Min(100, (now - start).TotalMinutes / (7 * 24 * 60) * 100));
        var left = end - now;
        v.Remaining = L.T("remaining", Span(left), L.DayName((int)end.DayOfWeek), end.AddSeconds(30).ToString("HH:mm"));
        v.Updated = when.ToString("HH:mm");
        if (manual) v.Note = L.T("manual_note", when.ToString("HH:mm"));
        else if ((now - when).TotalMinutes > 45) v.Note = L.T("stale", L.Span(now - when, true));

        double d = v.Usage - v.Elapsed;
        v.StatusColor = d > st.RedAt ? Red : d > st.YellowAt ? Yellow : (st.BlueIcon && d < -st.BlueAt) ? Blue : Green;
        v.IconText = IconArt.DiffText(d);
        // fetching has stopped (not just one failed try): gray icon so it is clear the number is not live
        if (lastError != null && (now - lastSuccess).TotalMinutes > 10) v.StatusColor = Theme.Gray;
        if (d > 1) v.Status = L.T("ahead", L.Pct(d));
        else if (d < -1) v.Status = L.T("behind", L.Pct(-d));
        else v.Status = L.T("ontrack");

        double rest = Math.Max(0, 100 - v.Usage);
        if (rest <= 0) v.Budget = L.T("used_all");
        else if (left.TotalDays < 1) v.Budget = L.T("left_until", L.Pct(rest));
        else v.Budget = L.T("per_day", L.Pct(rest / left.TotalDays));

        if (st.LastFivePct.HasValue && !manual)
        {
            DateTime? fr = Settings.S2D(st.LastFiveReset);
            if (fr.HasValue && fr.Value > now) { v.Five = st.LastFivePct; v.FiveReset = L.T("five_reset", L.Span(fr.Value - now, true)); }
            else if (!fr.HasValue) v.Five = st.LastFivePct;
            else v.Five = 0; // window already renewed
        }
        if (st.ShowOpus && st.LastOpusPct.HasValue && !manual && when == fetched) v.Opus = st.LastOpusPct;

        if (st.NoFive) return v; // plan without 5-hour limit: no session numbers
        int n;
        double? avg = hist.Average(out n);
        if (avg.HasValue && st.ShowSessionsLeft && rest > 0 && avg.Value > 0)
            v.SessionsLeft = L.T("sessions_left", L.FullSessions(rest / avg.Value));
        if (avg.HasValue) v.Ratio = L.T("ratio", L.PctN(History.N(avg.Value)), L.Sessions(n));
        var c = hist.Cur;
        if (c != null && c.Valid && c.Counts) v.RatioNow = L.T("ratio_now", L.PctN(History.N(c.WeekDelta)));
        else if (!avg.HasValue) v.Ratio = L.T("ratio_wait", L.PctN(History.N(History.MinFive)));
        return v;
    }

    // rebuild everything that holds text after the language was changed
    void ApplyLanguage()
    {
        L.Set(st.Language);
        var old = ni.ContextMenuStrip;
        ni.ContextMenuStrip = BuildMenu();
        if (old != null) old.Dispose();
        if (histForm != null && !histForm.IsDisposed) { histForm.Close(); ShowHistory(); }
    }

    void ShowHistory()
    {
        if (histForm != null && !histForm.IsDisposed) histForm.Close();
        histForm = new HistoryForm(hist, st.WeeksTab, st.NoFive);
        histForm.Show();
        histForm.Activate();
    }

    static string Span(TimeSpan t) { return L.Span(t); }

    void UpdateUi()
    {
        var v = Compute();
        pop.V = v;
        if (pop.Visible) pop.Invalidate();
        SetIcon(v);
    }

    void SetIcon(View v)
    {
        int n = SystemInformation.SmallIconSize.Width;
        using (var bmp = IconArt.Tray(n, v.HasData, v.Usage, v.Elapsed, v.StatusColor, v.IconText, st.IconNumber, st.IconNumberBars))
        {
            IntPtr h = bmp.GetHicon();
            var old = ni.Icon;
            ni.Icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            if (old != null) old.Dispose();
        }
    }

    // --png <file>: popup, icon variants and menu as pictures, for checks without the taskbar
    public static void Preview(string path)
    {
        var app = new TrayApp(true);
        if (Environment.GetCommandLineArgs().Length > 4) // "--png <file> <lang> demo": made-up numbers for public screenshots
        {
            var now = DateTime.Now;
            app.st = new Settings { Language = app.st.Language, LastWeekPct = 64, LastFivePct = 38, LastFetch = Settings.D2S(now),
                LastWeekReset = Settings.D2S(now.AddDays(2).AddHours(6)), LastFiveReset = Settings.D2S(now.AddHours(2).AddMinutes(40)) };
            app.hist = new History();
            if (Environment.GetCommandLineArgs()[4] == "signin") // "--png <file> <lang> signin": expired sign-in look
            { app.lastError = L.T("login_expired"); app.loginNeeded = true; app.lastSuccess = now.AddHours(-2); }
        }
        app.UpdateUi();
        using (var b = app.pop.Snapshot()) b.Save(path);
        app.ni.Icon.ToBitmap().Save(Path.ChangeExtension(path, ".icon.png"));
        var v = app.Compute();
        if (!v.HasData) { v.HasData = true; v.Usage = 62; v.Elapsed = 55; v.StatusColor = Theme.Yellow; v.IconText = "+7"; }
        int[] sizes = { 16, 20, 24, 32 };
        var looks = new[] { new[] { true, true }, new[] { true, false }, new[] { false, false } };
        Color[] cols = { Theme.Green, Theme.Yellow, Theme.Red, Theme.Blue };
        string[] txt = { "\u22125", "+7", "+14", "\u221218" };
        using (var sheet = new Bitmap(4 * 4 * 40 + 20, 3 * 44 + 60))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(32, 32, 32));
            for (int l = 0; l < looks.Length; l++)
                for (int c = 0; c < cols.Length; c++)
                    for (int s = 0; s < sizes.Length; s++)
                        using (var ic = IconArt.Tray(sizes[s], true, 62, 55, cols[c], txt[c], looks[l][0], looks[l][1]))
                            g.DrawImageUnscaled(ic, 10 + (c * 4 + s) * 40, 10 + l * 44);
            for (int s = 0; s < sizes.Length; s++)
                using (var ic = IconArt.App(sizes[s])) g.DrawImageUnscaled(ic, 10 + s * 40, 10 + 3 * 44);
            using (var ic = IconArt.App(48)) g.DrawImageUnscaled(ic, 10 + 4 * 40, 10 + 3 * 44 - 8);
            sheet.Save(Path.ChangeExtension(path, ".icons.png"));
        }
        var menu = app.BuildMenu();
        menu.Show(new Point(-3000, 0));
        Application.DoEvents();
        using (var b = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(b, new Rectangle(0, 0, menu.Width, menu.Height)); b.Save(Path.ChangeExtension(path, ".menu.png")); }
        menu.Close();
        app.ni.Dispose();
    }

    public static void SaveIco(string path) { File.WriteAllBytes(path, IconArt.AppIco()); }

    void ManualEntry()
    {
        pop.Hide();
        if (FocusOpenDialog()) return;
        var v = Compute();
        using (var f = new ManualForm(v.HasData ? v.Usage : 0))
        {
            openDialog = f;
            var res = f.ShowDialog();
            openDialog = null;
            if (res == DialogResult.OK)
            {
                st.ManualPercent = (double)f.Num.Value;
                st.ManualAt = Settings.D2S(DateTime.Now);
                st.Save();
                UpdateUi();
            }
        }
    }

    // Windows (newer 11 builds) skips Run entries that have no "enabled" mark in StartupApproved,
    // so autostart writes both. A Startup-folder shortcut is a second path (mutex prevents doubles).
    const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    const string LinkName = "Claude Usage Tray.lnk";
    static readonly byte[] Enabled = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    static string LinkPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName); } }

    internal static bool IsAutostart()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            if (k == null || k.GetValue(RunName) == null) return false;
        using (var a = Registry.CurrentUser.OpenSubKey(ApprovedRun))
        {
            var v = a == null ? null : a.GetValue(RunName) as byte[];
            return v == null || v.Length == 0 || v[0] == 2 || v[0] == 6;
        }
    }

    // Called on every start: repairs a missing approval mark, but respects "disabled" set by the owner in Task Manager.
    static void EnsureAutostart()
    {
        if (!IsAutostart()) return;
        bool missing;
        using (var a = Registry.CurrentUser.OpenSubKey(ApprovedRun)) missing = a == null || a.GetValue(RunName) == null;
        if (missing || !File.Exists(LinkPath)) SetAutostart(true);
    }

    internal static void SetAutostart(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunName, false);
            }
            using (var a = Registry.CurrentUser.CreateSubKey(ApprovedRun))
            {
                if (on) a.SetValue(RunName, Enabled, RegistryValueKind.Binary);
                else a.DeleteValue(RunName, false);
            }
            using (var a = Registry.CurrentUser.CreateSubKey(ApprovedFolder))
            {
                if (on) a.SetValue(LinkName, Enabled, RegistryValueKind.Binary);
                else a.DeleteValue(LinkName, false);
            }
            if (on) CreateShortcut(LinkPath, Application.ExecutablePath);
            else if (File.Exists(LinkPath)) File.Delete(LinkPath);
        }
        catch (Exception ex) { Settings.Log("autostart error: " + ex.Message); }
    }

    static void CreateShortcut(string lnk, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
        var st = sc.GetType();
        st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
        Marshal.ReleaseComObject(sc);
        Marshal.ReleaseComObject(sh);
    }
}
