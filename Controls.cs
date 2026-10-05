using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RGBCommander;

public static partial class Theme
{
    public static Color Bg { get; private set; } = Color.FromArgb(16, 17, 20);
    public static Color PanelBg { get; private set; } = Color.FromArgb(24, 26, 31);
    public static Color RowBg { get; private set; } = Color.FromArgb(33, 36, 43);
    public static Color RowHover { get; private set; } = Color.FromArgb(43, 47, 56);
    /// <summary>Accent color for the whole UI. Mutable so the user can re-theme at
    /// runtime; <see cref="AccentDim"/> is derived from it.</summary>
    public static Color Accent { get; private set; } = Color.FromArgb(0, 200, 170);
    public static Color AccentDim { get; private set; } = Color.FromArgb(0, 110, 95);

    /// <summary>Re-themes the UI. Callers should repaint open windows afterwards.</summary>
    public static void SetAccent(Color c)
    {
        _accentSpec = c;
        Accent = c;
        AccentDim = Color.FromArgb((int)(c.R * 0.55), (int)(c.G * 0.55), (int)(c.B * 0.55));
        if (Classic)
        {
            // A Win95 program had no accent colour to choose: selection navy is the only one.
            Accent = ClassicNavy;
            AccentDim = Color.FromArgb(0, 0, 80);
        }
        Revision++;
    }
    public static Color TextCol { get; private set; } = Color.FromArgb(232, 234, 238);
    public static Color Subtle { get; private set; } = Color.FromArgb(138, 144, 155);
    public static Color Border { get; private set; } = Color.FromArgb(52, 56, 66);
    public static Color ErrorCol { get; private set; } = Color.FromArgb(255, 120, 110);

    // ---- typography: Segoe UI Variable (the Windows 11 system font) when installed ----
    // Bold requests map to the true Semibold optical instance (the Win11 emphasis
    // weight) instead of GDI's smeared synthetic bold. Note the GDI family names are
    // truncated to 31 chars ("...Display Semib").
    private static readonly string TextFamily = FirstInstalled("Segoe UI Variable Text") ?? "Segoe UI";
    private static readonly string? TextSemibold = FirstInstalled("Segoe UI Variable Text Semibold");
    private static readonly string DisplayFamily = FirstInstalled("Segoe UI Variable Display") ?? "Segoe UI";
    private static readonly string? DisplaySemibold = FirstInstalled("Segoe UI Variable Display Semib");
    /// <summary>Icon font for UI glyphs (gear, list, ...); null if neither is installed.</summary>
    public static readonly string? IconFontName = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets");

    private static string? FirstInstalled(params string[] names)
    {
        try
        {
            using var installed = new System.Drawing.Text.InstalledFontCollection();
            var have = installed.Families.Select(f => f.Name).ToHashSet();
            return names.FirstOrDefault(have.Contains);
        }
        catch { return null; }
    }

    /// <summary>Body/UI font (Segoe UI Variable Text, falling back to Segoe UI).</summary>
    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => Track(
        Classic ? new Font(ClassicFamily, ClassicSize(size), style)
        : style.HasFlag(FontStyle.Bold) && TextSemibold != null
            ? new Font(TextSemibold, size, style & ~FontStyle.Bold)
            : new Font(TextFamily, size, style),
        size, style, display: false);

    /// <summary>Cached shared UI font for OnPaint paths — NEVER dispose the result. A fresh
    /// Font per paint holds a native GDI+ handle until finalization, and a hue-bar drag
    /// repaints every device row per mouse-move (~1,000 leaked fonts/sec), stalling the GC
    /// on the same thread that paces the 25fps device writes. Safe to share: Font is
    /// immutable, families are resolved once at startup, and TextRenderer takes no ownership.</summary>
    public static Font UiFontShared(float size, FontStyle style = FontStyle.Regular)
    {
        if (!s_fontCache.TryGetValue((size, style, Classic), out var f))
            s_fontCache[(size, style, Classic)] = f = UiFont(size, style);
        return f;
    }
    private static readonly Dictionary<(float, FontStyle, bool), Font> s_fontCache = new();

    /// <summary>Large-size font for titles (Segoe UI Variable Display).</summary>
    public static Font DisplayFont(float size, FontStyle style = FontStyle.Regular) => Track(
        Classic ? new Font(ClassicFamily, ClassicSize(size), style)
        : style.HasFlag(FontStyle.Bold) && DisplaySemibold != null
            ? new Font(DisplaySemibold, size, style & ~FontStyle.Bold)
            : new Font(DisplayFamily, size, style),
        size, style, display: true);

    /// <summary>Icon-font glyph font; size is the glyph's em size in points.</summary>
    public static Font IconFont(float size) => new(IconFontName ?? TextFamily, size);

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (Classic) radius = 0;   // 1995 had no rounded corners: squares every call site at once
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            // Classic squares every corner; AddArc with a zero-sized box throws in GDI+.
            path.AddRectangle(r);
            return path;
        }
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Linear blend of two colors; f=0 returns a, f=1 returns b.</summary>
    public static Color Blend(Color a, Color b, double f) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));

    public static Color HsvToColor(double h)
    {
        h = (h % 360 + 360) % 360;
        double x = 1 - Math.Abs(h / 60 % 2 - 1);
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (1.0, x, 0.0),
            < 2 => (x, 1.0, 0.0),
            < 3 => (0.0, 1.0, x),
            < 4 => (0.0, x, 1.0),
            < 5 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x)
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    public static Color HsvToColor(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = ((int)(h / 60)) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x)
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    /// <summary>Applies the shared dark theme to an existing menu (kept for the tray and
    /// other menus built the old way). New menus should use <see cref="Menus.Build"/>.</summary>
    public static void StyleMenu(ContextMenuStrip menu)
    {
        menu.Renderer = new ThemedMenuRenderer();
        menu.BackColor = PanelBg;
        menu.ForeColor = TextCol;
        menu.Font = UiFont(9.5f);
    }

    /// <summary>Retained no-op shim: the renderer now themes items, so menus rebuilt at
    /// runtime no longer need a per-item pass.</summary>
    public static void StyleMenuItem(ToolStripItem item) { }
}

