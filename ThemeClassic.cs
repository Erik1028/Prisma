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
        ApplySurfaces(classic);
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

    /// <summary>Assigns every surface/text token from the one <see cref="Surf"/> table. Classic is the
    /// 1995 system scheme: one grey window face for all chrome, a white inset surface for lists and
    /// fields, black text, maroon for errors.</summary>
    private static void ApplySurfaces(bool classic)
    {
        Color T(Tok t) => classic ? Surf[(int)t].Classic : Surf[(int)t].Modern;
        Bg = T(Tok.Bg);
        PanelBg = T(Tok.PanelBg);
        RailBg = T(Tok.RailBg);
        RowBg = T(Tok.RowBg);
        RowHover = T(Tok.RowHover);
        Border = T(Tok.Border);
        HairLine = T(Tok.HairLine);
        TextCol = T(Tok.TextCol);
        Subtle = T(Tok.Subtle);
        Faint = T(Tok.Faint);
        ErrorCol = T(Tok.ErrorCol);
    }

    // ---- corner radii as tokens: 1995 had none -----------------------------------------------------
    public static int RadShell { get; private set; } = 10;    // the floating rail / content cards, the deck
    public static int RadCard { get; private set; } = 10;     // a card / panel
    public static int RadControl { get; private set; } = 8;   // a button
    public static int RadChip { get; private set; } = 6;      // a badge, a swatch
    public static int RadSmall { get; private set; } = 4;     // the smallest insets

    private static void SetRadii(bool square)
    {
        RadShell = square ? 0 : 10;
        RadCard = square ? 0 : 10;
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
        using var br = new SolidBrush(enabled ? FaceDark : FaceShadow);
        // The tick is drawn, not typed: seven 1x3 columns, down three then up four (Mixtape's glyph).
        int[] top = { 2, 3, 4, 3, 2, 1, 0 };
        for (int i = 0; i < 7; i++) g.FillRectangle(br, box.X + 3 + i, box.Y + 3 + top[i], 1, 3);
        g.SmoothingMode = sm;
    }

    /// <summary>A 95 window's title bar: SOLID navy when active, the shadow grey when not. (The
    /// navy-to-blue gradient is Windows 98; Mixtape's Classic paints flat navy too.)</summary>
    public static void ClassicCaption(Graphics g, Rectangle r, bool active = true)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var b = new SolidBrush(active ? ClassicNavy : FaceShadow);
        g.FillRectangle(b, r);
    }

    /// <summary>The pointer a clickable control shows: the hand in the modern look, the plain arrow in
    /// Classic (1995 had no hand cursor). Resolved when Windows asks, so a live skin switch needs no
    /// walk over the controls: <c>public override Cursor Cursor { get => Theme.Pointing(base.Cursor); ... }</c>.</summary>
    public static Cursor Pointing(Cursor c) => Classic && c == Cursors.Hand ? Cursors.Default : c;

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

    /// <summary>The tokens a control may BAKE into a property, in Remap's match order (first match
    /// wins). Bg goes first: it is what the forms bake. RailBg, AccentBright and OnAccent are left out on
    /// purpose - their Classic values (Face, Navy, White) are already claimed by Bg, Accent and RowBg, so
    /// they are read in OnPaint only and never baked.</summary>
    private static readonly Tok[] RemapOrder =
        { Tok.Bg, Tok.PanelBg, Tok.RowBg, Tok.RowHover, Tok.Border, Tok.HairLine, Tok.TextCol, Tok.Subtle, Tok.Faint, Tok.ErrorCol };

    private static IEnumerable<(Color Modern, Color Classic)> TokenPairs()
    {
        foreach (var t in RemapOrder) yield return Surf[(int)t];
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
