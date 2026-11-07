// Shared modern (Windows 11 style, dark) look: colors, window helpers, menu renderer,
// toggle switch, number stepper, buttons, and the drawn icons (tray + app). C# 5 syntax only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class Theme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

    public static readonly Color Bg = Color.FromArgb(32, 32, 32);
    public static readonly Color Card = Color.FromArgb(43, 43, 43);
    public static readonly Color CardLine = Color.FromArgb(58, 58, 58);
    public static readonly Color Hover = Color.FromArgb(58, 58, 58);
    public static readonly Color Field = Color.FromArgb(54, 54, 54);
    public static readonly Color FieldLine = Color.FromArgb(78, 78, 78);
    public static readonly Color Text = Color.FromArgb(240, 240, 240);
    public static readonly Color Dim = Color.FromArgb(160, 160, 160);
    public static readonly Color Faint = Color.FromArgb(105, 105, 105);
    public static readonly Color Accent = Color.FromArgb(217, 119, 87);      // Claude orange
    public static readonly Color AccentHover = Color.FromArgb(228, 138, 108);
    public static readonly Color MenuBg = Color.FromArgb(44, 44, 44);
    public static readonly Color MenuLine = Color.FromArgb(66, 66, 66);

    public static readonly Color Green = Color.FromArgb(46, 160, 67), Yellow = Color.FromArgb(226, 165, 28),
        Red = Color.FromArgb(222, 62, 62), Gray = Color.FromArgb(105, 105, 105), Blue = Color.FromArgb(52, 120, 220);

    static string iconFont;
    public static string IconFont
    {
        get
        {
            if (iconFont == null)
            {
                iconFont = "Segoe MDL2 Assets";
                using (var fams = new System.Drawing.Text.InstalledFontCollection())
                    foreach (var f in fams.Families) if (f.Name == "Segoe Fluent Icons") { iconFont = f.Name; break; }
            }
            return iconFont;
        }
    }

    public static float Scale(Control c) { using (var g = c.CreateGraphics()) return g.DpiX / 96f; }

    // dark title bar + Windows 11 rounded corners; harmless on older Windows
    public static void DarkWindow(Form f)
    {
        f.HandleCreated += delegate
        {
            try { int on = 1; DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { }
            try { int c = ColorRef(Bg); DwmSetWindowAttribute(f.Handle, 35, ref c, 4); } catch { }
        };
    }

    // returns true when Windows drew the rounded corners and the border itself (Windows 11)
    public static bool RoundAndBorder(IntPtr h, Color border, bool small)
    {
        try
        {
            int round = small ? 3 : 2;
            if (DwmSetWindowAttribute(h, 33, ref round, 4) != 0) return false;
            int c = ColorRef(border);
            DwmSetWindowAttribute(h, 34, ref c, 4);
            return true;
        }
        catch { return false; }
    }

    public static void DarkScrollbars(Control c) { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }

    static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float rad, Color c)
    {
        using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
    {
        using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
    }

    // small glyph from Segoe Fluent Icons / MDL2 as a menu image
    public static Bitmap Glyph(char ch, int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        using (var f = new Font(IconFont, size * 0.78f, GraphicsUnit.Pixel))
        using (var br = new SolidBrush(c))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(ch.ToString(), f, br, new RectangleF(0, 0, size, size), sf);
        }
        return b;
    }

    public static void DrawToggle(Graphics g, RectangleF r, bool on, bool enabled, bool hot)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = r.Height / 2f;
        float k = r.Height * (hot ? 0.62f : 0.54f);
        if (on)
        {
            Fill(g, r, rad, enabled ? (hot ? AccentHover : Accent) : Faint);
            float x = r.Right - r.Height / 2f - k / 2f;
            using (var b = new SolidBrush(enabled ? Color.FromArgb(28, 28, 28) : Color.FromArgb(70, 70, 70)))
                g.FillEllipse(b, x, r.Y + (r.Height - k) / 2f, k, k);
        }
        else
        {
            Fill(g, r, rad, Color.FromArgb(hot ? 50 : 40, 40, 40));
            Stroke(g, RectangleF.Inflate(r, -0.5f, -0.5f), rad, enabled ? Dim : Faint, 1f);
            float x = r.X + r.Height / 2f - k / 2f;
            using (var b = new SolidBrush(enabled ? Dim : Faint)) g.FillEllipse(b, x, r.Y + (r.Height - k) / 2f, k, k);
        }
    }
}

