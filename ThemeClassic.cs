using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RGBCommander;

/// <summary>
/// The Windows 95 skin (Settings ▸ Appearance ▸ Look).
///
/// This is a SKIN, not a second colour scheme: <see cref="SetSkin"/> swaps the whole token layer at
/// once — surfaces, text, accent, corner radii, typeface — so the ~330 colour reads and the rounded-rect
/// call sites across the app need no branch of their own. Only the places that draw a SHAPE the period
/// did differently (a raised button, a sunken field, a checkbox, a title bar) ask <see cref="Classic"/>
/// and take another path.
/// </summary>
public static partial class Theme
{
    /// <summary>True while the Windows 95 look is on.</summary>
    public static bool Classic { get; private set; }

    /// <summary>Bumped by every skin/accent change. Anything that BAKES a theme colour into a built
    /// object (a gradient brush, a cached pen) stores the revision it was built at and rebuilds when it
    /// differs — so caches never go stale, yet cost nothing on the steady-state paint loop.</summary>
    public static int Revision { get; private set; }

    private static Color _accentSpec = Color.FromArgb(0, 200, 170);

    /// <summary>Turns the Windows 95 look on or off. Re-applies the stored accent, so switching back
    /// restores the modern palette exactly as it was. Callers repaint afterwards (see RestyleEverything).</summary>
    public static void SetSkin(bool classic)
    {
        Classic = classic;
        SetRadii(classic);
        if (classic) ApplyClassicSurfaces(); else ApplyModernSurfaces();
        SetAccent(_accentSpec);   // bumps Revision
    }

    // ---- the Windows 95 system colours, under the names the era's docs used ----------------------
    public static readonly Color Face = Color.FromArgb(192, 192, 192);        // COLOR_3DFACE / BTNFACE
    public static readonly Color FaceLight = Color.FromArgb(223, 223, 223);   // COLOR_3DLIGHT
    public static readonly Color FaceHi = Color.White;                        // COLOR_3DHIGHLIGHT
    public static readonly Color FaceShadow = Color.FromArgb(128, 128, 128);  // COLOR_3DSHADOW
    public static readonly Color FaceDark = Color.Black;                      // COLOR_3DDKSHADOW
    public static readonly Color ClassicNavy = Color.FromArgb(0, 0, 128);     // COLOR_HIGHLIGHT / active caption
    public static readonly Color ClassicNavyEnd = Color.FromArgb(16, 132, 208); // the caption gradient's far end
    public static readonly Color ClassicInfo = Color.FromArgb(255, 255, 225); // COLOR_INFOBK (tooltip yellow)
    public static readonly Color ClassicDesk = Color.FromArgb(0, 128, 128);   // COLOR_BACKGROUND (the teal desktop)

    /// <summary>The surface of an inset LIST or field, which 1995 drew white inside a grey window
    /// (COLOR_WINDOW). In the modern look it is simply the row surface.</summary>
    public static Color FieldBg => Classic ? Color.White : RowBg;

    private static void ApplyClassicSurfaces()
    {
        Bg = Face;        // the window face: pages, cards and dialogs all sit on it
        PanelBg = Face;
        RowBg = Color.White;                      // a list/field is white inside the grey window
        RowHover = Color.FromArgb(230, 230, 230); // 95 lists had no hover; the faintest grey keeps the affordance
        TextCol = Color.Black;                    // COLOR_WINDOWTEXT / BTNTEXT
        Subtle = Color.FromArgb(56, 56, 56);
        Border = FaceShadow;
        ErrorCol = Color.FromArgb(128, 0, 0);     // maroon, the era's red
    }

    private static void ApplyModernSurfaces()
    {
        Bg = Color.FromArgb(16, 17, 20);
        PanelBg = Color.FromArgb(24, 26, 31);
        RowBg = Color.FromArgb(33, 36, 43);
        RowHover = Color.FromArgb(43, 47, 56);
        TextCol = Color.FromArgb(232, 234, 238);
        Subtle = Color.FromArgb(138, 144, 155);
        Border = Color.FromArgb(52, 56, 66);
        ErrorCol = Color.FromArgb(255, 120, 110);
    }

    // ---- corner radii as tokens: 1995 had none -----------------------------------------------------
    public static int RadShell { get; private set; } = 18;    // the window shell
    public static int RadCard { get; private set; } = 8;      // a card / panel
    public static int RadControl { get; private set; } = 8;   // a button
    public static int RadChip { get; private set; } = 6;      // a badge, a swatch
    public static int RadSmall { get; private set; } = 4;     // the smallest insets

    private static void SetRadii(bool square)
    {
        RadShell = square ? 0 : 18;
        RadCard = square ? 0 : 8;
        RadControl = square ? 0 : 8;
        RadChip = square ? 0 : 6;
        RadSmall = square ? 0 : 4;
    }

