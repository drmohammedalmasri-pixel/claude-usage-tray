// History: one sample per poll (kept 3 days) + how much of the week a 5-hour session costs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Sample { public string T; public double W; public double F; public string FR; }

public class Session
{
    public string Key, Start, End;
    public double FiveFrom, FiveMax, WeekFrom, WeekTo;
    public double WeekCarry; // weekly increase before a weekly reset that fell inside this session
    public bool Valid;
    public double FiveDelta { get { return FiveMax - FiveFrom; } }
    public double WeekDelta { get { return WeekCarry + WeekTo - WeekFrom; } }
    public bool Counts { get { return Valid && FiveDelta >= History.MinFive; } }
    public double Ratio { get { return FiveDelta > 0 ? WeekDelta / FiveDelta * 100 : 0; } } // % of week per full 5h session
}

public class WeekRec { public string End, TopDay; public double Used, TopGain; public int Big; }

public class WeekAcc
{
    public string Key, End;
    public double LastW, MaxW;
    public int Big;
    public Dictionary<string, double> Days = new Dictionary<string, double>();
}

public class History
{
    public static double MinFive = 35; // smaller sessions are too rough (weekly % moves in whole steps); set from Settings
    public static int KeepDays = 56;   // how far back the average reaches; set from Settings
    public List<Sample> Samples = new List<Sample>();
    public List<Session> Sessions = new List<Session>();
    public List<WeekRec> Weeks = new List<WeekRec>();
    public WeekAcc Wk;
    public Session Cur;

    static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray", "history.json"); } }

