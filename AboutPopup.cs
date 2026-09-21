using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace RGBCommander;

public sealed record AboutDevice(string Name, string Kind, string Sub, Color Color, bool On);
public sealed record AboutPill(string Text, Color Color);

/// <summary>Live snapshot the title hands the popup at open time, so the popup never
/// reaches back into MainForm internals.</summary>
public sealed class AboutInfo
{
    public string Version = "";
    public string BuildLine = "";
    public string Effect = "";
    public Color CurrentColor;
    public bool LightsOff;
    public string ColorHex = "";
    public string DevicesSummary = "";
    public List<AboutDevice> Devices = new();
    public int TotalLeds;
    public int FrameRate;
    public List<AboutPill> Backends = new();
    public string RenderMode = "";
    public string StatusText = "";
    public AboutPill? Special;
    public string Tuning = "";
    public string ProfileLine = "";
    public string Uptime = "";
    public string Credit = "Unified control for everything that glows on your desk.";
}

/// <summary>The "About Prisma" popup dropped under the rainbow title: a translucent card
/// in the app aesthetic showing live state (effect, devices, backends, render mode, uptime)
/// plus folder/log shortcuts. Reads the supplied snapshot only; never touches the lighting.
/// Closes on click-away (Deactivate) or Esc.</summary>
public sealed class AboutPopup : Form
{
    private const int PAD = 16, W = 372;
    private readonly AboutInfo _i;
    /// <summary>Click-away dismissal; the test harness turns this off to keep it on screen.</summary>
    public bool CloseOnDeactivate { get; set; } = true;

    public AboutPopup(AboutInfo info)
    {
        _i = info;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Theme.Bg;
        Font = Theme.UiFont(9.5f);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        int contentH = PaintBody(null);
        Size = new Size(W, contentH + 12 + 38 + PAD); // content + button row
        BuildButtons();
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (CloseOnDeactivate) Close(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private void BuildButtons()
    {
        int by = Height - PAD - 38;
        int bw = (W - PAD * 2 - 10) / 2;
        var folder = new RoundedButton { Text = "Settings folder", Left = PAD, Top = by, Width = bw, Height = 38 };
        folder.Click += (_, _) =>
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBCommander");
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
        };
        var log = new RoundedButton { Text = "Debug log", Left = PAD + bw + 10, Top = by, Width = bw, Height = 38 };
        log.Click += (_, _) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "rgbc-debug.log");
            try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        };
        Controls.Add(folder);
        Controls.Add(log);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintBody(e.Graphics);