// ---------- context menu ----------
public class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public bool DwmBorder;
    public ModernMenuRenderer() : base(new ModernColors()) { RoundedEdges = false; }

    class ModernColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.MenuBg; } }
        public override Color MenuBorder { get { return Theme.MenuLine; } }
        public override Color SeparatorDark { get { return Theme.MenuLine; } }
        public override Color SeparatorLight { get { return Theme.MenuLine; } }
        public override Color CheckBackground { get { return Theme.MenuBg; } }
        public override Color CheckSelectedBackground { get { return Theme.Hover; } }
        public override Color CheckPressedBackground { get { return Theme.Hover; } }
        public override Color ButtonCheckedHighlight { get { return Theme.MenuBg; } }
        public override Color ButtonSelectedBorder { get { return Theme.Hover; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.MenuBg); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (DwmBorder) return;
        using (var p = new Pen(Theme.MenuLine)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        Theme.Fill(g, r, 4, Theme.Hover);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (var p = new Pen(Theme.MenuLine)) e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    // plain image, no highlight box behind checked items
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image != null) e.Graphics.DrawImage(e.Image, e.ImageRectangle);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var mi = e.Item as ToolStripMenuItem;
        // toggle items reserve room through a blank shortcut text; draw a switch there
        if (mi != null && mi.CheckOnClick && e.Text != null && e.Text.Trim().Length == 0 && e.Text.Length > 0)
        {
            var g = e.Graphics;
            float s = e.Item.Height / 36f;
            float h = 18 * s, w = 36 * s;
            var tr = e.TextRectangle;
            float x = e.Item.RightToLeft == RightToLeft.Yes ? tr.Left + 2 : tr.Right - w - 2;
            Theme.DrawToggle(g, new RectangleF(x, (e.Item.Height - h) / 2f, w, h), mi.Checked, true, false);
            return;
        }
        e.TextColor = e.Item.Enabled ? (e.Item.Tag as string == "head" ? Theme.Dim : Theme.Text) : Theme.Faint;
        base.OnRenderItemText(e);
    }
}

// ---------- toggle switch ----------
public class ToggleSwitch : Control
{
    bool on, hot;
    public event EventHandler CheckedChanged;
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        RightToLeft = RightToLeft.No;
        TabStop = true;
    }
    public bool Checked
    {
        get { return on; }
        set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(2, 2, Width - 4.5f, Height - 4.5f);
        Theme.DrawToggle(g, r, on, Enabled, hot && Enabled);
        if (Focused && ShowFocusCues) Theme.Stroke(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f, Theme.Text, 1f);
    }
}

// ---------- number stepper:  [ − ]  12  [ + ] ----------
public class Stepper : Control
{
    TextBox box = new TextBox();
    int val, min, max, hotZone, pressZone, repeatCount;
    Timer repeat = new Timer();
    public event EventHandler ValueChanged;

