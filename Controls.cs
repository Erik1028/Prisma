using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RGBCommander;

public static partial class Theme
{
    /// <summary>The surface and text tokens, by name.</summary>
    internal enum Tok { Bg, PanelBg, RailBg, RowBg, RowHover, Border, HairLine, TextCol, Subtle, Faint, ErrorCol }

    /// <summary>
    /// Every surface/text token in BOTH skins - one table, so the start-up values, ApplySurfaces and
    /// Restyle's Remap can never drift apart again (a hand-copied second list would leave SettingsForm's
    /// grey labels matching nothing after a skin switch). Modern is Mixtape's Graphite layering:
    /// wallpaper, rail, content card and controls get lighter in steps, so nothing needs an outline to
    /// separate it. Declared FIRST in the class: the properties below are initialised from it, and field
    /// initialisers only run in textual order within one file.
    /// </summary>
    internal static readonly (Color Modern, Color Classic)[] Surf =
    {
        /* Bg       */ (Color.FromArgb(13, 14, 16),    Color.FromArgb(192, 192, 192)),
        /* PanelBg  */ (Color.FromArgb(29, 30, 34),    Color.FromArgb(192, 192, 192)),
        /* RailBg   */ (Color.FromArgb(22, 23, 26),    Color.FromArgb(192, 192, 192)),
        /* RowBg    */ (Color.FromArgb(38, 39, 43),    Color.White),
        /* RowHover */ (Color.FromArgb(48, 50, 55),    Color.FromArgb(230, 230, 230)),
        /* Border   */ (Color.FromArgb(52, 54, 59),    Color.FromArgb(128, 128, 128)),
        /* HairLine */ (Color.FromArgb(44, 46, 51),    Color.FromArgb(206, 206, 206)),
        /* TextCol  */ (Color.FromArgb(242, 242, 243), Color.Black),
        /* Subtle   */ (Color.FromArgb(168, 172, 176), Color.FromArgb(56, 56, 56)),
        /* Faint    */ (Color.FromArgb(138, 143, 152), Color.FromArgb(104, 104, 104)),
        /* ErrorCol */ (Color.FromArgb(255, 120, 110), Color.FromArgb(128, 0, 0)),
    };

    /// <summary>The window back colour, under the wallpaper.</summary>
    public static Color Bg { get; private set; } = Surf[(int)Tok.Bg].Modern;
    /// <summary>The one content card.</summary>
    public static Color PanelBg { get; private set; } = Surf[(int)Tok.PanelBg].Modern;
    /// <summary>The left rail card (devices, profiles). Read at paint time only - never bake it into a
    /// property: in Classic it shares the window face with Bg and PanelBg, so Remap could not tell it apart.</summary>
    public static Color RailBg { get; private set; } = Surf[(int)Tok.RailBg].Modern;
    public static Color RowBg { get; private set; } = Surf[(int)Tok.RowBg].Modern;
    public static Color RowHover { get; private set; } = Surf[(int)Tok.RowHover].Modern;
    public static Color HairLine { get; private set; } = Surf[(int)Tok.HairLine].Modern;
    /// <summary>The third text tier: section captions, counts, hints.</summary>
    public static Color Faint { get; private set; } = Surf[(int)Tok.Faint].Modern;
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
    public static Color TextCol { get; private set; } = Surf[(int)Tok.TextCol].Modern;
    public static Color Subtle { get; private set; } = Surf[(int)Tok.Subtle].Modern;
    public static Color Border { get; private set; } = Surf[(int)Tok.Border].Modern;
    public static Color ErrorCol { get; private set; } = Surf[(int)Tok.ErrorCol].Modern;

    /// <summary>The accent lifted toward white: active glyphs, the selected tile's label. Classic has one
    /// accent only, selection navy. Paint-time only, never baked.</summary>
    public static Color AccentBright => Classic ? ClassicNavy : Blend(Accent, Color.White, 0.28);

    /// <summary>A glyph that sits ON an accent fill (the power disc): dark on a bright accent, white on a
    /// dark one. Paint-time only, never baked.</summary>
    public static Color OnAccent => Classic ? Color.White
        : (0.299 * Accent.R + 0.587 * Accent.G + 0.114 * Accent.B) > 130 ? Color.FromArgb(12, 16, 18) : Color.White;