    /// <summary>Single walk that both measures (g == null) and paints the card body.
    /// Returns the y just past the last painted block.</summary>
    private int PaintBody(Graphics? g)
    {
        if (g != null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            using var fill = new SolidBrush(Color.FromArgb(238, Theme.PanelBg));
            using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }

        int y = 14;

        // ---- header: rainbow PRISMA + version, then a rainbow hairline ----
        if (g != null)
        {
            using var f = Theme.DisplayFont(14f, FontStyle.Bold);
            using var wordPath = new GraphicsPath();
            float em = 14f * g.DpiY / 72f;
            wordPath.AddString("PRISMA", f.FontFamily, (int)FontStyle.Bold, em, new Point(PAD, y), StringFormat.GenericTypographic);
            var b = wordPath.GetBounds();
            using (var brush = new LinearGradientBrush(new RectangleF(b.X, b.Y, Math.Max(1, b.Width), b.Height),
                       Color.White, Color.White, LinearGradientMode.Horizontal))
            {
                var cb = new ColorBlend(7);
                for (int s = 0; s < 7; s++) { cb.Colors[s] = Theme.HsvToColor(s * 300.0 / 6, 0.9, 1.0); cb.Positions[s] = s / 6f; }
                brush.InterpolationColors = cb;
                g.FillPath(brush, wordPath);
            }
            int vx = PAD + (int)Math.Ceiling(b.Width) + 8;
            TextRenderer.DrawText(g, "v" + _i.Version, Theme.UiFont(8.5f), new Point(vx, y + 8), Theme.Subtle, TextFormatFlags.NoPrefix);
        }
        y += 30;
        if (g != null)
            for (int x = PAD; x < W - PAD; x++)
            {
                using var pen = new Pen(Theme.HsvToColor((x - PAD) * 300f / (W - 2 * PAD), 0.9, 1.0));
                g.DrawLine(pen, x, y, x, y + 2);
            }
        y += 12;

        // ---- NOW PLAYING ----
        y = Section(g, y, "NOW PLAYING");
        if (g != null)
        {
            if (_i.LightsOff)
                TextRenderer.DrawText(g, "Lights off", Theme.UiFont(10f, FontStyle.Bold), new Point(PAD, y), Theme.Subtle, TextFormatFlags.NoPrefix);
            else
            {
                Dot(g, PAD + 7, y + 9, _i.CurrentColor);
                TextRenderer.DrawText(g, _i.Effect, Theme.UiFont(10f, FontStyle.Bold), new Point(PAD + 20, y), Theme.TextCol, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, _i.ColorHex, Theme.UiFont(9f), new Rectangle(PAD, y, W - 2 * PAD, 20), Theme.Subtle, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            }
        }
        y += 26;

        // ---- hero: total LEDs ----
        if (g != null)
        {
            TextRenderer.DrawText(g, _i.TotalLeds.ToString(), Theme.DisplayFont(20f, FontStyle.Bold), new Point(PAD - 2, y), Theme.TextCol, TextFormatFlags.NoPrefix);
            int nx = PAD + TextRenderer.MeasureText(_i.TotalLeds.ToString(), Theme.DisplayFont(20f, FontStyle.Bold)).Width;
            TextRenderer.DrawText(g, $"LEDs lit  ·  {_i.FrameRate} fps", Theme.UiFont(9f), new Point(nx, y + 12), Theme.Accent, TextFormatFlags.NoPrefix);
        }
        y += 36;

        // ---- DEVICES roster ----
        y = Section(g, y, "DEVICES  ·  " + _i.DevicesSummary);
        foreach (var d in _i.Devices)
        {
            if (g != null)
            {
                var name = d.On ? Theme.TextCol : Theme.Subtle;
                Dot(g, PAD + 7, y + 9, d.On ? d.Color : Theme.Blend(d.Color, Theme.PanelBg, 0.6));
                TextRenderer.DrawText(g, d.Name, Theme.UiFont(9f, FontStyle.Bold), new Point(PAD + 20, y), name, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                Pill(g, W - PAD - 48, y + 2, 48, d.Kind, Theme.Subtle, false);
                TextRenderer.DrawText(g, d.Sub, Theme.UiFont(7.5f), new Point(PAD + 20, y + 13), Theme.Subtle, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            y += 30;
        }

        // ---- BACKENDS ----
        y = Section(g, y, "BACKENDS");
        if (g != null)
        {
            int px = PAD;
            foreach (var pill in _i.Backends)
            {
                int pw = TextRenderer.MeasureText(pill.Text, Theme.UiFont(8f, FontStyle.Bold)).Width + 18;
                Pill(g, px, y, pw, pill.Text, pill.Color, true);
                px += pw + 6;
            }
        }
        y += 28;

        // ---- RENDERING + HEALTH ----
        y = Kv(g, y, "Rendering", _i.RenderMode);
        y = Kv(g, y, "Tuning", _i.Tuning);
        y = Kv(g, y, "Profile", _i.ProfileLine);
        y = Kv(g, y, "Uptime", _i.Uptime);

        // ---- status line + special pill ----
        y += 4;
        if (g != null && !string.IsNullOrEmpty(_i.StatusText))
            TextRenderer.DrawText(g, _i.StatusText, Theme.UiFont(8.5f), new Rectangle(PAD, y, W - 2 * PAD, 16), Theme.Subtle, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        y += 18;
        if (_i.Special != null)
        {
            if (g != null) Pill(g, PAD, y, TextRenderer.MeasureText(_i.Special.Text, Theme.UiFont(8f, FontStyle.Bold)).Width + 18, _i.Special.Text, _i.Special.Color, true);
            y += 24;
        }

        // ---- build line + credit ----
        if (g != null)
        {
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, PAD, y, W - PAD, y);
        }
        y += 8;
        if (g != null)
        {
            TextRenderer.DrawText(g, _i.BuildLine, Theme.UiFont(8f), new Rectangle(PAD, y, W - 2 * PAD, 16), Theme.Subtle, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _i.Credit, Theme.UiFont(8f), new Rectangle(PAD, y + 16, W - 2 * PAD, 16), Theme.Accent, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        y += 36;
        return y;
    }

    private static int Section(Graphics? g, int y, string text)
    {
        if (g != null)
            TextRenderer.DrawText(g, text, Theme.UiFont(8f, FontStyle.Bold), new Point(PAD, y), Theme.Accent, TextFormatFlags.NoPrefix);
        return y + 20;
    }

    private static int Kv(Graphics? g, int y, string key, string value)
    {
        if (g != null)
        {
            TextRenderer.DrawText(g, key, Theme.UiFont(9f), new Point(PAD, y), Theme.Subtle, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, value, Theme.UiFont(9f), new Rectangle(PAD + 80, y, W - 2 * PAD - 80, 18), Theme.TextCol, TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        return y + 20;
    }

    private static void Dot(Graphics g, int cx, int cy, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - 5, cy - 5, 10, 10);
        using var pen = new Pen(Color.FromArgb(90, Color.White));
        g.DrawEllipse(pen, cx - 5, cy - 5, 9, 9);
    }

    private static void Pill(Graphics g, int x, int y, int w, string text, Color c, bool tint)
    {
        var r = new Rectangle(x, y, w, 18);
        using var path = Theme.RoundedRect(r, 8);
        if (tint) { using var fill = new SolidBrush(Color.FromArgb(36, c)); g.FillPath(fill, path); }
        using var pen = new Pen(Color.FromArgb(tint ? 200 : 120, c), 1f);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, text, Theme.UiFont(8f, FontStyle.Bold), r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