    public static History Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // an ended session is closed by the first minute tick, after the settings (MinFive) are applied
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<History>(File.ReadAllText(FilePath));
            }
        }
        catch (Exception ex) { Settings.Log("history load: " + ex.Message); }
        return new History();
    }

    [ScriptIgnore] public bool NoSave;

    // --test <png-base>: synthetic data, real file untouched. Expected: 3 sessions, average = (6+3+6)/(60+40+50) = 10 %.
    public static void SelfTest(string pngBase)
    {
        var h = new History { NoSave = true };
        DateTime day = DateTime.Today.AddHours(7);
        double w = 40;
        Action<DateTime, DateTime, double, double> sim = (from, reset, fiveTo, weekGain) =>
        {
            int steps = 24;
            double w0 = w;
            for (int i = 0; i <= steps; i++)
            {
                double five = fiveTo * i / steps;
                w = Math.Floor(w0 + weekGain * i / steps); // weekly % moves in whole steps
                h.Add(from.AddMinutes(10 * i), w, five, reset);
            }
            w = w0 + weekGain;
        };
        h.Add(day.AddMinutes(-10), w, 0, null);
        sim(day, day.AddHours(5), 60, 6);
        h.Add(day.AddHours(5).AddMinutes(10), w, 0, null);
        sim(day.AddHours(5).AddMinutes(20), day.AddHours(10).AddMinutes(20), 30, 3);   // below threshold → ignored
        h.Add(day.AddHours(10).AddMinutes(30), w, 0, null);
        sim(day.AddHours(10).AddMinutes(40), day.AddHours(15).AddMinutes(40), 40, 3);
        h.Add(day.AddHours(15).AddMinutes(50), w, 0, null);
        // session across the weekly reset: +4 before it (w 46 -> 50), new week 0 -> 2, 5h 50 % -> counts +6
        DateTime s4 = day.AddHours(16), r4 = s4.AddHours(5);
        double[] wk4 = { 46, 48, 50, 0, 1, 2 }, f4 = { 0, 10, 20, 30, 40, 50 };
        for (int i = 0; i < wk4.Length; i++) h.Add(s4.AddMinutes(10 * i), wk4[i], f4[i], r4);
        h.CloseExpired(r4.AddMinutes(1));
        int n; double? a = h.Average(out n);
        File.WriteAllText(pngBase + ".txt", "sessions=" + n + " avg=" + (a.HasValue ? N(a.Value) : "none") + " samples=" + h.Samples.Count);
        using (var f = new HistoryForm(h))
        {
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, 0);
            f.Show();
            var tabs = (TabControl)f.Controls[0];
            for (int i = 0; i < 2; i++)
            {
                tabs.SelectedIndex = i; Application.DoEvents();
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(pngBase + "-tab" + i + ".png"); }
            }
        }
    }

    public void Save()
    {
        if (NoSave) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(this));
            File.Copy(tmp, FilePath, true);
            File.Delete(tmp);
        }
        catch (Exception ex) { Settings.Log("history save: " + ex.Message); }
    }

    public void Add(DateTime t, double w, double? f, DateTime? fr, DateTime? wr = null)
    {
        Sample prev = Samples.Count > 0 ? Samples[Samples.Count - 1] : null;
        double fv = f ?? 0;
        Samples.Add(new Sample { T = Settings.D2S(t), W = w, F = fv, FR = fr.HasValue ? Settings.D2S(fr.Value) : null });
        DateTime cutoff = t.AddDays(-3);
        Samples.RemoveAll(x => (Settings.S2D(x.T) ?? DateTime.MinValue) < cutoff);
        Track(t, w, fv, fr, prev);
        TrackWeek(t, w, wr);
        // counted sessions are kept for good (owner 10.11); KeepDays only limits the average
        if (Sessions.Count > 5000) Sessions.RemoveRange(0, Sessions.Count - 5000);
        Save();
    }

    // one summary line per finished week: total used, big sessions, busiest day
    void TrackWeek(DateTime t, double w, DateTime? wr)
    {
        if (!wr.HasValue) return;
        string key = wr.Value.AddMinutes(30).ToString("yyyyMMddHH");
        if (Wk != null && Wk.Key != key)
        {
            var rec = new WeekRec { End = Wk.End, Used = Wk.MaxW, Big = Wk.Big };
            foreach (var kv in Wk.Days) if (kv.Value > rec.TopGain) { rec.TopGain = kv.Value; rec.TopDay = kv.Key; }
            Weeks.Add(rec);
            if (Weeks.Count > 104) Weeks.RemoveRange(0, Weeks.Count - 104);
            Wk = null;
        }
        if (Wk == null) { Wk = new WeekAcc { Key = key, End = Settings.D2S(wr.Value), LastW = w, MaxW = w }; return; }
        double d = w - Wk.LastW;
        if (d > 0)
        {
            string day = t.ToString("yyyy-MM-dd");
            double cur; Wk.Days.TryGetValue(day, out cur);
            Wk.Days[day] = cur + d;
        }
        Wk.LastW = w;
        Wk.MaxW = Math.Max(Wk.MaxW, w);
    }

    // CSV files for Excel (UTF-8 with BOM): <base>-readings, <base>-sessions, <base>-weeks
    public string[] ExportCsv(string basePath)
    {
        var enc = new System.Text.UTF8Encoding(true);
        string a = basePath + "-readings.csv", b = basePath + "-sessions.csv", c = basePath + "-weeks.csv";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("time,weekly_percent,five_hour_percent");
        foreach (var x in Samples) sb.AppendLine((Settings.S2D(x.T) ?? DateTime.MinValue).ToString("yyyy-MM-dd HH:mm") + "," + Inv(x.W) + "," + Inv(x.F));
        File.WriteAllText(a, sb.ToString(), enc);
        sb = new System.Text.StringBuilder();
        sb.AppendLine("start,end,five_hour_used,week_increase,full_session_percent_of_week");
        foreach (var x in Sessions) sb.AppendLine(When(x.Start) + "," + When(x.End) + "," + Inv(x.FiveDelta) + "," + Inv(x.WeekDelta) + "," + Inv(x.Ratio));
        File.WriteAllText(b, sb.ToString(), enc);
        sb = new System.Text.StringBuilder();
        sb.AppendLine("week_end,used_percent,big_sessions,busiest_day,busiest_day_gain");
        foreach (var x in Weeks) sb.AppendLine(When(x.End) + "," + Inv(x.Used) + "," + x.Big + "," + x.TopDay + "," + Inv(x.TopGain));
        File.WriteAllText(c, sb.ToString(), enc);
        return new[] { a, b, c };
    }

    static string Inv(double v) { return Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture); }
    public static string When(string iso) { var d = Settings.S2D(iso); return d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm") : ""; }

    void Track(DateTime t, double w, double f, DateTime? fr, Sample prev)
    {
        // a 5-hour window is identified by its reset time
        string key = (fr.HasValue && fr.Value > t) ? fr.Value.AddSeconds(30).ToString("yyyyMMddHHmm") : null;
        if (Cur != null && Cur.Key != key) Finish();
        if (key == null) return;
        if (Cur == null)
        {
            // previous sample recent → start from it (5h was 0 before this window)
            DateTime? pt = prev == null ? null : Settings.S2D(prev.T);
            bool linked = pt.HasValue && (t - pt.Value).TotalMinutes <= 20 && prev.W <= w + 0.01;
            Cur = new Session
            {
                Key = key, Start = Settings.D2S(linked ? pt.Value : t), End = Settings.D2S(t),
                FiveFrom = linked ? 0 : f, FiveMax = f, WeekFrom = linked ? prev.W : w, WeekTo = w, Valid = true
            };
            return;
        }
        if (w < Cur.WeekTo - 0.5)
        {
            // weekly reset inside the session: keep the part before it, the new week starts at 0
            Cur.WeekCarry += Cur.WeekTo - Cur.WeekFrom;
            Cur.WeekFrom = 0;
        }
        Cur.FiveMax = Math.Max(Cur.FiveMax, f);
        Cur.WeekTo = w;
        Cur.End = Settings.D2S(t);
    }

    // The running session moves to the list as soon as its 5-hour window has ended,
    // even when no new reading comes (PC asleep, app not fetching). Returns true if one was closed.
    public bool CloseExpired(DateTime now)
    {
        if (Cur == null) return false;
        DateTime end;
        if (!DateTime.TryParseExact(Cur.Key, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out end)) return false;
        if (now < end) return false;
        Finish();
        Save();
        return true;
    }

    void Finish()
    {
        if (Cur != null && Cur.Counts) { Sessions.Add(Cur); if (Wk != null) Wk.Big++; }
        Cur = null;
    }

    // weighted average: total weekly increase / total 5h usage of all counted sessions
    public double? Average(out int n)
    {
        double sw = 0, sf = 0; n = 0;
        DateTime lim = DateTime.Now.AddDays(-KeepDays);
        foreach (var x in Sessions) if (x.Counts && (Settings.S2D(x.End) ?? DateTime.Now) >= lim) { sw += x.WeekDelta; sf += x.FiveDelta; n++; }
        if (n == 0 || sf <= 0) return null;
        return sw / sf * 100;
    }

    public static string N(double v) { return Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture); }
}