/// <summary>Dark, rounded context-menu look shared by every right-click menu: accent
/// rounded hover, themed separators, accent checks, accent section headers.</summary>
public class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        => e.Graphics.Clear(Theme.PanelBg);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var b = new SolidBrush(Theme.RowHover);
        using var path = Theme.RoundedRect(rect, 6);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is MenuHeader)
        {
            e.TextColor = Theme.Accent;
            e.TextFont = Theme.UiFont(8f, FontStyle.Bold);
        }
        else e.TextColor = e.Item.Enabled ? Theme.TextCol : Theme.Subtle;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 26, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Selected: true } ? Theme.TextCol : Theme.Subtle;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using var pen = new Pen(Theme.Accent, 2f);
        g.DrawLines(pen, new[]
        {
            new Point(r.Left + 3, r.Top + r.Height / 2),
            new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4),
            new Point(r.Right - 2, r.Top + 3)
        });
    }

    private class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.PanelBg;
        public override Color ImageMarginGradientBegin => Theme.PanelBg;
        public override Color ImageMarginGradientMiddle => Theme.PanelBg;
        public override Color ImageMarginGradientEnd => Theme.PanelBg;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.RowHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}

/// <summary>A non-clickable accent section title inside a themed menu.</summary>
public class MenuHeader : ToolStripMenuItem
{
    public MenuHeader(string text) : base(text) { Enabled = false; }
}

/// <summary>Factory + item builders for the themed right-click menus. Items can carry a
/// Segoe Fluent glyph (rendered into the icon margin) or a colour dot.</summary>
public static class Menus
{
    public static ContextMenuStrip Build()
    {
        return new ContextMenuStrip
        {
            Renderer = new ThemedMenuRenderer(),
            BackColor = Theme.PanelBg,
            ForeColor = Theme.TextCol,
            Font = Theme.UiFont(9.5f),
            ShowImageMargin = true,
            ImageScalingSize = new Size(16, 16),
        };
    }

    public static ToolStripMenuItem Header(string text) => new MenuHeader(text);

    public static ToolStripMenuItem Item(string text, string? glyph, EventHandler onClick, bool isChecked = false)
    {
        var it = new ToolStripMenuItem(text) { Checked = isChecked };
        if (glyph != null && !isChecked) it.Image = Glyph(glyph);
        it.Click += onClick;
        return it;
    }

    public static ToolStripMenuItem Submenu(string text, string? glyph)
    {
        var it = new ToolStripMenuItem(text);
        if (glyph != null) it.Image = Glyph(glyph);
        return it;
    }

    /// <summary>Fluent icon glyph centred in a 16×16 image (subtle colour), or null when
    /// no icon font is installed.</summary>
    public static Bitmap? Glyph(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph) || Theme.IconFontName == null) return null;
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        using var f = Theme.IconFont(9.5f);
        path.AddString(glyph, f.FontFamily, 0, 13.5f, Point.Empty, StringFormat.GenericTypographic);
        var ink = path.GetBounds();
        if (ink.Width > 0)
        {
            using var mx = new Matrix();
            mx.Translate((16 - ink.Width) / 2f - ink.X, (16 - ink.Height) / 2f - ink.Y);
            path.Transform(mx);
            using var b = new SolidBrush(Theme.Subtle);
            g.FillPath(b, path);
        }
        return bmp;
    }

    public static Bitmap Dot(Color c)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 3, 3, 10, 10);
        using var pen = new Pen(Color.FromArgb(90, Color.White));
        g.DrawEllipse(pen, 3, 3, 9, 9);
        return bmp;
    }
}

/// <summary>Flat themed slider with an accent fill and a round thumb.</summary>
public class SleekSlider : Control
{
    private int _value = 50;
    private bool _dragging;