    public Stepper(int value, int minimum, int maximum)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        RightToLeft = RightToLeft.No;
        min = minimum; max = maximum; val = Math.Max(min, Math.Min(max, value));
        box.BorderStyle = BorderStyle.None;
        box.TextAlign = HorizontalAlignment.Center;
        box.BackColor = Theme.Field; box.ForeColor = Theme.Text;
        box.RightToLeft = RightToLeft.No;
        box.MaxLength = 3;
        box.Text = val.ToString();
        box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Up) { Value = val + 1; e.Handled = true; box.SelectAll(); }
            else if (e.KeyCode == Keys.Down) { Value = val - 1; e.Handled = true; box.SelectAll(); }
        };
        box.Leave += delegate { Commit(); };
        box.TextChanged += delegate { int v; if (int.TryParse(box.Text, out v) && v >= min && v <= max && v != val) { val = v; Fire(); } };
        box.MouseWheel += (s, e) => { Value = val + Math.Sign(e.Delta); };
        Controls.Add(box);
        repeat.Tick += delegate
        {
            repeatCount++;
            repeat.Interval = repeatCount > 8 ? 40 : 90;
            Value = val + (pressZone == 1 ? -1 : 1);
        };
    }

    public int Value
    {
        get { Commit(); return val; }
        set
        {
            int v = Math.Max(min, Math.Min(max, value));
            if (box.Text != v.ToString()) box.Text = v.ToString();
            if (v != val) { val = v; Fire(); }
            Invalidate();
        }
    }

    void Fire() { if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
    void Commit() { int v; if (!int.TryParse(box.Text, out v)) v = val; v = Math.Max(min, Math.Min(max, v)); val = v; if (box.Text != v.ToString()) box.Text = v.ToString(); }

    int Btn { get { return Height; } }
    int Zone(Point p) { return p.X < Btn ? 1 : p.X > Width - Btn ? 2 : 0; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        box.Width = Math.Max(10, Width - 2 * Btn - 4);
        box.Left = Btn + 2;
        box.Top = (Height - box.Height) / 2;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); box.Font = Font; PerformLayout(); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        box.Enabled = Enabled;
        box.BackColor = Enabled ? Theme.Field : Color.FromArgb(40, 40, 40);
        Invalidate(); base.OnEnabledChanged(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { int z = Zone(e.Location); if (z != hotZone) { hotZone = z; Invalidate(); } Cursor = z == 0 ? Cursors.Default : Cursors.Hand; base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hotZone = 0; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        pressZone = Zone(e.Location);
        if (pressZone == 0) { box.Focus(); return; }
        Value = val + (pressZone == 1 ? -1 : 1);
        repeatCount = 0; repeat.Interval = 400; repeat.Start();
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { repeat.Stop(); pressZone = 0; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value = val + Math.Sign(e.Delta); base.OnMouseWheel(e); }
    protected override void Dispose(bool disposing) { if (disposing) repeat.Dispose(); base.Dispose(disposing); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = Height * 0.18f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.Fill(g, r, rad, Enabled ? Theme.Field : Color.FromArgb(40, 40, 40));
        Color txt = Enabled ? Theme.Text : Theme.Faint;
        for (int z = 1; z <= 2; z++)
        {
            var br = z == 1 ? new RectangleF(3, 3, Btn - 6, Height - 6.5f) : new RectangleF(Width - Btn + 3, 3, Btn - 6, Height - 6.5f);
            bool atEnd = (z == 1 && val <= min) || (z == 2 && val >= max);
            if (Enabled && hotZone == z && !atEnd) Theme.Fill(g, br, rad * 0.8f, pressZone == z ? Color.FromArgb(82, 82, 82) : Color.FromArgb(70, 70, 70));
            float cx = br.X + br.Width / 2f, cy = br.Y + br.Height / 2f, k = Height * 0.17f;
            using (var p = new Pen(atEnd || !Enabled ? Theme.Faint : txt, Math.Max(1.2f, Height / 24f)))
            {
                g.DrawLine(p, cx - k, cy, cx + k, cy);
                if (z == 2) g.DrawLine(p, cx, cy - k, cx, cy + k);
            }
        }
        Theme.Stroke(g, r, rad, box.Focused ? Theme.Accent : Theme.FieldLine, 1f);
    }
}

// ---------- dark drop-down picker (same look as Stepper; the list is a themed menu) ----------
public class Picker : Control
{
    public readonly List<string> Items = new List<string>();
    int sel;
    bool hot;
    public int SelectedIndex { get { return sel; } set { sel = Math.Max(0, Math.Min(Items.Count - 1, value)); Invalidate(); } }

    public Picker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent; ForeColor = Theme.Text; Cursor = Cursors.Hand;
        RightToLeft = RightToLeft.No;
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Open(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down) SelectedIndex = sel + 1;
        else if (e.KeyCode == Keys.Up) SelectedIndex = sel - 1;
        else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.F4) Open();
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

    void Open()
    {
        var rnd = new ModernMenuRenderer();
        var m = new ContextMenuStrip { Renderer = rnd, Font = Font, BackColor = Theme.MenuBg, ForeColor = Theme.Text, ShowImageMargin = false, ShowCheckMargin = false, RightToLeft = RightToLeft.No };
        m.HandleCreated += delegate { rnd.DwmBorder = Theme.RoundAndBorder(m.Handle, Theme.MenuLine, false); };
        int pad = Height / 6;
        for (int i = 0; i < Items.Count; i++)
        {
            int k = i;
            var it = new ToolStripMenuItem(Items[i]) { Padding = new Padding(0, pad, 0, pad) };
            if (i == sel) it.Font = new Font(Font, FontStyle.Bold);
            it.Click += delegate { SelectedIndex = k; };
            m.Items.Add(it);
        }
        m.MinimumSize = new Size(Width, 0);
        m.Closed += delegate { m.BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
        m.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = Height * 0.18f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.Fill(g, r, rad, hot ? Color.FromArgb(58, 58, 58) : Theme.Field);
        string t = sel < Items.Count ? Items[sel] : "";
        int arrow = Height;
        TextRenderer.DrawText(g, t, Font, new Rectangle(Height / 3, 0, Width - arrow - Height / 3, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        float cx = Width - arrow / 2f, cy = Height / 2f + 1, k = Height * 0.13f;
        using (var p = new Pen(Theme.Dim, Math.Max(1.2f, Height / 24f)))
            g.DrawLines(p, new[] { new PointF(cx - k, cy - k / 2), new PointF(cx, cy + k / 2), new PointF(cx + k, cy - k / 2) });
        Theme.Stroke(g, r, rad, Focused ? Theme.Accent : Theme.FieldLine, 1f);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

// ---------- flat rounded button (keeps Button's DialogResult / Accept / Cancel behaviour) ----------
public class ModernButton : Button
{
    public bool Primary;
    bool hot, down;
    public ModernButton(string text, bool primary)
    {
        Text = text; Primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = Height * 0.16f;
        if (Primary)
        {
            Theme.Fill(g, r, rad, down ? Theme.Accent : hot ? Theme.AccentHover : Theme.Accent);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.FromArgb(25, 25, 25), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            Theme.Fill(g, r, rad, down ? Color.FromArgb(50, 50, 50) : hot ? Color.FromArgb(62, 62, 62) : Theme.Field);
            Theme.Stroke(g, r, rad, Theme.FieldLine, 1f);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        if (Focused && ShowFocusCues) Theme.Stroke(g, RectangleF.Inflate(r, -2, -2), rad, Primary ? Color.FromArgb(25, 25, 25) : Theme.Text, 1f);
    }
}

// ---------- rounded card that holds setting rows ----------
public class CardPanel : Panel
{
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.Fill(g, r, 7, Theme.Card);
        Theme.Stroke(g, r, 7, Theme.CardLine, 1f);
        // thin lines between rows
        using (var p = new Pen(Color.FromArgb(36, 36, 36)))
            for (int i = 1; i < Controls.Count; i++)
            {
                var c = Controls[i];
                g.DrawLine(p, 1, c.Top, Width - 2, c.Top);
            }
    }
}

// ---------- drawn icons ----------
public static class IconArt
{
    // tray icon. number = difference in the middle; bars = small usage strip with a time mark under the number
    public static Bitmap Tray(int n, bool hasData, double usage, double elapsed, Color c, string text, bool number, bool bars)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            if (!hasData) { Badge(g, n, Theme.Gray); Label(g, "?", new RectangleF(0, 0, n, n), n * 0.56f, Color.White); return bmp; }
            if (!number) { Ring(g, n, usage, elapsed, c); return bmp; }

            Badge(g, n, c);
            Color ink = Lum(c) > 0.58 ? Color.FromArgb(30, 30, 30) : Color.White;
            if (bars)
            {
                Label(g, text, new RectangleF(n * 0.1f, n * 0.08f, n * 0.8f, n * 0.56f), n * 0.42f, ink);
                // thin usage line (fills right → left) with a small gap where the time stands
                float pad = n * 0.2f, w = n - 2 * pad, bh = Math.Max(2f, (float)Math.Round(n * 0.1f)), y = (float)Math.Round(n * 0.74f);
                using (var tb = new SolidBrush(Color.FromArgb(ink.R > 128 ? 95 : 80, ink))) g.FillRectangle(tb, pad, y, w, bh);
                float fw = (float)(w * Clamp(usage) / 100.0);
                using (var fb = new SolidBrush(ink)) g.FillRectangle(fb, pad + w - fw, y, fw, bh);
                float tw = Math.Max(1f, (float)Math.Round(n / 16f));
                float tx = pad + w - (float)(w * Clamp(elapsed) / 100.0) - tw / 2f;
                g.SmoothingMode = SmoothingMode.None;
                using (var mb = new SolidBrush(c)) g.FillRectangle(mb, Math.Max(pad, Math.Min(pad + w - tw, tx)), y, tw, bh);
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
            else Label(g, text, new RectangleF(n * 0.06f, n * 0.06f, n * 0.88f, n * 0.88f), n * 0.56f, ink);
        }
        return bmp;
    }

    public static string DiffText(double d)
    {
        return (d >= 0.5 ? "+" : d <= -0.5 ? "−" : "") + Math.Round(Math.Abs(d)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    static double Clamp(double v) { return Math.Max(0, Math.Min(100, v)); }
    static double Lum(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }
    static Color Mix(Color a, Color b, float t) { return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

    static void Badge(Graphics g, int n, Color c)
    {
        var r = new RectangleF(0.5f, 0.5f, n - 1, n - 1);
        using (var p = Theme.Round(r, n * 0.27f))
        using (var b = new LinearGradientBrush(new RectangleF(0, -1, n, n + 2), Mix(c, Color.White, 0.16f), Mix(c, Color.Black, 0.10f), 90f))
            g.FillPath(b, p);
    }

    // usage ring (status color) with a white dot where the week's time stands
    static void Ring(Graphics g, int n, double usage, double elapsed, Color c)
    {
        float t = Math.Max(2.2f, n * 0.17f);
        var r = new RectangleF(t / 2f + 0.5f, t / 2f + 0.5f, n - t - 1, n - t - 1);
        using (var track = new Pen(Color.FromArgb(90, c), t)) g.DrawEllipse(track, r);
        float sweep = (float)(Clamp(usage) * 3.6);
        if (sweep > 0.5f) using (var arc = new Pen(c, t) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, r, -90, sweep);
        double a = (-90 + Clamp(elapsed) * 3.6) * Math.PI / 180;
        float cx = n / 2f + (float)(Math.Cos(a) * r.Width / 2), cy = n / 2f + (float)(Math.Sin(a) * r.Height / 2), d = t * 1.05f;
        using (var o = new SolidBrush(Color.FromArgb(30, 30, 30))) g.FillEllipse(o, cx - d / 2f - 0.8f, cy - d / 2f - 0.8f, d + 1.6f, d + 1.6f);
        g.FillEllipse(Brushes.White, cx - d / 2f, cy - d / 2f, d, d);
        float k = n * 0.22f;
        using (var b = new SolidBrush(c)) g.FillEllipse(b, n / 2f - k / 2f, n / 2f - k / 2f, k, k);
    }

    // text drawn as a path and scaled to fill the box (sharp and as large as possible)
    static void Label(Graphics g, string text, RectangleF box, float maxH, Color c)
    {
        if (string.IsNullOrEmpty(text)) return;
        using (var p = new GraphicsPath())
        using (var ff = new FontFamily("Segoe UI"))
        {
            p.AddString(text, ff, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
            var b = p.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) return;
            float sc = Math.Min(box.Width / b.Width, Math.Min(box.Height, maxH) / b.Height);
            using (var m = new Matrix())
            {
                m.Translate(box.X + box.Width / 2f, box.Y + box.Height / 2f);
                m.Scale(sc, sc);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                p.Transform(m);
            }
            using (var br = new SolidBrush(c)) g.FillPath(br, p);
        }
    }

    // program icon: dark tile with an orange gauge
    public static Bitmap App(int n)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            var r = new RectangleF(0.5f, 0.5f, n - 1, n - 1);
            using (var p = Theme.Round(r, n * 0.24f))
            using (var b = new LinearGradientBrush(new RectangleF(0, -1, n, n + 2), Color.FromArgb(58, 58, 58), Color.FromArgb(26, 26, 26), 90f))
                g.FillPath(b, p);
            float t = Math.Max(2f, n * 0.13f), m = n * 0.2f;
            var a = new RectangleF(m, m + n * 0.03f, n - 2 * m, n - 2 * m);
            using (var track = new Pen(Color.FromArgb(80, 80, 80), t) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(track, a, 135, 270);
            using (var arc = new Pen(Theme.Accent, t) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, a, 135, 175);
            // needle
            double ang = (135 + 175) * Math.PI / 180;
            float cx = a.X + a.Width / 2f, cy = a.Y + a.Height / 2f, len = a.Width * 0.36f;
            using (var pen = new Pen(Color.White, Math.Max(1.5f, n * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(ang) * len, cy + (float)Math.Sin(ang) * len);
            float d = Math.Max(3f, n * 0.15f);
            g.FillEllipse(Brushes.White, cx - d / 2f, cy - d / 2f, d, d);
        }
        return bmp;
    }

    static byte[] icoBytes;
    // multi-size .ico (PNG frames) for the exe and the windows
    public static byte[] AppIco()
    {
        if (icoBytes != null) return icoBytes;
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes) using (var b = App(s)) pngs.Add(s >= 256 ? Png(b) : Dib(b));
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(off);
                off += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
            w.Flush();
            icoBytes = ms.ToArray();
        }
        return icoBytes;
    }

    static byte[] Png(Bitmap b) { using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }

    // 32-bit icon frame: header + bottom-up BGRA pixels + empty AND mask
    static byte[] Dib(Bitmap b)
    {
        int n = b.Width, mask = ((n + 31) / 32) * 4 * n;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(n * n * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--)
                for (int x = 0; x < n; x++) { var c = b.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
            w.Write(new byte[mask]);
            w.Flush();
            return ms.ToArray();
        }
    }

    static Icon appIcon;
    public static Icon AppIcon { get { if (appIcon == null) using (var ms = new MemoryStream(AppIco())) appIcon = new Icon(ms); return appIcon; } }
}