public class HistoryForm : Form
{
    History h; float s; bool noFive;
    int S(float px) { return (int)Math.Round(px * s); }
    static readonly Color WeekC = Color.FromArgb(60, 110, 200), FiveC = Color.FromArgb(150, 110, 200);

    public HistoryForm(History hist, bool weeksTab = false, bool noFivePlan = false)
    {
        h = hist; noFive = noFivePlan;
        using (var g = CreateGraphics()) s = g.DpiX / 96f;
        Text = L.T("hist_title");
        Icon = IconArt.AppIcon;
        RightToLeft = L.Dir;
        RightToLeftLayout = L.Rtl;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(S(780), S(620));
        MinimumSize = new Size(S(500), S(400));

        var tabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = L.Rtl, Padding = new Point(S(14), S(5)) };
        tabs.TabPages.Add(SessionsPage());
        string[] names = { L.T("today"), L.T("yesterday"), L.T("daybefore") };
        for (int d = 0; d < 3; d++) tabs.TabPages.Add(DayPage(DateTime.Today.AddDays(-d), names[d]));
        if (weeksTab) tabs.TabPages.Add(WeeksPage());
        Controls.Add(tabs);
    }

    TabPage WeeksPage()
    {
        var p = new TabPage(L.T("weeks_tab"));
        var lv = NewList(L.T("c_weekend"), 130, L.T("c_used"), 100, L.T("c_big"), 100, L.T("c_topday"), 150, L.T("c_topgain"), 130);
        for (int i = h.Weeks.Count - 1; i >= 0; i--)
        {
            var x = h.Weeks[i];
            string day = "";
            DateTime dd;
            if (!string.IsNullOrEmpty(x.TopDay) && DateTime.TryParseExact(x.TopDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dd))
                day = L.DayName((int)dd.DayOfWeek) + " " + dd.ToString("dd.MM");
            lv.Items.Add(new ListViewItem(new[] { When(x.End), L.PctN(History.N(x.Used)), x.Big.ToString(), day, x.TopGain > 0 ? "\u200E+" + L.PctN(History.N(x.TopGain)) : "" }));
        }
        if (h.Weeks.Count == 0) lv.Items.Add(new ListViewItem(new[] { "", L.T("no_weeks"), "", "", "" }));
        p.Controls.Add(lv);
        return p;
    }

    ListView NewList(params object[] cols)
    {
        var lv = new ListView
        {
            View = System.Windows.Forms.View.Details, FullRowSelect = true, GridLines = true, Dock = DockStyle.Fill,
            RightToLeftLayout = L.Rtl, HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        for (int i = 0; i < cols.Length; i += 2) lv.Columns.Add((string)cols[i], S((int)cols[i + 1]), HorizontalAlignment.Center);
        return lv;
    }

    TabPage SessionsPage()
    {
        var p = new TabPage(L.T("sess_tab"));
        int n;
        double? avg = h.Average(out n);
        string sum = avg.HasValue
            ? L.T("avg_sum", L.PctN(History.N(avg.Value)), L.Sessions(n))
            : L.T("no_sessions");
        var c = h.Cur;
        // session that began while the app was not reading: only the part seen after the first reading counts
        bool late = !noFive && c != null && c.Valid && c.FiveFrom > 0;
        var lbl = new Label
        {
            Dock = DockStyle.Top, Height = S(late ? 100 : 70), Padding = new Padding(S(10)),
            Text = noFive ? L.T("no_five_plan") : sum + "\n" + L.T("only_counted", L.PctN(History.N(History.MinFive)))
                + (late ? "\n" + L.T("late_start", L.PctN(History.N(c.FiveFrom)), L.PctN(History.N(c.FiveDelta))) : "")
        };
        var lv = NewList(L.T("c_start"), 115, L.T("c_last"), 115, L.T("five"), 170, L.T("c_weekinc"), 110, L.T("c_full"), 180);
        if (c != null && c.Valid)
        {
            var it = Row(lv, c, c.Counts);
            it.SubItems[1].Text = L.T("running");
            it.ForeColor = WeekC;
        }
        for (int i = h.Sessions.Count - 1; i >= 0; i--) Row(lv, h.Sessions[i], true);
        AddDelete(lv);
        p.Controls.Add(lv);
        p.Controls.Add(lbl);
        lv.BringToFront();
        return p;
    }

    static string When(string iso) { var d = Settings.S2D(iso); return d.HasValue ? d.Value.ToString("dd.MM HH:mm") : ""; }

    ListViewItem Row(ListView lv, Session x, bool showRatio)
    {
        var it = new ListViewItem(new[] {
            When(x.Start), When(x.End),
            x.FiveFrom > 0 ? L.T("five_range", L.PctN(History.N(x.FiveDelta)), History.N(x.FiveFrom), History.N(x.FiveMax)) : L.PctN(History.N(x.FiveDelta)),
            "\u200E+" + L.PctN(History.N(x.WeekDelta)),
            showRatio ? L.PctN(History.N(x.Ratio)) : L.T("not_reached", L.PctN(History.N(History.MinFive))) });
        it.Tag = x;
        lv.Items.Add(it);
        return it;
    }

    // right-click or Delete key: remove a session from the list and the average (asks first)
    void AddDelete(ListView lv)
    {
        var menu = new ContextMenuStrip { RightToLeft = L.Dir };
        var del = menu.Items.Add(L.T("del_session"));
        del.Click += delegate { DeleteSelected(lv); };
        menu.Opening += (o, e) => { e.Cancel = lv.SelectedItems.Count == 0 || !(lv.SelectedItems[0].Tag is Session); };
        lv.ContextMenuStrip = menu;
        lv.MouseDown += (o, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = lv.GetItemAt(e.X, e.Y);
            if (hit != null) { lv.SelectedItems.Clear(); hit.Selected = true; }
        };
        lv.KeyDown += (o, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(lv); };
    }

    void DeleteSelected(ListView lv)
    {
        if (lv.SelectedItems.Count == 0) return;
        var x = lv.SelectedItems[0].Tag as Session;
        if (x == null) return;
        var opts = L.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        if (MessageBox.Show(this, L.T("del_ask", When(x.Start)), L.T("hist_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2, opts) != DialogResult.Yes) return;
        if (x == h.Cur) x.Valid = false; // running session: hidden now and not added to the list when it ends
        else h.Sessions.Remove(x);
        h.Save();
        var tabs = (TabControl)Controls[0];
        tabs.TabPages.RemoveAt(0);
        tabs.TabPages.Insert(0, SessionsPage());
        tabs.SelectedIndex = 0;
    }

    TabPage DayPage(DateTime day, string name)
    {
        var p = new TabPage(name + " " + day.ToString("dd.MM"));
        var list = new List<Sample>();
        foreach (var x in h.Samples) { var t = Settings.S2D(x.T); if (t.HasValue && t.Value.Date == day) list.Add(x); }

        var chart = new Panel { Dock = DockStyle.Top, Height = S(200), RightToLeft = RightToLeft.No, BackColor = Color.White };
        chart.Paint += (o, e) => DrawChart(e.Graphics, chart.ClientRectangle, list);
        chart.Resize += delegate { chart.Invalidate(); };

        var lv = NewList(L.T("time"), 90, L.T("c_weekly"), 100, L.T("c_fiveh"), 110, L.T("c_weekchg"), 120);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            double d = i > 0 ? list[i].W - list[i - 1].W : 0;
            lv.Items.Add(new ListViewItem(new[] {
                (Settings.S2D(list[i].T) ?? DateTime.MinValue).ToString("HH:mm"),
                L.PctN(History.N(list[i].W)), L.PctN(History.N(list[i].F)),
                Math.Abs(d) < 0.05 ? "" : (d > 0 ? "\u200E+" : "\u200E") + L.PctN(History.N(d)) }));
        }
        if (list.Count == 0) lv.Items.Add(new ListViewItem(new[] { "", L.T("no_readings"), "", "" }));
        p.Controls.Add(lv);
        p.Controls.Add(chart);
        lv.BringToFront();
        return p;
    }

    void DrawChart(Graphics g, Rectangle r, List<Sample> list)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int l = S(42), t = S(26), rr = S(16), b = S(28);
        var area = new Rectangle(r.X + l, r.Y + t, r.Width - l - rr, r.Height - t - b);
        using (var small = new Font("Segoe UI", 8f))
        using (var grid = new Pen(Color.FromArgb(228, 228, 228)))
        {
            for (int v = 0; v <= 100; v += 25)
            {
                int y = area.Bottom - area.Height * v / 100;
                g.DrawLine(grid, area.Left, y, area.Right, y);
                TextRenderer.DrawText(g, v + "%", small, new Rectangle(0, y - S(8), l - S(6), S(16)), Color.Gray, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            for (int hr = 0; hr <= 24; hr += 3)
            {
                int x = area.Left + area.Width * hr / 24;
                g.DrawLine(grid, x, area.Top, x, area.Bottom);
                TextRenderer.DrawText(g, hr.ToString("00") + ":00", small, new Rectangle(x - S(22), area.Bottom + S(4), S(44), S(16)), Color.Gray, TextFormatFlags.HorizontalCenter);
            }
            Line(g, area, list, true, WeekC);
            Line(g, area, list, false, FiveC);
            // legend, right side: [line] الأسبوعي   [line] الخمس ساعات
            int x0 = area.Right;
            x0 = Legend(g, small, x0, L.T("c_weekly"), WeekC);
            Legend(g, small, x0 - S(18), L.T("c_fiveh"), FiveC);
        }
    }

    // draws "swatch + label" ending at x (right edge), returns the left edge
    int Legend(Graphics g, Font f, int x, string text, Color c)
    {
        int y = S(6), sw = S(16);
        using (var b = new SolidBrush(c)) g.FillRectangle(b, x - sw, y + S(6), sw, S(3));
        int tw = TextRenderer.MeasureText(g, text, f).Width;
        TextRenderer.DrawText(g, text, f, new Rectangle(x - sw - S(4) - tw, y, tw, S(16)), Color.FromArgb(70, 70, 70),
            (L.Rtl ? TextFormatFlags.RightToLeft : 0) | TextFormatFlags.Right);
        return x - sw - S(4) - tw;
    }

    void Line(Graphics g, Rectangle a, List<Sample> list, bool weekly, Color c)
    {
        var pts = new List<PointF>();
        DateTime? last = null;
        using (var pen = new Pen(c, S(2)))
        {
            foreach (var x in list)
            {
                var t = Settings.S2D(x.T); if (!t.HasValue) continue;
                if (last.HasValue && (t.Value - last.Value).TotalMinutes > 40 && pts.Count > 0) { Draw(g, pen, pts); pts.Clear(); } // gap: PC was off
                last = t;
                double v = Math.Max(0, Math.Min(100, weekly ? x.W : x.F));
                pts.Add(new PointF(a.Left + (float)(a.Width * t.Value.TimeOfDay.TotalHours / 24), a.Bottom - (float)(a.Height * v / 100)));
            }
            Draw(g, pen, pts);
        }
    }

    static void Draw(Graphics g, Pen pen, List<PointF> pts)
    {
        if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
        else if (pts.Count == 1) using (var b = new SolidBrush(pen.Color)) g.FillEllipse(b, pts[0].X - 2, pts[0].Y - 2, 4, 4);
    }
}