    /// <summary>A fully-rounded "pill" of the given height — square in Classic.</summary>
    public static int RadPill(int height) => Classic ? 0 : Math.Max(0, height - 1);

    // ---- typography --------------------------------------------------------------------------------
    // 1995's UI typeface: MS Sans Serif 8pt. It is a BITMAP face (.FON) that GDI+ does not enumerate,
    // so it is named directly — the GDI font mapper resolves it, and where it is missing it falls
    // through to its TrueType twin, Microsoft Sans Serif. A bitmap face draws unsmoothed through
    // TextRenderer, which is exactly the look; no hinting setting can fake it.
    private const string ClassicFamily = "MS Sans Serif";

    /// <summary>1995 had ONE text size. The app's size ramp collapses onto the era's two: 8pt for
    /// everything, and 12pt for the single big heading a window is allowed.</summary>
    private static float ClassicSize(float size) => size >= 13f ? 12f : 8f;

    /// <summary>How text is smoothed. Classic asks for the aliased path; the bitmap face carries the
    /// rest on its own. Paint sites read this instead of naming ClearType directly.</summary>
    public static TextRenderingHint TextHint =>
        Classic ? TextRenderingHint.SingleBitPerPixelGridFit : TextRenderingHint.ClearTypeGridFit;

    /// <summary>Classic draws pixels, not curves: no anti-aliasing anywhere in the chrome.</summary>
    public static SmoothingMode ShapeMode => Classic ? SmoothingMode.None : SmoothingMode.AntiAlias;

    // ---- the 3D edge of 1995 -----------------------------------------------------------------------

    /// <summary>
    /// The bevel every Windows 95 control was built from: two 1-pixel rings, light from the top-left
    /// and shade to the bottom-right (<paramref name="raised"/>), or the reverse (sunken — what a text
    /// field, a list or a status panel wore). <paramref name="thin"/> draws the single-ring version:
    /// a toolbar button, a separator, a group box. Never anti-aliased.
    /// </summary>
    public static void Bevel(Graphics g, Rectangle r, bool raised, bool thin = false)
    {
        if (r.Width < 2 || r.Height < 2) return;
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        if (thin) BevelRing(g, r, raised ? FaceHi : FaceShadow, raised ? FaceShadow : FaceHi);
        else
        {
            BevelRing(g, r, raised ? FaceHi : FaceShadow, raised ? FaceDark : FaceHi);
            BevelRing(g, Rectangle.Inflate(r, -1, -1), raised ? FaceLight : FaceDark, raised ? FaceShadow : FaceLight);
        }
        g.SmoothingMode = sm;
    }

    private static void BevelRing(Graphics g, Rectangle r, Color topLeft, Color bottomRight)
    {
        if (r.Width < 1 || r.Height < 1) return;
        using var a = new Pen(topLeft);
        using var b = new Pen(bottomRight);
        g.DrawLine(a, r.Left, r.Bottom - 1, r.Left, r.Top);             // left
        g.DrawLine(a, r.Left, r.Top, r.Right - 1, r.Top);               // top
        g.DrawLine(b, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);   // right
        g.DrawLine(b, r.Right - 1, r.Bottom - 1, r.Left, r.Bottom - 1); // bottom
    }

    /// <summary>Fills with the window face and puts a raised (or sunken) edge round it — the two steps
    /// behind every piece of 95 chrome.</summary>
    public static void ClassicPanel(Graphics g, Rectangle r, bool raised = true, bool thin = false)
    {
        using (var b = new SolidBrush(Face)) g.FillRectangle(b, r);
        Bevel(g, r, raised, thin);
    }

    /// <summary>A sunken white field — a text box, a list, a picture box — filled and edged.</summary>
    public static void ClassicField(Graphics g, Rectangle r, Color? fill = null)
    {
        using (var b = new SolidBrush(fill ?? Color.White)) g.FillRectangle(b, r);
        Bevel(g, r, raised: false);
    }

    /// <summary>The 13×13 check box of 1995: a sunken white well, with the tick drawn as the era's
    /// hand-made pixel glyph rather than a font character.</summary>
    public static void ClassicCheck(Graphics g, Rectangle box, bool on, bool enabled = true)
    {
        ClassicField(g, box, enabled ? Color.White : Face);
        if (!on) return;
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(enabled ? Color.Black : FaceShadow);
        // The tick is three descending strokes then four ascending — drawn, not typed.
        int x = box.X + 3, y = box.Y + box.Height / 2 - 1;
        for (int i = 0; i < 3; i++) g.DrawLine(pen, x + i, y + i, x + i, y + i + 2);
        for (int i = 0; i < 4; i++) g.DrawLine(pen, x + 3 + i, y + 2 - i, x + 3 + i, y + 4 - i);
        g.SmoothingMode = sm;
    }