    /// <summary>Legible ink for text or a glyph over an arbitrary colour (a light colour on a tile).</summary>
    public static Color OnColor(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 145 ? Color.FromArgb(20, 22, 26) : Color.FromArgb(244, 255, 255, 255);

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

    /// <summary>A rounded rectangle on a fractional grid (half-pixel-inset card edges, small glyph
    /// squares). Squared in Classic, like the integer overload.</summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (Classic) radius = 0;
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Cached shared display font (the wordmark) - NEVER dispose. Keyed by skin like
    /// UiFontShared: the same request resolves to a different face in Classic.</summary>
    public static Font DisplayFontShared(float size, FontStyle style = FontStyle.Regular)
    {
        if (!s_displayCache.TryGetValue((size, style, Classic), out var f))
            s_displayCache[(size, style, Classic)] = f = DisplayFont(size, style);
        return f;
    }
    private static readonly Dictionary<(float, FontStyle, bool), Font> s_displayCache = new();

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
/// rounded hover, themed separators, accent checks, accent section headers. In Classic, Mixtape's 1995
/// menu: a raised grey window, a solid navy band under the pointer, etched rules, embossed disabled
/// items, the pixel tick and the solid arrow, all in crisp type.</summary>
public class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

    /// <summary>The menu's surface: the window face in Classic (PanelBg is the WHITE of a 95 field).</summary>
    public static Color Surface => Theme.Classic ? Theme.Face : Theme.PanelBg;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        => e.Graphics.Clear(Surface);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var r = e.AffectedBounds;
        if (Theme.Classic) { Theme.Bevel(e.Graphics, new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height), raised: true); return; }
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        if (Theme.Classic) return;   // a 95 menu has no gutter: the face runs under the check column
        base.OnRenderImageMargin(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        if (Theme.Classic)   // a solid navy band across the row, inside the menu's edge
        {
            using var nb = new SolidBrush(Theme.ClassicNavy);
            g.FillRectangle(nb, 3, 0, e.Item.Width - 6, e.Item.Height);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var b = new SolidBrush(Theme.RowHover);
        using var path = Theme.RoundedRect(rect, 6);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (Theme.Classic && e.TextDirection == ToolStripTextDirection.Horizontal)
        {
            var g = e.Graphics;
            var tr = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width + 4, e.Item.Height);
            const TextFormatFlags F = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var font = e.Item is MenuHeader ? Theme.UiFontShared(8f, FontStyle.Bold) : e.TextFont;
            if (!e.Item.Enabled)   // the embossed grey of a disabled command: white one pixel down-right, grey on top
            {
                CrispText.DrawText(g, e.Text, font, new Rectangle(tr.X + 1, tr.Y + 1, tr.Width, tr.Height), Theme.FaceHi, F);
                CrispText.DrawText(g, e.Text, font, tr, Theme.FaceShadow, F);
            }
            else CrispText.DrawText(g, e.Text, font, tr, e.Item.Selected ? Color.White : Color.Black, F);
            return;
        }
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
        if (Theme.Classic)   // the etched rule of a 95 menu, nearly edge to edge
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var sh = new Pen(Theme.FaceShadow);
            using var hi = new Pen(Theme.FaceHi);
            e.Graphics.DrawLine(sh, 3, y - 1, e.Item.Width - 4, y - 1);
            e.Graphics.DrawLine(hi, 3, y, e.Item.Width - 4, y);
            return;
        }
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 26, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (Theme.Classic)   // the solid black (white when highlighted) submenu arrow
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            var r = e.ArrowRectangle;
            using var ab = new SolidBrush(e.Item?.Selected == true ? Color.White : Theme.FaceDark);
            int ax = r.Left + r.Width / 2 - 2, ay = r.Top + r.Height / 2;
            for (int i = 0; i < 4; i++) g.FillRectangle(ab, ax + i, ay - 3 + i, 1, 7 - 2 * i);
            return;
        }
        e.ArrowColor = e.Item is { Selected: true } ? Theme.TextCol : Theme.Subtle;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic)   // the era's pixel tick, in the text colour (white on the navy band)
        {
            g.SmoothingMode = SmoothingMode.None;
            using var kb = new SolidBrush(e.Item.Selected ? Color.White : Theme.FaceDark);
            var ir = e.ImageRectangle;
            int kx = ir.X + ir.Width / 2 - 3, ky = e.Item.Height / 2 - 3;
            int[] top = { 2, 3, 4, 3, 2, 1, 0 };
            for (int i = 0; i < 7; i++) g.FillRectangle(kb, kx + i, ky + top[i], 1, 3);
            return;
        }
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
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
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
            BackColor = ThemedMenuRenderer.Surface,
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
        // no Fluent icons in a 1995 menu (colour dots stay: they carry information)
        if (string.IsNullOrEmpty(glyph) || Theme.IconFontName == null || Theme.Classic) return null;
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

//// <summary>Flat themed slider with an accent fill and a round thumb.</summary>
public class SleekSlider : Control
{
    private int _value = 50;
    private bool _dragging;
    private bool _blocked;

    public int Minimum { get; set; } = 1;
    public int Maximum { get; set; } = 100;
    /// <summary>Draws the track as a hue spectrum and colors the thumb by value.</summary>
    public bool RainbowTrack { get; set; }
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Not in use for the current effect (Mixtape's idea): the control stays in place so nothing
    /// reflows, paints a faded track with no knob, and ignores mouse, wheel and keys - but stays ENABLED,
    /// so its tooltip can still say why (a disabled child window never sees the hover).
    /// </summary>
    public bool Blocked
    {
        get => _blocked;
        set
        {
            if (_blocked == value) return;
            _blocked = value;
            _dragging = false;
            TabStop = !value;
            Cursor = value || Theme.Classic ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    private bool Live => Enabled && !_blocked;

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
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Height = 28;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Cursor = !Live || Theme.Classic ? Cursors.Default : Cursors.Hand;   // 1995 had no hand cursor
        base.OnMouseEnter(e);
    }

    private static readonly Color[] Spectrum =
        { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
    private static readonly float[] SpectrumAt = { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); return; }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // a 6 px track covers exactly 6 crisp rows
        const int pad = 9;
        int cy = Height / 2;
        if (!Live)
        {
            // blocked: a faded track, no fill, no knob - present, but not in use
            var tr = new Rectangle(pad, cy - 3, Math.Max(0, Width - pad * 2), 6);
            using var tp = Theme.RoundedRect(tr, 3);
            if (RainbowTrack)
            {
                using var spectrum = new LinearGradientBrush(Rectangle.Inflate(tr, 1, 0), Color.Red, Color.Red, 0f);
                spectrum.InterpolationColors = new ColorBlend
                {
                    Colors = Spectrum.Select(c => Color.FromArgb(70, c)).ToArray(),
                    Positions = SpectrumAt,
                };
                g.FillPath(spectrum, tp);
            }
            else using (var tb = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillPath(tb, tp);
            return;
        }
        int trackW = Width - pad * 2;
        double frac = (Value - Minimum) / (double)(Maximum - Minimum);
        int fillW = (int)(trackW * frac);

        if (RainbowTrack)
        {
            var trackRect = new Rectangle(pad, cy - 4, trackW, 8);
            using var spectrum = new LinearGradientBrush(Rectangle.Inflate(trackRect, 1, 0), Color.Red, Color.Red, 0f);
            spectrum.InterpolationColors = new ColorBlend { Colors = Spectrum, Positions = SpectrumAt };
            using var trackPath = Theme.RoundedRect(trackRect, 4);
            g.FillPath(spectrum, trackPath);
        }
        else
        {
            // white at a low alpha reads on any surface (RowBg was ~9 levels above the card: invisible)
            using (var track = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            using (var trackPath = Theme.RoundedRect(new Rectangle(pad, cy - 3, trackW, 6), 3))
                g.FillPath(track, trackPath);
            if (fillW > 4)
                using (var fill = new SolidBrush(Theme.Accent))
                using (var fillPath = Theme.RoundedRect(new Rectangle(pad, cy - 3, fillW, 6), 3))
                    g.FillPath(fill, fillPath);
        }

        int tx = pad + fillW;
        if (Focused && ShowFocusCues)
            using (var ring = new Pen(Theme.AccentBright, 1.5f))
                g.DrawEllipse(ring, tx - 10.5f, cy - 10.5f, 21f, 21f);
        using (var thumbOuter = new SolidBrush(Theme.TextCol))
            g.FillEllipse(thumbOuter, tx - 8, cy - 8, 16, 16);
        Color inner = RainbowTrack
            ? Theme.HsvToColor(Value * 360.0 / Math.Max(1, Maximum))
            : _dragging ? Theme.Accent : Theme.PanelBg;
        using (var thumbInner = new SolidBrush(inner))
            g.FillEllipse(thumbInner, tx - 5, cy - 5, 10, 10);
    }

    /// <summary>The 1995 trackbar, drawn as Mixtape draws it: a sunken WHITE channel with the full
    /// two-ring edge, a navy fill inset inside it, and a raised 11x16 thumb. The hue slider keeps its
    /// spectrum - the colour is the control's whole point - inside the same inset.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        const int pad = 9;
        int cy = Height / 2;
        int trackW = Width - pad * 2;
        if (trackW <= 0) return;
        double frac = (Value - Minimum) / (double)(Maximum - Minimum);

        var chan = new Rectangle(pad, cy - 3, Math.Max(6, trackW), 6);
        var inside = new Rectangle(chan.X + 2, chan.Y + 2, chan.Width - 4, chan.Height - 4);
        if (!Live)
        {
            // a disabled 95 trackbar: the same sunken channel, a face-grey interior, no thumb
            using (var bed = new SolidBrush(Theme.Face)) g.FillRectangle(bed, chan);
            Theme.Bevel(g, chan, raised: false);
            return;
        }
        using (var wb = new SolidBrush(Color.White)) g.FillRectangle(wb, chan);
        Theme.Bevel(g, chan, raised: false);
        int fw = (int)Math.Round(inside.Width * Math.Clamp(frac, 0, 1));
        if (RainbowTrack)
        {
            using var spectrum = new LinearGradientBrush(Rectangle.Inflate(inside, 1, 0), Color.Red, Color.Red, 0f);
            spectrum.InterpolationColors = new ColorBlend { Colors = Spectrum, Positions = SpectrumAt };
            g.FillRectangle(spectrum, inside);
        }
        else if (fw > 0)
            using (var nb = new SolidBrush(Theme.ClassicNavy)) g.FillRectangle(nb, inside.X, inside.Y, fw, inside.Height);

        int kx = inside.X + fw;
        var thumb = new Rectangle(Math.Clamp(kx - 5, chan.X - 3, chan.Right - 8), cy - 8, 11, 16);
        Theme.ClassicPanel(g, thumb, raised: true);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - 2, Height - 2), Color.Black, Theme.Face);
    }