    public int Minimum { get; set; } = 1;
    public int Maximum { get; set; } = 100;
    /// <summary>Draws the track as a hue spectrum and colors the thumb by value.</summary>
    public bool RainbowTrack { get; set; }
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SleekSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 28;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); return; }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int pad = 9;
        int cy = Height / 2;
        int trackW = Width - pad * 2;
        double frac = (Value - Minimum) / (double)(Maximum - Minimum);
        int fillW = (int)(trackW * frac);

        if (RainbowTrack)
        {
            var trackRect = new Rectangle(pad, cy - 4, trackW, 8);
            using var spectrum = new LinearGradientBrush(trackRect, Color.Red, Color.Red, 0f);
            spectrum.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f }
            };
            using var trackPath = Theme.RoundedRect(trackRect, 4);
            g.FillPath(spectrum, trackPath);
        }
        else
        {
            using (var track = new SolidBrush(Theme.RowBg))
            using (var trackPath = Theme.RoundedRect(new Rectangle(pad, cy - 3, trackW, 6), 3))
                g.FillPath(track, trackPath);
            if (fillW > 4)
                using (var fill = new SolidBrush(Theme.Accent))
                using (var fillPath = Theme.RoundedRect(new Rectangle(pad, cy - 3, fillW, 6), 3))
                    g.FillPath(fill, fillPath);
        }

        int tx = pad + fillW;
        using (var thumbOuter = new SolidBrush(Theme.TextCol))
            g.FillEllipse(thumbOuter, tx - 8, cy - 8, 16, 16);
        Color inner = RainbowTrack
            ? Theme.HsvToColor(Value * 360.0 / Math.Max(1, Maximum))
            : _dragging ? Theme.Accent : Theme.PanelBg;
        using (var thumbInner = new SolidBrush(inner))
            g.FillEllipse(thumbInner, tx - 5, cy - 5, 10, 10);
    }

    /// <summary>The 1995 trackbar: a thin SUNKEN channel with a raised rectangular thumb riding it.
    /// A hue slider keeps its spectrum inside the channel - the colour is the control's whole point -
    /// but squared off and edged like any other inset of the era.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        const int pad = 9;
        int cy = Height / 2;
        int trackW = Width - pad * 2;
        if (trackW <= 0) return;
        double frac = (Value - Minimum) / (double)(Maximum - Minimum);
        int fillW = (int)(trackW * frac);

        var channel = new Rectangle(pad, cy - 3, trackW, 6);
        if (RainbowTrack)
        {
            using var spectrum = new LinearGradientBrush(channel, Color.Red, Color.Red, 0f);
            spectrum.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f }
            };
            g.FillRectangle(spectrum, channel);
        }
        else
        {
            using (var bed = new SolidBrush(Theme.FaceShadow)) g.FillRectangle(bed, channel);
            if (fillW > 0)
                using (var done = new SolidBrush(Theme.ClassicNavy))
                    g.FillRectangle(done, new Rectangle(channel.X, channel.Y, fillW, channel.Height));
        }
        Theme.Bevel(g, channel, raised: false, thin: true);

        var thumb = new Rectangle(pad + fillW - 5, cy - 9, 11, 19);
        using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, thumb);
        Theme.Bevel(g, thumb, raised: true);
    }

    private void SetFromMouse(int x)
    {
        const int pad = 9;
        double frac = (x - pad) / (double)(Width - pad * 2);
        Value = Minimum + (int)Math.Round(Math.Clamp(frac, 0, 1) * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        SetFromMouse(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int step = Math.Max(1, (Maximum - Minimum) / 100); // 3° on the hue bar, 1 elsewhere
        Value += Math.Sign(e.Delta) * step;
        if (e is HandledMouseEventArgs h) h.Handled = true; // don't also scroll a parent
    }
}

/// <summary>A settings row with a custom checkbox and label.</summary>
public class ToggleRow : Control
{
    private bool _hover;

    public bool Checked { get; set; }
    public event EventHandler? CheckedChanged;

    public ToggleRow(string text, bool initialChecked)
    {
        Text = text;
        Checked = initialChecked;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(400, 42);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic)
        {
            // A 95 check box sat straight on the dialog face - no row plate, no hover.
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = Theme.TextHint;
            using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, ClientRectangle);
            Theme.ClassicCheck(g, new Rectangle(12, Height / 2 - 6, 13, 13), Checked, Enabled);
            TextRenderer.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(34, 0, Width - 40, Height),
                Enabled ? Color.Black : Theme.FaceShadow,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(_hover ? Theme.RowHover : Theme.RowBg))
        using (var bgPath = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            g.FillPath(bg, bgPath);

        var box = new Rectangle(12, Height / 2 - 9, 18, 18);
        if (Checked)
        {
            using var fill = new SolidBrush(Theme.Accent);
            using var boxPath = Theme.RoundedRect(box, 5);
            g.FillPath(fill, boxPath);
            using var pen = new Pen(Theme.Bg, 2.2f);
            g.DrawLines(pen, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 7, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
        }
        else
        {
            using var pen = new Pen(Theme.Border, 1.6f);
            using var boxPath = Theme.RoundedRect(box, 5);
            g.DrawPath(pen, boxPath);
        }

        TextRenderer.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(40, 0, Width - 46, Height),
            Theme.TextCol, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
}

/// <summary>Flat button with rounded corners, hover/press states, and a configurable border.</summary>
public class RoundedButton : Control
{
    private bool _hover, _pressed;

    public int CornerRadius { get; set; } = 9;
    public Color FillColor { get; set; } = Theme.RowBg;
    public Color HoverFillColor { get; set; } = Theme.RowHover;
    public Color BorderColor { get; set; } = Theme.Border;
    public int BorderSize { get; set; } = 1;
    public bool Interactive { get; set; } = true;
    /// <summary>Treat Text as a single icon glyph: measure its actual ink box and fill
    /// it dead-center as a vector path. Font cell metrics (what TextRenderer centers
    /// on) sit icon-font glyphs visibly off-center.</summary>
    public bool IconGlyph { get; set; }
    /// <summary>Classic: draw as a LATCHED toolbar button - held down over the 50% dither a
    /// checked button wore. Set by the owner for "this is the selected one" buttons.</summary>
    public bool Latched { get; set; }

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.TextCol;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(9.5f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); return; }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill = FillColor;
        if (!Enabled) fill = Theme.Blend(FillColor, Theme.PanelBg, 0.45);
        else if (Interactive && _pressed) fill = Blend(fill, Color.Black, 0.18);
        else if (Interactive && _hover)
            // swatch-style buttons (Hover == Fill) still get a subtle lift on hover
            fill = HoverFillColor.ToArgb() == FillColor.ToArgb() ? Blend(FillColor, Color.White, 0.10) : HoverFillColor;

        Color textCol = Enabled ? ForeColor : Theme.Subtle;
        using (var path = Theme.RoundedRect(rect, CornerRadius))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            using var pen = new Pen(Enabled ? BorderColor : Theme.Blend(BorderColor, Theme.PanelBg, 0.45), BorderSize);
            g.DrawPath(pen, path);
        }
        if (string.IsNullOrEmpty(Text)) return;
        if (IconGlyph)
        {
            using var glyph = new GraphicsPath();
            glyph.AddString(Text, Font.FontFamily, (int)Font.Style,
                Font.SizeInPoints * g.DpiY / 72f, Point.Empty, StringFormat.GenericTypographic);
            var ink = glyph.GetBounds();
            using var center = new Matrix();
            center.Translate((rect.Width - ink.Width) / 2f - ink.X, (rect.Height - ink.Height) / 2f - ink.Y);
            glyph.Transform(center);
            using var brush = new SolidBrush(textCol);
            g.FillPath(brush, glyph);
            return;
        }
        // NoPadding: GDI's default side bearings are asymmetric, which sat every centred
        // caption ~2px left of true centre (plainest on narrow pills like "8" / "16").
        TextRenderer.DrawText(g, Text, Font, rect, textCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    /// <summary>The Windows 95 button: a raised face that goes sunken while held, its label
    /// stepping down-right with it. A colour well (a swatch, where hover == fill) keeps its own
    /// colour and wears a sunken edge instead. A latched button - the selected effect - stays
    /// pressed over the 50% dither a checked toolbar button wore.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Theme.TextHint;
        var full = new Rectangle(0, 0, Width, Height);
        bool isWell = string.IsNullOrEmpty(Text) && HoverFillColor.ToArgb() == FillColor.ToArgb();

        if (isWell)
        {
            using (var b = new SolidBrush(Enabled ? FillColor : Theme.Face)) g.FillRectangle(b, full);
            Theme.Bevel(g, full, raised: false);
            return;
        }

        bool latched = Latched;
        bool down = latched || (Interactive && _pressed);

        if (latched)
        {
            using var hatch = new System.Drawing.Drawing2D.HatchBrush(
                System.Drawing.Drawing2D.HatchStyle.Percent50, Theme.FaceHi, Theme.Face);
            g.FillRectangle(hatch, full);
        }
        else using (var b = new SolidBrush(Theme.Face)) g.FillRectangle(b, full);

        Theme.Bevel(g, full, raised: !down);

        if (string.IsNullOrEmpty(Text)) return;
        var tr = full;
        if (down) tr.Offset(1, 1);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (!Enabled)
        {
            // 1995 embossed a disabled label: a white ghost down-right, grey on top.
            var sh = tr; sh.Offset(1, 1);
            TextRenderer.DrawText(g, Text, Font, sh, Theme.FaceHi, flags);
            TextRenderer.DrawText(g, Text, Font, tr, Theme.FaceShadow, flags);
            return;
        }
        TextRenderer.DrawText(g, Text, Font, tr, Color.Black, flags);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private static Color Blend(Color a, Color b, double f) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
}

/// <summary>The clickable, slowly hue-drifting "PRISMA" wordmark in the header card.
/// Per-glyph saturated hues + a cheap translucent-stroke bloom, painted as vector paths
/// (like RoundedButton.IconGlyph) so it stays crisp over the translucent card. Owns its
/// own 20fps timer that pauses when the window hides to tray; invalidates only itself.</summary>
public sealed class RainbowTitle : Control
{
    private const string Word = "PRISMA";
    private const float HueSpread = 20f;     // degrees between adjacent letters
    private const float DegPerTick = 1.6f;   // ~12.5s per full wheel at 20fps
    private const float Inset = 8f;          // glow margin around the ink, each side

    private readonly System.Windows.Forms.Timer _t = new() { Interval = 50 };
    private float _phase = Environment.TickCount % 360;
    private GraphicsPath[]? _glyphs;
    private GraphicsPath? _whole;
    private float _dpi;
    private bool _hover;

    public event EventHandler? TitleClicked;

    public RainbowTitle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;       // composites over the translucent header card
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "About Prisma";
        AccessibleRole = AccessibleRole.PushButton;
        _t.Tick += (_, _) => { _phase = (_phase + DegPerTick) % 360f; Invalidate(); }; // only this control
    }

    // Pause while hidden to tray (a hidden control reports Visible=false).
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && !Theme.Classic) _t.Start(); else _t.Stop();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Size the control NOW (a 0x0 control never gets an OnPaint, so the lazy
        // size-in-paint would leave the wordmark invisible).
        using (var g = CreateGraphics()) BuildPaths(g.DpiY);
        if (Visible && !Theme.Classic) _t.Start();
    }

    private void BuildPaths(float dpiY)
    {
        if (_glyphs != null) foreach (var p in _glyphs) p.Dispose();
        _whole?.Dispose();

        using var f = Theme.DisplayFont(15f, FontStyle.Bold);
        float em = 15f * dpiY / 72f;         // same point->px conversion RoundedButton uses

        _whole = new GraphicsPath();
        _whole.AddString(Word, f.FontFamily, (int)FontStyle.Bold, em, Point.Empty, StringFormat.GenericTypographic);
        var ink = _whole.GetBounds();
        using (var mx = new Matrix()) { mx.Translate(Inset - ink.X, Inset - ink.Y); _whole.Transform(mx); }

        _glyphs = new GraphicsPath[Word.Length];
        float x = Inset;
        for (int i = 0; i < Word.Length; i++)
        {
            var gp = new GraphicsPath();
            gp.AddString(Word[i].ToString(), f.FontFamily, (int)FontStyle.Bold, em, Point.Empty, StringFormat.GenericTypographic);
            var gi = gp.GetBounds();
            using (var mx = new Matrix()) { mx.Translate(x - gi.X, Inset - ink.Y); gp.Transform(mx); }
            _glyphs[i] = gp;
            x += gi.Width + em * 0.06f;       // ink width + a little tracking
        }
        Size = new Size((int)Math.Ceiling(x + Inset), (int)Math.Ceiling(ink.Height + Inset * 2));
        _dpi = dpiY;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic)
        {
            // A title bar carried flat white bold text. No glow, no per-letter hues, no curves.
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = Theme.TextHint;
            TextRenderer.DrawText(g, Word, Theme.UiFontShared(9f, FontStyle.Bold),
                new Rectangle(4, 0, Width - 8, Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_glyphs == null || _whole == null || _dpi != g.DpiY) BuildPaths(g.DpiY);

        // (1) bloom: two widening translucent strokes of the whole word in the mid-hue;
        //     hover bumps the alpha so the title visibly lights up (clickable affordance).
        var glow = Theme.HsvToColor(_phase + Word.Length / 2f * HueSpread);
        int boost = _hover ? 16 : 0;
        foreach (var (w, a) in new[] { (5f, 18 + boost), (3f, 30 + boost) })
            using (var pen = new Pen(Color.FromArgb(Math.Min(a, 255), glow), w) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, _whole!);

        // (2) crisp per-glyph rainbow on top — one saturated hue per letter.
        for (int i = 0; i < _glyphs!.Length; i++)
            using (var b = new SolidBrush(Theme.HsvToColor(_phase + i * HueSpread, 0.95, 1.0)))
                g.FillPath(b, _glyphs[i]);

        if (Focused && ShowFocusCues) // keyboard focus only, not the initial/mouse focus
            using (var pen = new Pen(Theme.Accent, 1.2f))
                g.DrawPath(pen, Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { TitleClicked?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { TitleClicked?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _t.Dispose();
            _whole?.Dispose();
            if (_glyphs != null) foreach (var p in _glyphs) p.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>A device entry with a custom checkbox, name, and source line.</summary>
public class DeviceRow : Control
{
    private bool _hover;

    public IRgbDevice Device { get; }
    public bool Checked { get; private set; }
    /// <summary>Shown after the source line when the row has its own effect override.</summary>
    public string? EffectNote { get; set; }
    public event EventHandler? CheckedChanged;

    /// <summary>Programmatic check-state change (profile apply); skips CheckedChanged
    /// so the caller can batch updates and re-apply the effect once.</summary>
    public void SetChecked(bool value)
    {
        if (Checked == value) return;
        Checked = value;
        Invalidate();
    }

    /// <summary>Stable identity key; disambiguates same-named devices.</summary>
    public string Key { get; set; } = "";
    /// <summary>The device's current base color, shown as a chip on the row.</summary>
    public Color DisplayColor { get; set; } = Theme.RowBg;
    /// <summary>True while this row is the active target of the color controls.</summary>
    public bool Targeted { get; set; }
    /// <summary>True when this row is a member of a collapsed/expanded device group (e.g. one of
    /// the 4 identical RAM sticks) — drawn indented under its <see cref="DeviceGroupRow"/> header.</summary>
    public bool InGroup { get; set; }
    /// <summary>Per-device static-color fade state (each device smooths to its own color).</summary>
    public FadeState Fade { get; } = new();

    public DeviceRow(IRgbDevice device, bool initialChecked = true)
    {
        Device = device;
        Checked = initialChecked;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(245, 44); // fits the devices card with its scrollbar, no horizontal scroll
        Margin = new Padding(0, 0, 0, 5);
        // cursor is set per-region in OnMouseMove (hand over the checkbox, arrow elsewhere)
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // one constants block; every coordinate below derives from these (no bare literals)
        const int Pad = 12, Gutter = 12, ChipW = 18, ChipH = 14, BadgeW = 56, NameX = 40;
        const int Line1 = 7, Line2 = 24;

        var g = e.Graphics;
        g.SmoothingMode = Theme.ShapeMode;
        g.TextRenderingHint = Theme.TextHint;
        bool sel = Theme.Classic && Targeted;   // a selected list item: navy band, white text
        var outer = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using (var bg = new SolidBrush(sel ? Theme.ClassicNavy : _hover ? Theme.RowHover : Theme.RowBg))
            g.FillPath(bg, outer);
        if (Targeted && !Theme.Classic)
            using (var hl = new Pen(Theme.Accent, 1.6f))
                g.DrawPath(hl, outer);
        outer.Dispose();

        // enable control: a CIRCLE (deliberately not a square) so it never reads as a second
        // colour swatch alongside the square DisplayColor chip on the right — the two used to
        // look like duplicate green controls whenever the lighting colour was green.
        var box = new Rectangle(Pad, Height / 2 - 9, 18, 18);
        if (Theme.Classic)
        {
            Theme.ClassicCheck(g, new Rectangle(Pad + 2, Height / 2 - 6, 13, 13), Checked);
        }
        else if (Checked)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillEllipse(fill, box);
            using var pen = new Pen(Theme.Bg, 2.2f);
            g.DrawLines(pen, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 7, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
        }
        else
        {
            using var pen = new Pen(Theme.Border, 1.6f);
            g.DrawEllipse(pen, box);
        }

        // right rail: a fixed-width badge with the colour chip centred directly beneath it,
        // both flush to the same right margin so they align for any Kind length.
        int rightEdge = Width - Pad;
        if (!string.IsNullOrEmpty(Device.Kind))
        {
            var badge = new Rectangle(rightEdge - BadgeW, Line1 + 1, BadgeW, 16);
            if (Theme.Classic) Theme.Bevel(g, badge, raised: false, thin: true);
            else
            {
                using var pen = new Pen(Theme.Border, 1f);
                using var badgePath = Theme.RoundedRect(badge, 7);
                g.DrawPath(pen, badgePath);
            }
            TextRenderer.DrawText(g, Device.Kind, Theme.UiFontShared(7f, FontStyle.Bold), badge,
                sel ? Color.White : Theme.Subtle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        var chip = new Rectangle(rightEdge - (BadgeW + ChipW) / 2, Line2 + 1, ChipW, ChipH);
        if (Theme.Classic)
        {
            using (var chipFill = new SolidBrush(Checked ? DisplayColor : Theme.Blend(DisplayColor, Theme.Face, 0.6)))
                g.FillRectangle(chipFill, chip);
            Theme.Bevel(g, chip, raised: false, thin: true);
        }
        else using (var chipPath = Theme.RoundedRect(chip, 4))
        {
            using (var chipFill = new SolidBrush(Checked ? DisplayColor : Theme.Blend(DisplayColor, Theme.RowBg, 0.6)))
                g.FillPath(chipFill, chipPath);
            using (var chipPen = new Pen(Theme.Border, 1f))
                g.DrawPath(chipPen, chipPath);
        }

        // text column: name + sub-line share one right limit (the rail)
        int railW = BadgeW + Gutter;
        int textW = Width - NameX - Pad - railW;
        var nameCol = sel ? Color.White : Checked ? Theme.TextCol : Theme.Subtle;
        TextRenderer.DrawText(g, Device.Name, Theme.UiFontShared(9.5f, FontStyle.Bold),
            new Rectangle(NameX, Line1, textW, 20), nameCol,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
        // Sub-line stays terse: just the zone count (the backend/Source repeated on every row —
        // "OpenRGB" ×5 — is noise here; it's still one hover away in the row tooltip).
        string sub = Device.LedCount == 1 ? "1 zone" : $"{Device.LedCount} zones";
        if (EffectNote != null) sub += $"  ·  ✦ {EffectNote}";
        TextRenderer.DrawText(g, sub, Theme.UiFontShared(8f),
            new Rectangle(NameX, Line2, textW, 16),
            sel ? Color.White : EffectNote != null ? Theme.Accent : Theme.Subtle,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
    }

    /// <summary>Toggling is deliberate: only a LEFT click on the checkbox column flips
    /// the device. A right click (handled by the owner) opens the per-device effect menu
    /// and must never toggle — the old OnClick override fired for the right button too,
    /// so right-clicking a device instantly turned it off.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && CheckboxHit(e.Location))
        {
            Checked = !Checked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseUp(e); // raise MouseUp so the owner's right-click menu still fires
    }

    /// <summary>The checkbox plus a comfortable margin around it (the left column).</summary>
    private bool CheckboxHit(Point p)
    {
        var box = new Rectangle(12, Height / 2 - 9, 18, 18);
        box.Inflate(12, 11); // ~left 42px, full row height — easy to hit, still clearly "the checkbox"
        return box.Contains(p);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // hand cursor only over the checkbox column, so it's clear where a click toggles
        Cursor = CheckboxHit(e.Location) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
}

/// <summary>Header row standing in for a run of identical devices (e.g. the four ENE DRAM
/// sticks): one shared enable circle + colour chip, an "×N · Σ zones" sub-line, a type badge,
/// and a chevron that expands/collapses the member rows. The members stay the real
/// <see cref="DeviceRow"/>s (so all effect/apply/target logic is unchanged) — this is a display
/// proxy that toggles them together and hides them when collapsed.</summary>
public class DeviceGroupRow : Control
{
    private bool _hover;
    public IReadOnlyList<DeviceRow> Members { get; }
    public bool Expanded { get; set; }
    /// <summary>The shared enable circle was clicked (toggle every member).</summary>
    public event EventHandler? ToggleAllRequested;
    /// <summary>The chevron / row was clicked to expand or collapse.</summary>
    public event EventHandler? ExpandToggled;

    public DeviceGroupRow(IReadOnlyList<DeviceRow> members)
    {
        Members = members;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(245, 44);
        Margin = new Padding(0, 0, 0, 5);
    }

    private bool AllChecked => Members.All(m => m.Checked);
    private bool AnyChecked => Members.Any(m => m.Checked);

    protected override void OnPaint(PaintEventArgs e)
    {
        const int Pad = 12, ChipW = 18, ChipH = 14, BadgeW = 56, NameX = 40, Line1 = 7, Line2 = 24;
        var g = e.Graphics;
        g.SmoothingMode = Theme.ShapeMode;
        g.TextRenderingHint = Theme.TextHint;
        using (var outer = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
        using (var bg = new SolidBrush(_hover ? Theme.RowHover : Theme.RowBg))
            g.FillPath(bg, outer);

        // shared enable circle: filled = all on, dash = mixed, hollow = all off
        var box = new Rectangle(Pad, Height / 2 - 9, 18, 18);
        if (Theme.Classic)
        {
            // 1995's three-state box: ticked, or a tick on a grey well for "some of them".
            var cb = new Rectangle(Pad + 2, Height / 2 - 6, 13, 13);
            if (AnyChecked && !AllChecked)
            {
                Theme.ClassicField(g, cb, Theme.Face);
                Theme.ClassicCheck(g, cb, true, enabled: false);
            }
            else Theme.ClassicCheck(g, cb, AllChecked);
        }
        else if (AllChecked)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillEllipse(fill, box);
            using var pen = new Pen(Theme.Bg, 2.2f);
            g.DrawLines(pen, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 7, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
        }
        else if (AnyChecked)
        {
            using var fill = new SolidBrush(Theme.Blend(Theme.Accent, Theme.RowBg, 0.5));
            g.FillEllipse(fill, box);
            using var pen = new Pen(Theme.Bg, 2.2f);
            g.DrawLine(pen, box.X + 5, box.Y + 9, box.X + 13, box.Y + 9);
        }
        else
        {
            using var pen = new Pen(Theme.Border, 1.6f);
            g.DrawEllipse(pen, box);
        }

        // right rail: type badge + shared colour chip
        int rightEdge = Width - Pad;
        string kind = Members[0].Device.Kind;
        if (!string.IsNullOrEmpty(kind))
        {
            var badge = new Rectangle(rightEdge - BadgeW, Line1 + 1, BadgeW, 16);
            using var pen = new Pen(Theme.Border, 1f);
            using var badgePath = Theme.RoundedRect(badge, 7);
            g.DrawPath(pen, badgePath);
            TextRenderer.DrawText(g, kind, Theme.UiFontShared(7f, FontStyle.Bold), badge, Theme.Subtle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        var chip = new Rectangle(rightEdge - (BadgeW + ChipW) / 2, Line2 + 1, ChipW, ChipH);
        Color chipColor = Members[0].DisplayColor;
        using (var chipPath = Theme.RoundedRect(chip, 4))
        {
            using (var cf = new SolidBrush(AnyChecked ? chipColor : Theme.Blend(chipColor, Theme.RowBg, 0.6))) g.FillPath(cf, chipPath);
            using (var cp = new Pen(Theme.Border, 1f)) g.DrawPath(cp, chipPath);
        }

        // chevron just left of the rail (▾ expanded / ▸ collapsed)
        int railW = BadgeW + 12;
        int chevX = Width - Pad - railW - 10;
        float chevY = Height / 2f;
        using (var cpen = new Pen(Theme.Subtle, 1.6f))
        {
            if (Expanded) g.DrawLines(cpen, new[] { new PointF(chevX - 4, chevY - 2), new PointF(chevX, chevY + 3), new PointF(chevX + 4, chevY - 2) });
            else g.DrawLines(cpen, new[] { new PointF(chevX - 2, chevY - 4), new PointF(chevX + 3, chevY), new PointF(chevX - 2, chevY + 4) });
        }

        // text: name + "×N · Σ zones"
        int textW = Width - NameX - Pad - railW - 22;
        Color nameCol = AnyChecked ? Theme.TextCol : Theme.Subtle;
        TextRenderer.DrawText(g, Members[0].Device.Name, Theme.UiFontShared(9.5f, FontStyle.Bold),
            new Rectangle(NameX, Line1, textW, 20), nameCol,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
        int zones = Members.Sum(m => m.Device.LedCount);
        TextRenderer.DrawText(g, $"×{Members.Count}  ·  {zones} zones", Theme.UiFontShared(8f),
            new Rectangle(NameX, Line2, textW, 16), Theme.Subtle,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
    }

    private bool CircleHit(Point p)
    {
        var box = new Rectangle(12, Height / 2 - 9, 18, 18);
        box.Inflate(12, 11);
        return box.Contains(p);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (CircleHit(e.Location)) ToggleAllRequested?.Invoke(this, EventArgs.Empty);
            else { Expanded = !Expanded; Invalidate(); ExpandToggled?.Invoke(this, EventArgs.Empty); }
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) { Cursor = Cursors.Hand; base.OnMouseMove(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
}

/// <summary>A settings row in the same rounded-card style as <see cref="ToggleRow"/>,
/// but hosting inline controls (combos, button groups) placed by the caller: label on
/// the left, controls right-aligned. Paints the label dimmed while disabled, for
/// sub-rows that belong to an unchecked toggle.</summary>
public class OptionRow : Control
{
    /// <summary>Label x within the row. Default 12; sub-rows indented 24px under a ToggleRow
    /// set 16 so their label ink lines up exactly with the parent's caption (24+16 = 0+40).</summary>
    public int TextInset { get; set; } = 12;

    public OptionRow(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(400, 42);
        BackColor = Theme.Bg;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.RowBg))
        using (var bgPath = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            g.FillPath(bg, bgPath);
        TextRenderer.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(TextInset, 0, Width - TextInset - 6, Height),
            Enabled ? Theme.TextCol : Theme.Subtle,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>The window's soft multi-color gradient backdrop ("mesh gradient"), rendered
/// once per window size into a cached bitmap and sliced out by whichever control paints
/// it. The blob hues derive from the accent color, so re-theming re-colors the backdrop —
/// call <see cref="Invalidate"/> after Theme.SetAccent.</summary>
public static class Backdrop
{
    private static Bitmap? _bmp;

    /// <summary>Glow strength 0-100: scales the blob alphas (0 = plain flat background).</summary>
    public static int Intensity = 100;

    /// <summary>While the acrylic "liquid glass" is on, the gradient steps aside: the
    /// backdrop paints flat black, which a GDI window surface reports as alpha-0 — fully
    /// transparent to the DWM acrylic — so the real blurred desktop shows through.
    /// Painting the gradient as well would composite additively over the blur.</summary>
    public static bool Glass;

    // The gradient renders ONCE at this fixed low resolution, is upscaled ONCE per window
    // size into a full-client-area cache (_scaled), and every panel then copies its slice
    // out of that cache 1:1 (a plain pixel blit, no resampling). PathGradientBrush fills
    // are CPU-rasterized and far too slow to re-render per resize tick; and a per-panel
    // bilinear upscale (the previous approach) paid the resample cost once for EVERY
    // translucent surface every paint — multiplied by panel count and again by display
    // resolution, it visibly stuttered during a drag. On content this soft a single
    // bilinear upscale is indistinguishable from a native render.
    private const int RW = 480, RH = 300;
    private static Bitmap? _scaled;       // _bmp upscaled to the current client size
    private static Size _scaledSize;      // the client size _scaled was built for

    public static void Invalidate()
    {
        _bmp?.Dispose();
        _bmp = null;
        _scaled?.Dispose();
        _scaled = null;
        _scaledSize = Size.Empty;
    }

    /// <summary>Draws this control's slice of the window backdrop.</summary>
    public static void Paint(Graphics g, Control c)
    {
        if (Theme.Classic) { g.Clear(Theme.Face); return; }
        var form = c.FindForm();
        if (form == null || form.ClientSize.Width < 1 || form.ClientSize.Height < 1)
        {
            g.Clear(Theme.Bg);
            return;
        }
        if (Glass)
        {
            g.Clear(Color.Black);
            return;
        }
        var size = form.ClientSize;
        if (_scaled == null || _scaledSize != size) BuildScaled(size);
        var origin = form.PointToClient(c.PointToScreen(Point.Empty));
        var prevIm = g.InterpolationMode;
        var prevPo = g.PixelOffsetMode;
        // src and dest are the same pixel size -> NearestNeighbor is an exact copy, not a
        // resample; PixelOffsetMode.Half makes the 1:1 blit pixel-perfect (no half-px shift).
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_scaled!, new Rectangle(0, 0, c.Width, c.Height),
            origin.X, origin.Y, c.Width, c.Height, GraphicsUnit.Pixel);
        g.InterpolationMode = prevIm;
        g.PixelOffsetMode = prevPo;
    }

    // Upscale the low-res gradient to the full client size exactly once per size change.
    // Opaque 32bppRgb: the gradient fully covers, so no per-pixel alpha is needed and the
    // per-panel slice copies are as cheap as a memcpy.
    private static void BuildScaled(Size size)
    {
        _bmp ??= Render();
        _scaled?.Dispose();
        _scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(_scaled);
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY); // no transparent edge bleed at the borders
        g.DrawImage(_bmp, new Rectangle(0, 0, size.Width, size.Height),
            0, 0, RW, RH, GraphicsUnit.Pixel, attrs);
        _scaledSize = size;
    }

    private static Bitmap Render()
    {
        var bmp = new Bitmap(RW, RH, PixelFormat.Format32bppRgb); // opaque: cheaper blits
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Bg);
        int k = Math.Clamp(Intensity, 0, 100);
        if (k == 0) return bmp;
        double hue = Theme.Accent.GetHue();
        var size = new Size(RW, RH);
        // four soft radial blobs, kept dark enough that white text stays readable
        Blob(g, size, -0.10, -0.05, 0.95, Hsv(hue, 0.80, 0.62, 125 * k / 100));       // cool accent, top-left
        Blob(g, size, 1.05, 0.05, 0.85, Hsv(hue + 40, 0.85, 0.40, 100 * k / 100));    // deep shade, top-right
        Blob(g, size, -0.05, 1.05, 0.85, Hsv(hue + 150, 0.88, 0.60, 115 * k / 100));  // warm counterpoint, bottom-left
        Blob(g, size, 0.45, 1.20, 0.75, Hsv(hue + 185, 0.92, 0.66, 130 * k / 100));   // glow, bottom-center
        return bmp;
    }

    private static void Blob(Graphics g, Size s, double cx, double cy, double r, Color c)
    {
        float radius = (float)(r * Math.Max(s.Width, s.Height));
        using var path = new GraphicsPath();
        path.AddEllipse((float)(cx * s.Width) - radius, (float)(cy * s.Height) - radius, radius * 2, radius * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = c,
            SurroundColors = new[] { Color.FromArgb(0, c) }
        };
        g.FillPath(brush, path);
    }

    private static Color Hsv(double h, double s, double v, int a)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = ((int)(h / 60)) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x)
        };
        return Color.FromArgb(a, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}

/// <summary>A panel whose background is its slice of the shared window backdrop.</summary>
public class GradientPanel : Panel
{
    public GradientPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(e.Graphics, this);
}

/// <summary>Rounded section card that hosts children. Paints everything in
/// OnPaintBackground so transparent children (RoundedButton, SleekSlider,
/// LivePreview) composite over the card fill via PaintTransparentBackground.
/// Do NOT move the painting to OnPaint.</summary>
public class CardPanel : Panel
{
    public string? Title { get; set; }
    /// <summary>Classic only: paint this card as a window TITLE BAR (the navy caption gradient)
    /// rather than a group box. Set on the header card, which is where the app's chrome lives.</summary>
    public bool CaptionStyle { get; set; }
    public int CornerRadius { get; set; } = 10;
    /// <summary>Translucent cards let the window gradient glow through ("smoked glass");
    /// turn off for cards hosting opaque scrollable children (the device list).</summary>
    public bool Translucent { get; set; } = true;

    public CardPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; // ambient: child labels composite over the card paint
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); return; }
        Backdrop.Paint(g, this);                         // corner pixels = the window gradient
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        var fillColor = Translucent ? Color.FromArgb(206, Theme.PanelBg) : Theme.PanelBg;
        using (var fill = new SolidBrush(fillColor)) g.FillPath(fill, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
        if (!string.IsNullOrEmpty(Title))
            TextRenderer.DrawText(g, Title, Theme.UiFontShared(9f, FontStyle.Bold), new Point(14, 12), Theme.Accent,
                TextFormatFlags.NoPrefix); // Accent read per paint, so SetAccent re-themes it
    }

    /// <summary>The 1995 group box: a flat window face inside an ETCHED frame (a thin sunken ring),
    /// with the caption sitting ON the top line over a gap cut in it.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Theme.TextHint;
        using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, ClientRectangle);

        if (CaptionStyle)
        {
            // A 95 window: the raised face of the frame, with the caption band inset in it.
            Theme.Bevel(g, new Rectangle(0, 0, Width, Height), raised: true);
            Theme.ClassicCaption(g, new Rectangle(3, 3, Width - 6, 26));   // covers the wordmark + version row
            return;
        }

        var font = Theme.UiFontShared(9f);
        int top = string.IsNullOrEmpty(Title) ? 0 : font.Height / 2;
        var frame = new Rectangle(0, top, Width - 1, Height - top - 1);
        Theme.Bevel(g, frame, raised: false, thin: true);

        if (string.IsNullOrEmpty(Title)) return;
        var size = TextRenderer.MeasureText(g, Title, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        // cut the frame line so the caption sits in the gap, the way a group box did
        using (var face = new SolidBrush(Theme.Face))
            g.FillRectangle(face, 10, frame.Top, size.Width + 8, 2);
        TextRenderer.DrawText(g, Title, font, new Point(13, 0), Color.Black,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

/// <summary>Mutable static-color fade accumulator. A plain reference type so it can be
/// shared/mutated without passing a Control field by ref (Control is MarshalByRefObject).</summary>
public sealed class FadeState
{
    public double R = -1, G, B;
    // Music effect per-device state: falling peak-hold indicator.
    public double Peak, HoldUntil, LastT;
}

/// <summary>A horizontal strip that visualizes the exact colors being pushed to the
/// hardware this frame (one segment per LED).</summary>
public class LivePreview : Control
{
    private uint[] _colors = Array.Empty<uint>();

    public LivePreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 34;
    }

    /// <summary>Updates the displayed colors (packed r|g&lt;&lt;8|b&lt;&lt;16). Repaints only on change.</summary>
    public void SetColors(uint[] colors)
    {
        if (_colors.AsSpan().SequenceEqual(colors)) return;
        _colors = (uint[])colors.Clone();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = Theme.ShapeMode;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (Theme.Classic) Theme.Bevel(g, new Rectangle(0, 0, Width, Height), raised: false);
        using var clip = Theme.RoundedRect(Theme.Classic ? Rectangle.Inflate(rect, -2, -2) : rect, 8);
        using (var bg = new SolidBrush(Theme.Classic ? Color.Black : Theme.RowBg)) g.FillPath(bg, clip);

        int n = _colors.Length;
        if (n > 0 && Width > 0)
        {
            g.SetClip(clip);
            double seg = (double)Width / n;
            for (int i = 0; i < n; i++)
            {
                int r = (int)(_colors[i] & 0xFF), gg = (int)((_colors[i] >> 8) & 0xFF), b = (int)((_colors[i] >> 16) & 0xFF);
                int x0 = (int)Math.Floor(i * seg);
                int x1 = (int)Math.Floor((i + 1) * seg);
                using var br = new SolidBrush(Color.FromArgb(r, gg, b));
                g.FillRectangle(br, x0, 0, Math.Max(1, x1 - x0), Height);
            }
            g.ResetClip();
        }
        using (var pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, clip);
    }
}