    /// <summary>A 95 window's title bar: the navy-to-blue caption gradient with its white bold label.</summary>
    public static void ClassicCaption(Graphics g, Rectangle r, bool active = true)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (active)
        {
            using var lg = new LinearGradientBrush(r, ClassicNavy, ClassicNavyEnd, LinearGradientMode.Horizontal);
            g.FillRectangle(lg, r);
        }
        else
        {
            using var b = new SolidBrush(FaceShadow);
            g.FillRectangle(b, r);
        }
    }

    /// <summary>A caption button (minimise / maximise / close): a small raised face carrying a pixel glyph.</summary>
    public static void ClassicCaptionButton(Graphics g, Rectangle r, char glyph, bool pressed = false)
    {
        using (var b = new SolidBrush(Face)) g.FillRectangle(b, r);
        Bevel(g, r, raised: !pressed);
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Color.Black);
        int cx = r.X + r.Width / 2 + (pressed ? 1 : 0), cy = r.Y + r.Height / 2 + (pressed ? 1 : 0);
        switch (glyph)
        {
            case '_':
                g.FillRectangle(Brushes.Black, cx - 3, cy + 3, 6, 2);
                break;
            case 'O':
                g.DrawRectangle(pen, cx - 5, cy - 4, 9, 8);
                g.DrawLine(pen, cx - 5, cy - 3, cx + 4, cy - 3);
                break;
            case 'X':
                for (int i = 0; i < 7; i++)
                {
                    g.FillRectangle(Brushes.Black, cx - 3 + i, cy - 3 + i, 1, 1);
                    g.FillRectangle(Brushes.Black, cx + 3 - i, cy - 3 + i, 1, 1);
                }
                break;
        }
        g.SmoothingMode = sm;
    }

    // ---- carrying baked properties across a skin change --------------------------------------------

    private sealed record FontSpec(float Size, FontStyle Style, bool Display);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Font, FontSpec> s_fontSpecs = new();

    /// <summary>Records the size/style a themed font was ASKED for, so <see cref="Respec"/> can rebuild
    /// it at the other skin's face. Classic collapses the size ramp, so the request — not the resulting
    /// font — is the only thing that survives a round trip.</summary>
    internal static Font Track(Font f, float size, FontStyle style, bool display)
    {
        s_fontSpecs.AddOrUpdate(f, new FontSpec(size, style, display));
        return f;
    }

    /// <summary>The same font rebuilt for the CURRENT skin, or null when it did not come from
    /// UiFont/DisplayFont — a caller's own font is never touched. UI fonts come from the skin-keyed
    /// shared cache, so re-skinning allocates nothing per control.</summary>
    public static Font? Respec(Font? f)
    {
        if (f == null || !s_fontSpecs.TryGetValue(f, out var spec)) return null;
        return spec.Display ? DisplayFont(spec.Size, spec.Style) : UiFontShared(spec.Size, spec.Style);
    }

    /// <summary>What each surface token is in each skin. Used to move a baked property to the other
    /// skin; anything that matches no token is a deliberate colour and is left alone.</summary>
    private static IEnumerable<(Color Modern, Color Classic)> TokenPairs()
    {
        yield return (Color.FromArgb(16, 17, 20), Face);                                  // Bg
        yield return (Color.FromArgb(24, 26, 31), Face);                                  // PanelBg
        yield return (Color.FromArgb(33, 36, 43), Color.White);                           // RowBg
        yield return (Color.FromArgb(43, 47, 56), Color.FromArgb(230, 230, 230));         // RowHover
        yield return (Color.FromArgb(232, 234, 238), Color.Black);                        // TextCol
        yield return (Color.FromArgb(138, 144, 155), Color.FromArgb(56, 56, 56));         // Subtle
        yield return (Color.FromArgb(52, 56, 66), FaceShadow);                            // Border
        yield return (Color.FromArgb(255, 120, 110), Color.FromArgb(128, 0, 0));          // ErrorCol
        yield return (_accentSpec, ClassicNavy);                                          // Accent
        yield return (Dim(_accentSpec), Color.FromArgb(0, 0, 80));                        // AccentDim
    }

    private static Color Dim(Color c) => Color.FromArgb((int)(c.R * 0.55), (int)(c.G * 0.55), (int)(c.B * 0.55));

    /// <summary>Moves a colour that is a theme token to the current skin's value for that token.
    /// NOTE: Classic draws both Bg and PanelBg as the window face, so a Classic-to-Modern round trip
    /// resolves that pair to Bg. Two panels pick up the page colour instead of the card colour; nothing
    /// else is ambiguous.</summary>
    public static Color Remap(Color c)
    {
        int argb = c.ToArgb();
        foreach (var (modern, classic) in TokenPairs())
            if (argb == modern.ToArgb() || argb == classic.ToArgb())
                return Classic ? classic : modern;
        return c;
    }
}