    private void SetFromMouse(int x)
    {
        const int pad = 9;
        double frac = (x - pad) / (double)(Width - pad * 2);
        Value = Minimum + (int)Math.Round(Math.Clamp(frac, 0, 1) * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Live) return;
        if (CanFocus) Focus();
        _dragging = true;
        SetFromMouse(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && Live) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
    }

    private int Step => Math.Max(1, (Maximum - Minimum) / 100); // 3° on the hue bar, 1 elsewhere

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Live) Value += Math.Sign(e.Delta) * Step;
        if (e is HandledMouseEventArgs h) h.Handled = true; // don't also scroll a parent
    }

    // The keyboard of a native trackbar: arrows by one step, Page keys by ten, Home/End to the ends.
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown
            or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Live)
        {
            int? to = e.KeyCode switch
            {
                Keys.Left or Keys.Down => Value - Step,
                Keys.Right or Keys.Up => Value + Step,
                Keys.PageDown => Value - Step * 10,
                Keys.PageUp => Value + Step * 10,
                Keys.Home => Minimum,
                Keys.End => Maximum,
                _ => null,
            };
            if (to.HasValue) { Value = to.Value; e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _dragging = false; Invalidate(); base.OnLostFocus(e); }
}

// <summary>A settings row with a custom checkbox and label.</summary>
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

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override Cursor Cursor { get => Theme.Pointing(base.Cursor); set => base.Cursor = value; }

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
            CrispText.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(34, 0, Width - 40, Height),
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

        CrispText.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(40, 0, Width - 46, Height),
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

    /// <summary>How the button paints in the modern skin. Legacy uses FillColor / HoverFillColor /
    /// BorderColor as set; the others are Mixtape's looks resolved at PAINT time from the current tokens,
    /// so a skin or accent change can never leave a stale baked colour behind.</summary>
    public enum Look { Legacy, Secondary, Tile, Well, Chip, Swatch }
    public Look Variant { get; set; } = Look.Legacy;
    /// <summary>Tile: the selected effect (accent wash, accent label). Well: the active colour (a double
    /// ring). Chip: a specific colour target is chosen (accent wash).</summary>
    public bool Selected { get; set; }
    /// <summary>Tile: paints the effect art (22 px) at the left of the label.</summary>
    public Action<Graphics, Rectangle>? Art { get; set; }

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.TextCol;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(9.5f);
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override Cursor Cursor { get => Theme.Pointing(base.Cursor); set => base.Cursor = value; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); PaintFocus(g); return; }
        if (Variant == Look.Well) { PaintWell(g); PaintFocus(g); return; }
        if (Variant != Look.Legacy) { PaintVariant(g); PaintFocus(g); return; }
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
            if (BorderSize > 0)   // a 0-width GDI+ pen still strokes a 1 px hairline
            {
                using var pen = new Pen(Enabled ? BorderColor : Theme.Blend(BorderColor, Theme.PanelBg, 0.45), BorderSize);
                g.DrawPath(pen, path);
            }
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
        CrispText.DrawText(g, Text, Font, rect, textCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private const TextFormatFlags CenterText = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                               TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
    private const TextFormatFlags LeftText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                             TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

    /// <summary>Mixtape's button looks, as alpha washes over whatever surface hosts the button (the
    /// control is transparent, so its parent is already painted underneath).</summary>
    private void PaintVariant(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        int h = Interactive && _hover && Enabled ? 1 : 0, p = Interactive && _pressed && Enabled ? 1 : 0;
        Color fill, line = Color.Empty, text;
        Font font;
        switch (Variant)
        {
            case Look.Secondary:
                fill = Color.FromArgb(Enabled ? 22 + 18 * h - 8 * p : 12, 255, 255, 255);
                line = Color.FromArgb(Enabled ? 30 + 20 * h : 18, 255, 255, 255);
                text = Enabled ? Theme.TextCol : Theme.Faint;
                font = Theme.UiFontShared(9.5f, FontStyle.Bold);
                break;
            case Look.Swatch:
                // a colour chip: FillColor itself, a well's faint outline, the label bold like Secondary
                fill = !Enabled ? Theme.Blend(FillColor, Theme.PanelBg, 0.55) : p == 1 ? Blend(FillColor, Color.Black, 0.18) : FillColor;
                line = Color.FromArgb(Enabled && h == 1 ? 80 : 22, 255, 255, 255);
                text = Enabled ? Theme.OnColor(FillColor) : Theme.Faint;
                font = Theme.UiFontShared(9.5f, FontStyle.Bold);
                break;
            case Look.Chip:
                fill = Selected ? Color.FromArgb(46 + 18 * h, Theme.Accent) : Color.FromArgb(255 * h, Theme.RowHover);
                text = !Enabled ? Theme.Faint : Selected ? Theme.AccentBright : h == 1 ? Theme.TextCol : Theme.Subtle;
                font = Theme.UiFontShared(8.75f);
                break;
            default: // Tile
                if (Selected)
                {
                    fill = Color.FromArgb(46, Theme.Accent);
                    line = Color.FromArgb(115 + 30 * h, Theme.Accent);
                    text = Theme.AccentBright;
                    font = Theme.UiFontShared(10f, FontStyle.Bold);
                }
                else
                {
                    fill = Color.FromArgb(12 + 12 * h - 4 * p, 255, 255, 255);
                    if (h == 1) line = Color.FromArgb(30, 255, 255, 255);
                    text = Enabled ? Theme.TextCol : Theme.Faint;
                    font = Theme.UiFontShared(10f);
                }
                break;
        }
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), CornerRadius))
        {
            if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (!line.IsEmpty && line.A > 0) using (var pen = new Pen(line)) g.DrawPath(pen, path);
        }
        if (Variant == Look.Tile && Art != null)
        {
            Art(g, new Rectangle(9, (Height - 22) / 2, 22, 22));
            CrispText.DrawText(g, Text, font, new Rectangle(41, 0, Math.Max(0, Width - 49), Height), text, LeftText);
        }
        else if (!string.IsNullOrEmpty(Text))
            CrispText.DrawText(g, Text, font, new Rectangle(0, 0, Width, Height), text, CenterText);
    }

    /// <summary>A colour well: the colour itself, a faint cover-style outline, and for the active colour
    /// a 2 px ring in the text colour with a 2 px ring of the surface inside it (so white and cream
    /// swatches still show their selection).</summary>
    private void PaintWell(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Color fill = Enabled ? FillColor : Theme.Blend(FillColor, Theme.PanelBg, 0.55);
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), CornerRadius))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(_hover && Enabled ? 80 : 22, 255, 255, 255));
            g.DrawPath(pen, path);
        }
        if (!Selected) return;
        using (var outer = new Pen(Enabled ? Theme.TextCol : Theme.Faint, 2f))
        using (var po = Theme.RoundedRect(new RectangleF(1f, 1f, Width - 2f, Height - 2f), Math.Max(0, CornerRadius - 1)))
            g.DrawPath(outer, po);
        using (var inner = new Pen(Theme.PanelBg, 2f))
        using (var pi = Theme.RoundedRect(new RectangleF(3f, 3f, Width - 6f, Height - 6f), Math.Max(0, CornerRadius - 3)))
            g.DrawPath(inner, pi);
    }

    private void PaintFocus(Graphics g)
    {
        if (!Focused || !ShowFocusCues) return;
        if (Theme.Classic) { ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4)); return; }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.AccentBright, 1.5f);
        using var path = Theme.RoundedRect(new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), CornerRadius);
        g.DrawPath(pen, path);
    }

    // keyboard, like a native button: Enter clicks once on key-down, Space presses and clicks on
    // key-up, and auto-repeat never fires it again (a held key must not re-apply an effect 30x a second)
    private bool _keyHeld;
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            if (!_keyHeld)
            {
                _keyHeld = true;
                if (e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
                else { _pressed = true; Invalidate(); }
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _keyHeld && _pressed) { _pressed = false; Invalidate(); OnClick(EventArgs.Empty); }
        if (e.KeyCode is Keys.Space or Keys.Enter) _keyHeld = false;
        base.OnKeyUp(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyHeld = false; _pressed = false; Invalidate(); base.OnLostFocus(e); }

    /// <summary>The Windows 95 button: a raised face that goes sunken while held, its label
    /// stepping down-right with it. A colour well keeps its own colour and wears a sunken edge
    /// instead; the active one is marked the way the 95 Color dialog did it - a black frame, a line
    /// of face, then the well. A latched button - the selected effect - stays pressed over the 50%
    /// dither a checked toolbar button wore.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Theme.TextHint;
        var full = new Rectangle(0, 0, Width, Height);
        bool isWell = Variant == Look.Well || (string.IsNullOrEmpty(Text) && HoverFillColor.ToArgb() == FillColor.ToArgb());

        if (isWell)
        {
            var well = full;
            if (Selected)
            {
                using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, full);
                using (var black = new Pen(Color.Black)) g.DrawRectangle(black, 0, 0, Width - 1, Height - 1);
                well = Rectangle.Inflate(full, -2, -2);
            }
            using (var b = new SolidBrush(Enabled ? FillColor : Theme.Face)) g.FillRectangle(b, well);
            Theme.Bevel(g, well, raised: false);
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
        if (Variant == Look.Swatch)
        {
            // a 1995 colour button (Display Properties' "Color:"): the colour itself in a small
            // black-framed square on the face, then the label beside it
            var sq = new Rectangle(tr.X + 8, tr.Y + (Height - 12) / 2, 16, 12);
            using (var cb = new SolidBrush(Enabled ? FillColor : Theme.Face)) g.FillRectangle(cb, sq);
            using (var frame = new Pen(Enabled ? Color.Black : Theme.FaceShadow)) g.DrawRectangle(frame, sq.X, sq.Y, sq.Width - 1, sq.Height - 1);
            tr = new Rectangle(sq.Right + 2, tr.Y, Math.Max(0, tr.Right - sq.Right - 6), tr.Height);
        }
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (!Enabled)
        {
            // 1995 embossed a disabled label: a white ghost down-right, grey on top.
            var sh = tr; sh.Offset(1, 1);
            CrispText.DrawText(g, Text, Font, sh, Theme.FaceHi, flags);
            CrispText.DrawText(g, Text, Font, tr, Theme.FaceShadow, flags);
            return;
        }
        CrispText.DrawText(g, Text, Font, tr, Color.Black, flags);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    // keeps the source alpha: a Transparent fill must stay transparent when pressed or disabled
    private static Color Blend(Color a, Color b, double f) => Color.FromArgb(a.A,
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
            CrispText.DrawText(g, Word, Theme.UiFontShared(9f, FontStyle.Bold),
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
/// <summary>The pieces both device rows share (the single row and the group header), so the two always
/// line up: the enable circle, the name / sub-line pair, and the Classic kind icon.</summary>
internal static class DeviceRowArt
{
    public const int NameX = 40, TileSize = 30;
    /// <summary>Classic: the name column right of the check box and the kind icon / [+] box.</summary>
    public const int ClassicNameX = 46;

    /// <summary>The keyboard-focus cue of a rail row: an AccentBright ring round the row's pill, or the
    /// dotted rectangle of 1995.</summary>
    public static void PaintFocus(Graphics g, Control row)
    {
        if (!row.Focused || !ShowsFocusCues(row)) return;
        if (Theme.Classic)
        {
            ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, row.Width - 2, row.Height - 2));
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.AccentBright, 1.5f);
        using var path = Theme.RoundedRect(new RectangleF(0.75f, 0.75f, row.Width - 1.5f, row.Height - 1.5f), 8f);
        g.DrawPath(pen, path);
    }

    // Control.ShowFocusCues is protected; a row asks through this so the rule stays in one place
    private static bool ShowsFocusCues(Control c) => c is IFocusCues f && f.ShowCues;

    /// <summary>The art tile of a modern row (right edge, vertically centred).</summary>
    public static Rectangle TileRect(Control row) =>
        new(row.Width - 12 - TileSize, (row.Height - TileSize) / 2, TileSize, TileSize);

    /// <summary>The enable control: a CIRCLE (deliberately not a square) so it never reads as a second
    /// colour swatch beside the square art tile - with a green accent and green lighting the two used to
    /// look like duplicate controls. 0 = off, 1 = on, 2 = some of a group on.</summary>
    public static void EnableCircle(Graphics g, Rectangle box, int state, Color surface)
    {
        if (state == 1)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillEllipse(fill, box);
            using var pen = new Pen(Theme.OnAccent, 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLines(pen, new[] { new Point(box.X + 5, box.Y + 9), new Point(box.X + 8, box.Y + 12), new Point(box.X + 13, box.Y + 6) });
        }
        else if (state == 2)
        {
            using var fill = new SolidBrush(Theme.Blend(Theme.Accent, surface, 0.5));
            g.FillEllipse(fill, box);
            using var pen = new Pen(Theme.OnAccent, 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLine(pen, box.X + 5, box.Y + 9, box.X + 13, box.Y + 9);
        }
        else
        {
            using var pen = new Pen(Theme.Border, 1.6f);
            g.DrawEllipse(pen, box);
        }
    }

    public static string Zones(int n) => n == 1 ? "1 zone" : $"{n} zones";

    /// <summary>Name (10 semibold) over a sub-line (8.75), with an optional per-device effect note
    /// appended in the accent - the only sign on a row that the device runs its own effect.</summary>
    public static void Lines(Graphics g, int x, int w, int h, string name, bool on, string sub, string? note, bool sel)
    {
        if (w <= 8) return;
        const TextFormatFlags F = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Top | TextFormatFlags.NoPadding;
        bool c = Theme.Classic;
        var nameFont = Theme.UiFontShared(c ? 9f : 10f, FontStyle.Bold);
        var subFont = Theme.UiFontShared(c ? 9f : 8.75f);
        int y1 = h / 2 - (c ? 15 : 16), y2 = h / 2 + (c ? 2 : 2);
        Color nameCol = sel ? Color.White : on ? Theme.TextCol : Theme.Subtle;
        Color subCol = sel ? Color.White : Theme.Subtle;
        CrispText.DrawText(g, name, nameFont, new Rectangle(x, y1, w, 18), nameCol, F);
        if (note == null)
        {
            CrispText.DrawText(g, sub, subFont, new Rectangle(x, y2, w, 16), subCol, F);
            return;
        }
        int sw = Math.Min(w, TextRenderer.MeasureText(g, sub, subFont, Size.Empty, TextFormatFlags.NoPadding).Width);
        CrispText.DrawText(g, sub, subFont, new Rectangle(x, y2, sw, 16), subCol, F);
        CrispText.DrawText(g, "  ·  ✦ " + note, subFont, new Rectangle(x + sw, y2, Math.Max(0, w - sw), 16),
            sel ? Color.White : Theme.AccentBright, F);
    }

    public static ClassicIcons.Id KindIcon(string kind) => kind switch
    {
        "GPU" => ClassicIcons.Id.Gpu,
        "RAM" => ClassicIcons.Id.Ram,
        "MB" => ClassicIcons.Id.Board,
        "USB" => ClassicIcons.Id.Usb,
        "MOUSE" => ClassicIcons.Id.Mouse,
        _ => ClassicIcons.Id.Devices,
    };

    /// <summary>Classic: the colour chip of a device-manager row - a sunken well holding the art.</summary>
    public static void ClassicChip(Graphics g, Rectangle chip, MainForm.Effect fx, Color c, Color end, bool on, Color live = default)
    {
        Theme.ClassicField(g, chip, Theme.Face);
        EffectArt.Paint(g, Rectangle.Inflate(chip, -2, -2), fx, c, end, 0, live, on ? 0 : 0.6, Theme.Face);
    }
}

/// <summary>Lets a shared painter ask a control whether it should show keyboard focus cues.</summary>
internal interface IFocusCues { bool ShowCues { get; } }

public class DeviceRow : Control, IFocusCues
{
    private bool _hover;
    bool IFocusCues.ShowCues => ShowFocusCues;

    /// <summary>The sampled screen colour, for a device running Screen sync (Empty otherwise).</summary>
    public Color LiveColor { get; private set; }
    /// <summary>Raised by Space (toggle), and by the menu key / Shift+F10 (open the device menu).</summary>
    public event EventHandler? MenuKeyRequested;

    /// <summary>Sets the sampled colour; repaints only the tile, only when it shows it.</summary>
    public void SetLive(Color c)
    {
        if (c.ToArgb() == LiveColor.ToArgb()) return;
        LiveColor = c;
        if (ArtEffect == MainForm.Effect.Ambient) Invalidate(Theme.Classic ? ClassicChipRect : DeviceRowArt.TileRect(this));
    }

    internal Rectangle ClassicChipRect => new(Width - 6 - 22, Height / 2 - 8, 22, 16);

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
    /// <summary>The effect this device actually runs (its own override or the global one) and the
    /// gradient end colour - what its art tile shows. Set by the host.</summary>
    internal MainForm.Effect ArtEffect { get; set; } = MainForm.Effect.Static;
    public Color ArtEnd { get; set; } = Color.White;

    public DeviceRow(IRgbDevice device, bool initialChecked = true)
    {
        Device = device;
        Checked = initialChecked;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(245, 48);
        Margin = new Padding(0, 0, 0, 4);
        AccessibleRole = AccessibleRole.CheckButton;
        // cursor is set per-region in OnMouseMove (hand over the checkbox, arrow elsewhere)
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new RowAccessible(this);
    private sealed class RowAccessible : ControlAccessibleObject
    {
        private readonly DeviceRow _row;
        public RowAccessible(DeviceRow row) : base(row) { _row = row; }
        public override string? Name => _row.Device.Name;
        public override AccessibleStates State => base.State | (_row.Checked ? AccessibleStates.Checked : 0);
    }

    // keyboard: Space toggles the device (key-up, never on auto-repeat), the menu key opens its menu
    private bool _keyHeld;
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { _keyHeld = true; e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _keyHeld)
        {
            _keyHeld = false;
            Checked = !Checked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }
    protected override void WndProc(ref Message m)
    {
        // WM_CONTEXTMENU from the keyboard (menu key, Shift+F10) carries lParam -1; the mouse's
        // right-click is the owner's MouseUp handler
        if (m.Msg == 0x007B && m.LParam == (IntPtr)(-1)) { MenuKeyRequested?.Invoke(this, EventArgs.Empty); return; }
        base.WndProc(ref m);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyHeld = false; Invalidate(); base.OnLostFocus(e); }

    /// <summary>A rail row in Mixtape's style: no plate at rest, a soft pill under the pointer, an
    /// accent ring while it is the colour target. The enable circle stays at the LEFT (same hit column
    /// as always); the device's art tile - its real effect and colour - sits on the right, and the kind
    /// moves into the sub-line, which frees the old badge column for longer names.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic) { PaintClassic(g); return; }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = Theme.TextHint;
        Color rail = Theme.RailBg;
        g.Clear(rail);
        using (var pill = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8f))
        {
            if (_hover) using (var hb = new SolidBrush(Theme.Blend(rail, Color.White, 0.06))) g.FillPath(hb, pill);
            if (Targeted) using (var ring = new Pen(Theme.Accent, 1.5f)) g.DrawPath(ring, pill);
        }
        DeviceRowArt.EnableCircle(g, new Rectangle(12, Height / 2 - 9, 18, 18), Checked ? 1 : 0, rail);
        var tile = DeviceRowArt.TileRect(this);
        EffectArt.Paint(g, tile, ArtEffect, DisplayColor, ArtEnd, 6, LiveColor, Checked ? 0 : 0.6, rail);
        DeviceRowArt.Lines(g, DeviceRowArt.NameX, tile.Left - 10 - DeviceRowArt.NameX, Height, Device.Name, Checked,
            DeviceRowArt.Zones(Device.LedCount) + "  ·  " + Device.Kind, EffectNote, false);
        DeviceRowArt.PaintFocus(g, this);
    }

    /// <summary>Classic: a row of a 1995 device-manager list - white, a check box, the kind icon, the
    /// name and its details, a sunken colour chip; the colour target is the navy selection band.</summary>
    private void PaintClassic(Graphics g)
    {
        bool sel = Targeted;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Theme.TextHint;
        g.Clear(sel ? Theme.ClassicNavy : Color.White);
        Theme.ClassicCheck(g, new Rectangle(5, Height / 2 - 6, 13, 13), Checked);
        ClassicIcons.Draw(g, DeviceRowArt.KindIcon(Device.Kind), 24, Height / 2 - 8);
        var chip = ClassicChipRect;
        DeviceRowArt.ClassicChip(g, chip, ArtEffect, DisplayColor, ArtEnd, Checked, LiveColor);
        DeviceRowArt.Lines(g, DeviceRowArt.ClassicNameX, chip.Left - 6 - DeviceRowArt.ClassicNameX, Height, Device.Name, Checked,
            DeviceRowArt.Zones(Device.LedCount) + "  ·  " + Device.Kind, EffectNote, sel);
        DeviceRowArt.PaintFocus(g, this);
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
    private bool CheckboxHit(Point p) => p.X >= 0 && p.X < (Theme.Classic ? 22 : 42); // the whole left column

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // hand cursor only over the checkbox column, so it's clear where a click toggles (arrow in Classic)
        Cursor = CheckboxHit(e.Location) ? Theme.Pointing(Cursors.Hand) : Cursors.Default;
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
public class DeviceGroupRow : Control, IFocusCues
{
    private bool _hover;
    bool IFocusCues.ShowCues => ShowFocusCues;
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
        Size = new Size(245, 48);
        Margin = new Padding(0, 0, 0, 4);
        AccessibleRole = AccessibleRole.OutlineButton;
        AccessibleName = members.Count > 0 ? $"{members[0].Device.Name} ×{members.Count}" : "Device group";
    }

    /// <summary>Repaints just the art (the lead's sampled Screen-sync colour moved).</summary>
    public void InvalidateTile() =>
        Invalidate(Theme.Classic ? new Rectangle(Width - 6 - 22, Height / 2 - 8, 22, 16) : DeviceRowArt.TileRect(this));

    // keyboard: Space toggles every member, Left/Right (or Enter) collapse and expand
    private bool _keyHeld;
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter or Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space: _keyHeld = true; e.Handled = true; break;
            case Keys.Enter when !_keyHeld: _keyHeld = true; SetExpanded(!Expanded); e.Handled = true; break;
            case Keys.Right when !Expanded: SetExpanded(true); e.Handled = true; break;
            case Keys.Left when Expanded: SetExpanded(false); e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _keyHeld) ToggleAllRequested?.Invoke(this, EventArgs.Empty);
        if (e.KeyCode is Keys.Space or Keys.Enter) _keyHeld = false;
        base.OnKeyUp(e);
    }
    private void SetExpanded(bool v)
    {
        if (Expanded == v) return;
        Expanded = v;
        Invalidate();
        ExpandToggled?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyHeld = false; Invalidate(); base.OnLostFocus(e); }

    private bool AllChecked => Members.All(m => m.Checked);
    private bool AnyChecked => Members.Any(m => m.Checked);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var lead = Members[0];
        int zones = Members.Sum(m => m.Device.LedCount);
        string sub = $"×{Members.Count}  ·  {zones} zones  ·  {lead.Device.Kind}";
        int state = AllChecked ? 1 : AnyChecked ? 2 : 0;
        if (Theme.Classic)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = Theme.TextHint;
            g.Clear(Color.White);
            var cb = new Rectangle(5, Height / 2 - 6, 13, 13);
            if (state == 2) { Theme.ClassicField(g, cb, Theme.Face); Theme.ClassicCheck(g, cb, true, enabled: false); }
            else Theme.ClassicCheck(g, cb, state == 1);
            // the [+] / [-] box of a 95 tree view
            var tb = new Rectangle(24, Height / 2 - 4, 9, 9);
            g.FillRectangle(Brushes.White, tb);
            using (var frame = new Pen(Theme.FaceShadow)) g.DrawRectangle(frame, tb.X, tb.Y, tb.Width - 1, tb.Height - 1);
            g.DrawLine(Pens.Black, tb.X + 2, tb.Y + 4, tb.X + 6, tb.Y + 4);
            if (!Expanded) g.DrawLine(Pens.Black, tb.X + 4, tb.Y + 2, tb.X + 4, tb.Y + 6);
            var chip = new Rectangle(Width - 6 - 22, Height / 2 - 8, 22, 16);
            DeviceRowArt.ClassicChip(g, chip, lead.ArtEffect, lead.DisplayColor, lead.ArtEnd, AnyChecked, lead.LiveColor);
            DeviceRowArt.Lines(g, DeviceRowArt.ClassicNameX, chip.Left - 6 - DeviceRowArt.ClassicNameX, Height,
                lead.Device.Name, AnyChecked, sub, null, false);
            DeviceRowArt.PaintFocus(g, this);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = Theme.TextHint;
        Color rail = Theme.RailBg;
        g.Clear(rail);
        if (_hover)
            using (var pill = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8f))
            using (var hb = new SolidBrush(Theme.Blend(rail, Color.White, 0.06)))
                g.FillPath(hb, pill);
        DeviceRowArt.EnableCircle(g, new Rectangle(12, Height / 2 - 9, 18, 18), state, rail);
        var tile = DeviceRowArt.TileRect(this);
        EffectArt.Paint(g, tile, lead.ArtEffect, lead.DisplayColor, lead.ArtEnd, 6, lead.LiveColor, AnyChecked ? 0 : 0.6, rail);

        // chevron just left of the tile (▾ expanded / ▸ collapsed)
        float cx = tile.Left - 13, cy = Height / 2f;
        using (var cpen = new Pen(Theme.Subtle, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
        {
            if (Expanded) g.DrawLines(cpen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            else g.DrawLines(cpen, new[] { new PointF(cx - 2, cy - 4), new PointF(cx + 2, cy), new PointF(cx - 2, cy + 4) });
        }
        DeviceRowArt.Lines(g, DeviceRowArt.NameX, tile.Left - 24 - DeviceRowArt.NameX, Height, lead.Device.Name, AnyChecked, sub, null, false);
        DeviceRowArt.PaintFocus(g, this);
    }

    private bool CircleHit(Point p) => p.X >= 0 && p.X < (Theme.Classic ? 22 : 42);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (CircleHit(e.Location)) ToggleAllRequested?.Invoke(this, EventArgs.Empty);
            else { Expanded = !Expanded; Invalidate(); ExpandToggled?.Invoke(this, EventArgs.Empty); }
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) { Cursor = Theme.Pointing(Cursors.Hand); base.OnMouseMove(e); }
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
        CrispText.DrawText(g, Text, Theme.UiFontShared(9.5f), new Rectangle(TextInset, 0, Width - TextInset - 6, Height),
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

    /// <summary>Glow strength 0-100: scales the two accent glows and the accent band (0 = the plain
    /// dark gradient).</summary>
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
    private const int RW = 480;           // low-res render width; the height follows the window's aspect
    private static Bitmap? _scaled;       // _bmp upscaled to the current client size
    private static Size _scaledSize;      // the client size _scaled was built for
    private static int _rh;               // the low-res height _bmp was rendered at

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
        var form = c.FindForm();
        if (Theme.Classic)
        {
            // One grey window face, framed by the raised 95 window edge. Every panel that reaches a
            // window edge paints its own piece of that one frame, in form coordinates - so the frame
            // stays whole however the panels are cut. A maximised 95 window had no frame.
            g.Clear(Theme.Face);
            if (form != null && form.WindowState != FormWindowState.Maximized)
            {
                var o = form.PointToClient(c.PointToScreen(Point.Empty));
                Theme.Bevel(g, new Rectangle(-o.X, -o.Y, form.ClientSize.Width, form.ClientSize.Height), raised: true);
            }
            return;
        }
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
        // Render at the window's own aspect, so the glows stay round instead of being stretched
        // into ellipses by a fixed-aspect source. Still a few ms: it only happens per size change.
        int rh = Math.Clamp((int)Math.Round(RW * size.Height / (double)size.Width), 60, 960);
        if (_bmp == null || _rh != rh) { _bmp?.Dispose(); _bmp = Render(RW, rh); _rh = rh; }
        _scaled?.Dispose();
        _scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(_scaled);
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY); // no transparent edge bleed at the borders
        g.DrawImage(_bmp, new Rectangle(0, 0, size.Width, size.Height),
            0, 0, RW, rh, GraphicsUnit.Pixel, attrs);
        _scaledSize = size;
    }

    /// <summary>
    /// Mixtape's wallpaper, so the two apps share one look: a near-black 60° gradient with a faint
    /// accent-tinted band through the middle, an accent glow off the top-left corner and a second,
    /// hue-shifted glow toward the bottom-right. The rail and content cards float on it. Every colour
    /// derives from the rail tone and the accent, so it re-themes with the accent setting.
    /// </summary>
    private static Bitmap Render(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb); // opaque: cheaper blits
        using var g = Graphics.FromImage(bmp);
        var r = new Rectangle(0, 0, w, h);
        double k = Math.Clamp(Intensity, 0, 100) / 100.0;
        Color rail = Theme.RailBg;
        Color a0 = Theme.Blend(rail, Color.Black, 0.38);                                   // deep base, top
        Color a1 = Theme.Blend(rail, Color.Black, 0.42);
        Color mid = Theme.Blend(Theme.Blend(rail, Color.Black, 0.30), Theme.Accent, 0.12 * k); // accent band
        Color a3 = Theme.Blend(rail, Color.Black, 0.52);                                   // darkest, bottom
        using (var br = new LinearGradientBrush(r, a0, a3, 60f))
        {
            br.InterpolationColors = new ColorBlend
            {
                Colors = new[] { a0, a1, mid, a3 },
                Positions = new[] { 0f, 0.30f, 0.62f, 1f },
            };
            g.FillRectangle(br, r);
        }
        if (k <= 0) return bmp;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float m = Math.Max(w, h);
        Glow(g, -w * 0.18f, -h * 0.30f, m * 0.95f, Color.FromArgb((int)(55 * k), Theme.Accent));
        Glow(g, w - w * 0.35f, h - h * 0.10f, m * 0.85f,
            Color.FromArgb((int)(40 * k), Theme.HsvToColor((Theme.Accent.GetHue() + 55) % 360, 0.55, 0.72)));
        return bmp;
    }

    /// <summary>A soft radial glow fading to nothing at its rim; (x, y) is the top-left of its circle.</summary>
    private static void Glow(Graphics g, float x, float y, float d, Color c)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(x, y, d, d);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = c,
            SurroundColors = new[] { Color.FromArgb(0, c) },
            CenterPoint = new PointF(x + d / 2, y + d / 2),
        };
        g.FillPath(brush, path);
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

    /// <summary>A SECTION of one of the two floating cards rather than a card of its own: Modern paints
    /// only the card's flat surface and a muted caps caption (one surface, no box-in-a-box); Classic
    /// paints the 1995 group box with a Title Case caption breaking its etched frame.</summary>
    public bool Section { get; set; }
    /// <summary>Section on the left rail (RailBg) instead of the content card (PanelBg).</summary>
    public bool OnRail { get; set; }
    /// <summary>A quiet figure right-aligned on the caption band (the device count).</summary>
    public string? TitleNote { get; set; }
    /// <summary>Modern paints no caption (the content starts at the top); Classic still captions the
    /// group box. The LIVE stage: a picture needs no label, a 95 group box does.</summary>
    public bool CaptionInClassicOnly { get; set; }
    /// <summary>Content inset inside a section, both sides - room for the Classic group-box frame.</summary>
    public const int SectionPad = 10;
    /// <summary>Where a section's content starts (under the caption band).</summary>
    public const int SectionTop = 28;
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
        if (Section) { PaintSection(g); return; }
        if (Theme.Classic) { PaintClassic(g); return; }
        Backdrop.Paint(g, this);                         // corner pixels = the window gradient
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        var fillColor = Translucent ? Color.FromArgb(206, Theme.PanelBg) : Theme.PanelBg;
        using (var fill = new SolidBrush(fillColor)) g.FillPath(fill, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
        if (!string.IsNullOrEmpty(Title))
            CrispText.DrawText(g, Title, Theme.UiFontShared(9f, FontStyle.Bold), new Point(14, 12), Theme.Accent,
                TextFormatFlags.NoPrefix); // Accent read per paint, so SetAccent re-themes it
    }

    private void PaintSection(Graphics g)
    {
        if (Theme.Classic)
        {
            // the 1995 group box: an etched frame (shadow ring, highlight ring one pixel in) with a
            // Title Case caption sitting in a gap cut in the top line
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = Theme.TextHint;
            using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, ClientRectangle);
            var f = Theme.UiFontShared(9f);
            int top = f.Height / 2;
            using (var dark = new Pen(Theme.FaceShadow)) g.DrawRectangle(dark, 0, top, Width - 2, Height - top - 2);
            using (var light = new Pen(Theme.FaceHi)) g.DrawRectangle(light, 1, top + 1, Width - 2, Height - top - 2);
            if (string.IsNullOrEmpty(Title)) return;
            string cap = Title.Length > 1 ? char.ToUpperInvariant(Title[0]) + Title.Substring(1).ToLowerInvariant() : Title;
            var sz = TextRenderer.MeasureText(g, cap, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, 6, 0, sz.Width + 6, f.Height);
            CrispText.DrawText(g, cap, f, new Point(9, 0), Color.Black, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            return;
        }
        using (var b = new SolidBrush(OnRail ? Theme.RailBg : Theme.PanelBg)) g.FillRectangle(b, ClientRectangle);
        if (string.IsNullOrEmpty(Title) || CaptionInClassicOnly) return;
        const TextFormatFlags F = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var lf = Theme.UiFontShared(8f, FontStyle.Bold);
        CrispText.DrawText(g, Title.ToUpperInvariant(), lf, new Rectangle(SectionPad, 0, Width - 2 * SectionPad, 22), Theme.Faint, F);
        if (!string.IsNullOrEmpty(TitleNote))
            CrispText.DrawText(g, TitleNote, Theme.UiFontShared(8.75f), new Rectangle(SectionPad, 0, Width - 2 * SectionPad, 22),
                Theme.Faint, F | TextFormatFlags.Right);
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
        CrispText.DrawText(g, Title, font, new Point(13, 0), Color.Black,
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
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;   // a picture, not a control: Tab must not land on it and seem to vanish
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

    /// <summary>The lights are off: the stage shows a dim placeholder instead of black.</summary>
    public bool Off { get; set; }

    /// <summary>
    /// Modern: the live stage - the frame's colours blended into one soft strip (no hard segment edges),
    /// a gentle shade toward the bottom and a faint rim, the one big colourful object of the window.
    /// Classic: a sunken black bed with the LEDs as hard cells, like a hardware readout.
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int n = _colors.Length;
        if (Theme.Classic)
        {
            g.SmoothingMode = SmoothingMode.None;
            var bed = new Rectangle(0, 0, Width, Height);
            Theme.Bevel(g, bed, raised: false);
            var inner = Rectangle.Inflate(bed, -2, -2);
            g.FillRectangle(Brushes.Black, inner);
            if (n == 0 || inner.Width < n) return;
            using var cell = new SolidBrush(Color.Black);
            for (int i = 0; i < n; i++)
            {
                int x0 = inner.X + 1 + (inner.Width - 1) * i / n, x1 = inner.X + 1 + (inner.Width - 1) * (i + 1) / n;
                cell.Color = Off ? Color.FromArgb(40, 40, 40) : Unpack(_colors[i]);
                g.FillRectangle(cell, x0, inner.Y + 1, Math.Max(1, x1 - x0 - 1), inner.Height - 2);
            }
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rect = new RectangleF(0, 0, Width, Height);
        using var clip = Theme.RoundedRect(rect, 10f);
        if (n < 2 || Off)
        {
            using var dim = new SolidBrush(Theme.Blend(Theme.PanelBg, Theme.Faint, 0.35));
            g.FillPath(dim, clip);
        }
        else
        {
            using var strip = new LinearGradientBrush(new RectangleF(-1, 0, Width + 2, Height), Color.Black, Color.Black, 0f);
            var cols = new Color[n];
            var pos = new float[n];
            for (int i = 0; i < n; i++) { cols[i] = Unpack(_colors[i]); pos[i] = i / (float)(n - 1); }
            strip.InterpolationColors = new ColorBlend { Colors = cols, Positions = pos };
            g.FillPath(strip, clip);
            using var shade = new LinearGradientBrush(new RectangleF(0, -1, Width, Height + 2),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(60, 0, 0, 0), 90f);
            g.FillPath(shade, clip);
        }
        using var rim = new Pen(Color.FromArgb(28, 255, 255, 255));
        using var rp = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10f);
        g.DrawPath(rim, rp);
    }

    private static Color Unpack(uint c) => Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
}
