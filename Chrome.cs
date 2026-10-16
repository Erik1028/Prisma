using System.Drawing.Drawing2D;

namespace RGBCommander;

// The window chrome of the refreshed main window: one borderless strip that IS the title bar, laid out on
// Mixtape's exact metrics so the two apps read as one family. Logo + wordmark on the left, the power disc,
// a centred "deck" card that shows what the lights are doing, two flat toggles, the gear and the caption
// buttons. Classic folds the same pieces into a 1995 window: a navy caption band with the caption buttons,
// and a raised toolbar under it.
//
// Structure (from the adversarial WinForms review): the strip is a plain container that answers
// HTTRANSPARENT for every point, so empty strip area falls through to the form, which returns HTCAPTION -
// native drag, Aero snap, double-click maximise and drag-restore. Every interactive element is a REAL
// child control (native tooltips, focus, keyboard), and each paints its own slice of what lies under it
// (the wallpaper, or in Classic the caption band / toolbar face), so nothing relies on transparent
// compositing. Nothing in the strip runs a timer: the deck's live line is fed from RenderFrame and only its
// own few hundred pixels are invalidated, so the UI thread that paces the device writes is never starved.

/// <summary>Segoe Fluent / MDL2 icon glyphs as cached vector paths, centred on their INK box - font cell
/// metrics sit icon glyphs visibly off-centre. Sizes are in pixels, not points.</summary>
internal static class Glyphs
{
    public const string Power = "", Monitor = "", Pin = "", Pinned = "",
                        Gear = "", Palette = "", Add = "";

    private static readonly Dictionary<(string, float), GraphicsPath?> s_cache = new();
    private static readonly Dictionary<(string, float), SizeF> s_ink = new();

    private static GraphicsPath? PathFor(string glyph, float px)
    {
        if (s_cache.TryGetValue((glyph, px), out var hit)) return hit;
        GraphicsPath? p = null;
        if (Theme.IconFontName != null)
        {
            try
            {
                using var fam = new FontFamily(Theme.IconFontName);
                p = new GraphicsPath();
                p.AddString(glyph, fam, (int)FontStyle.Regular, px, PointF.Empty, StringFormat.GenericTypographic);
                var ink = p.GetBounds();
                using var m = new Matrix();
                m.Translate(-ink.X - ink.Width / 2f, -ink.Y - ink.Height / 2f);
                p.Transform(m);
                s_ink[(glyph, px)] = ink.Size;
            }
            catch { p?.Dispose(); p = null; }
        }
        s_cache[(glyph, px)] = p;
        return p;
    }

    /// <summary>Fills the glyph centred in <paramref name="box"/>.</summary>
    public static void Draw(Graphics g, string glyph, float px, RectangleF box, Color color)
    {
        var p = PathFor(glyph, px);
        if (p == null) return;
        var st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // pixel i covers [i, i+1]
        // Snap the ink box's TOP-LEFT to a whole pixel (not its centre): an even-width glyph centred on a
        // pixel centre turns every 1 px stroke into two 50% ones.
        var ink = s_ink.TryGetValue((glyph, px), out var sz) ? sz : SizeF.Empty;
        float ox = (float)Math.Round(box.X + (box.Width - ink.Width) / 2f) + ink.Width / 2f;
        float oy = (float)Math.Round(box.Y + (box.Height - ink.Height) / 2f) + ink.Height / 2f;
        g.TranslateTransform(ox, oy);
        using var b = new SolidBrush(color);
        g.FillPath(b, p);
        g.Restore(st);
    }
}

/// <summary>A child of the title strip. Paints whatever lies under it on the strip itself (so it works on
/// the wallpaper AND on the Classic navy caption band), tracks hover/press, and is keyboard-operable:
/// Space or Enter clicks it, and a focus ring shows only for keyboard focus.</summary>
internal abstract class StripChild : Control
{
    protected bool Hover, Down;

    protected StripChild()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        TabStop = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Parent is TitleStrip strip) strip.PaintUnder(e.Graphics, this);
        else Backdrop.Paint(e.Graphics, this);
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (Down) { Down = false; Invalidate(); } base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyHeld = false; Down = false; Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    // Like a native button: Enter clicks once on the first key-down, Space clicks on key-up, and the
    // keyboard's auto-repeat never fires the action again (held Space on the power disc would otherwise
    // toggle every device ~30 times a second, a burst of hardware mode writes).
    private bool _keyHeld;
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            if (!_keyHeld)
            {
                _keyHeld = true;
                if (e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
                else { Down = true; Invalidate(); }
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _keyHeld && Down) { Down = false; Invalidate(); OnClick(EventArgs.Empty); }
        if (e.KeyCode is Keys.Space or Keys.Enter) _keyHeld = false;
        base.OnKeyUp(e);
    }

    /// <summary>A left click only (a right click must never fire the action).</summary>
    protected override void OnClick(EventArgs e)
    {
        if (e is MouseEventArgs me && me.Button != MouseButtons.Left) return;
        base.OnClick(e);
    }

    protected void PaintFocus(Graphics g, Rectangle r)
    {
        if (!Focused || !ShowFocusCues) return;
        if (Theme.Classic) { ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -3, -3)); return; }
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.AccentBright, 1.5f);
        using var p = Theme.RoundedRect(new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f), 8f);
        g.DrawPath(pen, p);
        g.SmoothingMode = sm;
    }

    /// <summary>The 1995 toolbar button: always raised, sunken while held; a LATCHED button (an on-state
    /// toggle) stays sunken over the 50% dither a checked button wore.</summary>
    internal static void PaintClassicTool(Graphics g, Rectangle r, bool latched, bool down)
    {
        if (latched)
        {
            using var hatch = new HatchBrush(HatchStyle.Percent50, Theme.FaceHi, Theme.Face);
            g.FillRectangle(hatch, r);
        }
        else using (var b = new SolidBrush(Theme.Face)) g.FillRectangle(b, r);
        Theme.Bevel(g, r, raised: !(latched || down));
    }
}

/// <summary>The app's mark: the exe icon's own spectrum "P" plus the "Prisma" wordmark. Click (or Space /
/// Enter) opens the About popup. The ONLY fixed colour in the chrome - everything else that is coloured
/// comes from the lights.</summary>
internal sealed class BrandButton : StripChild
{
    private static Bitmap? s_logo32, s_logo16;
    private const string Word = "Prisma";

    public BrandButton()
    {
        AccessibleName = "About Prisma";
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { Cursor = Theme.Classic ? Cursors.Default : Cursors.Hand; base.OnMouseEnter(e); }

    internal static Bitmap? Logo(int size)
    {
        ref Bitmap? slot = ref (size <= 16 ? ref s_logo16 : ref s_logo32);
        if (slot != null) return slot;
        try
        {
            using var ico = Icon.ExtractIcon(Application.ExecutablePath, 0, size <= 16 ? 16 : 32);
            if (ico != null) slot = ico.ToBitmap();
        }
        catch { }
        return slot;
    }

    /// <summary>The width this control wants in the current skin (it measures its own wordmark).</summary>
    public int PreferredWidthFor()
    {
        using var g = CreateGraphics();
        if (Theme.Classic) return 16;   // the caption icon; the title text belongs to the draggable band
        int w = TextRenderer.MeasureText(g, Word, Theme.DisplayFontShared(15f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
        return 6 + 20 + 8 + w + 8;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Theme.Classic)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            var l16 = Logo(16);
            if (l16 != null) g.DrawImage(l16, new Rectangle(0, (Height - 16) / 2, 16, 16));
            PaintFocus(g, ClientRectangle);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Hover)
            using (var chip = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 8))
            using (var b = new SolidBrush(Theme.RowHover))
                g.FillPath(b, chip);
        var logo = Logo(32);
        if (logo != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(logo, new Rectangle(6, (Height - 20) / 2, 20, 20));
        }
        CrispText.DrawText(g, Word, Theme.DisplayFontShared(15f, FontStyle.Bold), new Rectangle(34, 0, Width - 34, Height),
            Theme.TextCol, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        PaintFocus(g, ClientRectangle);
    }
}

/// <summary>Lights on / off - Prisma's "play" disc: filled with the accent while the lights are on, a dark
/// disc when they are off.</summary>
internal sealed class PowerButton : StripChild
{
    private bool _on = true;
    public bool On { get => _on; set { if (_on == value) return; _on = value; Invalidate(); } }

    public PowerButton()
    {
        AccessibleName = "Lights on or off";
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (Theme.Classic)
        {
            PaintClassicTool(g, r, latched: _on, down: Down);
            int d = _on || Down ? 1 : 0;
            ClassicIcons.DrawCentered(g, ClassicIcons.Id.Power, new Rectangle(r.X + d, r.Y + d, r.Width, r.Height));
            PaintFocus(g, r);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color disc = _on ? (Hover ? Theme.AccentBright : Theme.Accent)
                         : (Hover ? Color.FromArgb(62, 63, 67) : Color.FromArgb(49, 50, 53));
        using (var b = new SolidBrush(disc)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        Glyphs.Draw(g, Glyphs.Power, 15f, r, _on ? Theme.OnAccent : (Hover ? Theme.TextCol : Theme.Subtle));
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(Theme.AccentBright, 1.5f)) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
    }
}

/// <summary>A borderless icon button: no plate at rest, a RowHover chip under the pointer. A mode toggle
/// that is ON shows an accent glyph and no chip (Mixtape's shuffle / repeat convention). Ghost buttons (the
/// gear) use a slightly brighter rest glyph.</summary>
internal sealed class FlatIconButton : StripChild
{
    public string Glyph { get; set; } = Glyphs.Gear;
    /// <summary>The glyph while Active (e.g. the filled pin), or null to keep <see cref="Glyph"/>.</summary>
    public string? ActiveGlyph { get; set; }
    public ClassicIcons.Id ClassicIcon { get; set; } = ClassicIcons.Id.Gear;
    public float GlyphPx { get; set; } = 16f;
    public bool Ghost { get; set; }

    private bool _active;
    public bool Active { get => _active; set { if (_active == value) return; _active = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (Theme.Classic)
        {
            PaintClassicTool(g, r, latched: _active, down: Down);
            int d = _active || Down ? 1 : 0;
            ClassicIcons.DrawCentered(g, ClassicIcon, new Rectangle(r.X + d, r.Y + d, r.Width, r.Height));
            PaintFocus(g, r);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // ON reads by more than hue (a red-green colour-blind user cannot tell an accent glyph from a grey
        // one): the selected-tile wash sits behind it, and the glyph is the brighter accent.
        if (_active || Hover)
            using (var chip = Theme.RoundedRect(r, 8))
            using (var b = new SolidBrush(_active ? Color.FromArgb(Hover ? 70 : 46, Theme.Accent) : Theme.RowHover))
                g.FillPath(b, chip);
        Color ink = _active ? Theme.AccentBright
                  : Hover ? Theme.TextCol
                  : Ghost ? Color.FromArgb(205, 210, 214) : Theme.Subtle;
        Glyphs.Draw(g, _active && ActiveGlyph != null ? ActiveGlyph : Glyph, GlyphPx, r, ink);
        PaintFocus(g, r);
    }
}

/// <summary>Minimise / maximise-restore / close, drawn as vectors (never icon-font glyphs): Mixtape's
/// WindowButton look in Modern, the 16x14 pixel buttons of 1995 in Classic.</summary>
internal sealed class CaptionButton : StripChild
{
    public enum Kind { Minimize, Maximize, Close }
    public Kind Which { get; init; }

    private bool _maximized;
    public bool Maximized { get => _maximized; set { if (_maximized == value) return; _maximized = value; Invalidate(); } }

    public CaptionButton() { TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Theme.Classic) { PaintClassic(e.Graphics); return; }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Hover)
        {
            var hc = Which == Kind.Close ? Color.FromArgb(Down ? 200 : 255, 232, 17, 35) : Color.FromArgb(Down ? 24 : 36, 255, 255, 255);
            using var hb = new SolidBrush(hc);
            using var hp = Theme.RoundedRect(new RectangleF(2.5f, 2.5f, Width - 5f, Height - 5f), 7f);
            g.FillPath(hb, hp);
        }
        Color stroke = Which == Kind.Close && Hover ? Color.White : Color.FromArgb(218, 222, 226);
        using var pen = new Pen(stroke, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = Width / 2f, cy = Height / 2f, s = 5.5f, rad = 3.2f;
        switch (Which)
        {
            case Kind.Minimize:
                g.DrawLine(pen, cx - s, cy, cx + s, cy);
                break;
            case Kind.Maximize when !_maximized:
                using (var mp = Theme.RoundedRect(new RectangleF(cx - s, cy - s, s * 2, s * 2), rad)) g.DrawPath(pen, mp);
                break;
            case Kind.Maximize:
            {
                float o = 2.2f, sq = s * 2 - o, rr = rad * 0.8f;
                using (var back = Theme.RoundedRect(new RectangleF(cx - s + o, cy - s - o, sq, sq), rr)) g.DrawPath(pen, back);
                var front = new RectangleF(cx - s, cy - s + o, sq, sq);
                using (var fill = new SolidBrush(Hover ? Color.FromArgb(70, 0, 0, 0) : Theme.Blend(Theme.RailBg, Color.Black, 0.38)))
                using (var fp = Theme.RoundedRect(front, rr)) g.FillPath(fill, fp);
                using (var fp2 = Theme.RoundedRect(front, rr)) g.DrawPath(pen, fp2);
                break;
            }
            case Kind.Close:
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                break;
        }
    }

    /// <summary>The caption buttons of 1995: a small raised face (sunken while held) carrying a hand-placed
    /// 1-pixel glyph. Drawn, not scaled - anti-aliasing any of it would give the era away at once.</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        var r = new Rectangle(0, 0, Width, Height);
        using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, r);
        Theme.Bevel(g, r, raised: !Down);
        int dx = Down ? 1 : 0;
        int cx = Width / 2 + dx, cy = Height / 2 + dx;
        using var pen = new Pen(Theme.FaceDark);
        using var br = new SolidBrush(Theme.FaceDark);
        switch (Which)
        {
            case Kind.Minimize:
                g.FillRectangle(br, cx - 4, cy + 3, 7, 2);
                break;
            case Kind.Maximize when !_maximized:
                g.DrawRectangle(pen, cx - 5, cy - 5, 9, 9);
                g.DrawLine(pen, cx - 5, cy - 4, cx + 4, cy - 4);   // the thick caption line of a title bar
                break;
            case Kind.Maximize:                                     // restore: a small window in front of a larger one
                g.DrawRectangle(pen, cx - 2, cy - 5, 6, 6);
                g.DrawLine(pen, cx - 2, cy - 4, cx + 4, cy - 4);
                using (var face = new SolidBrush(Theme.Face)) g.FillRectangle(face, cx - 5, cy - 2, 7, 7);
                g.DrawRectangle(pen, cx - 5, cy - 2, 6, 6);
                g.DrawLine(pen, cx - 5, cy - 1, cx + 1, cy - 1);
                break;
            case Kind.Close:
                for (int i = 0; i < 6; i++)                          // a 6x6 cross, a pixel at a time, two wide
                {
                    g.FillRectangle(br, cx - 3 + i, cy - 3 + i, 2, 1);
                    g.FillRectangle(br, cx + 2 - i, cy - 3 + i, 2, 1);
                }
                break;
        }
    }
}

/// <summary>
/// The deck: what the lights are doing, in the middle of the title bar - Prisma's counterpart of Mixtape's
/// "now playing" card. The tile is the effect's art (click it to pick a colour), the title the effect, the
/// second line the app status, a right column the brightness and the base colour, and along the bottom a
/// thin line of the actual LED colours (at full value, so it reads even at 6% brightness).
/// Only the tile (and an ellipsised or error status line) takes the mouse; the rest of the card is
/// HTTRANSPARENT, so the window can be dragged by it.
/// </summary>
internal sealed class DeckCard : StripChild
{
    public MainForm.Effect Effect { get; private set; } = MainForm.Effect.Static;
    public Color BaseColor { get; private set; } = Color.White;
    public Color EndColor { get; private set; } = Color.White;
    public Color LiveColor { get; private set; }
    public string Title { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool StatusError { get; private set; }
    public int Brightness { get; private set; } = 100;

    /// <summary>Raised when the tile is clicked: the host opens the colour picker.</summary>
    public event EventHandler? TileClicked;

    private uint[] _leds = Array.Empty<uint>();
    private bool _off;
    private bool _tileHover;
    private bool _statusClipped;

    public DeckCard()
    {
        TabStop = false;   // the tile's action (pick a colour) is also COLOR > Custom..., so no tab stop
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = "Now lighting";
    }

    // ---- state (each setter repaints only when something actually changed) ----
    public void SetLook(MainForm.Effect fx, Color baseC, Color endC, string title, int brightness)
    {
        if (fx == Effect && baseC.ToArgb() == BaseColor.ToArgb() && endC.ToArgb() == EndColor.ToArgb() &&
            title == Title && brightness == Brightness) return;
        Effect = fx; BaseColor = baseC; EndColor = endC; Title = title; Brightness = brightness;
        Invalidate();
    }

    public void SetStatus(string text, bool error)
    {
        if (text == Status && error == StatusError) return;
        Status = text; StatusError = error;
        // the status (and its error dot) live on line 1; the union also covers any shared pixel rows
        Invalidate(Rectangle.Union(TextRect(0), TextRect(1)));
    }

    /// <summary>Called from RenderFrame with the 22-LED preview frame at full value. Repaints the
    /// live line (and the tile for Screen sync, whose art IS the sampled colour) only when it changed.</summary>
    public void SetLive(uint[] leds, bool off, Color ambient)
    {
        if (!Visible || IsDisposed) return;
        if (ambient.ToArgb() != LiveColor.ToArgb())
        {
            LiveColor = ambient;
            if (Effect == MainForm.Effect.Ambient)
            {
                Invalidate(TileRect);
                if (Figures) Invalidate(FigRect(1));   // the sampled colour's hex
            }
        }
        if (off == _off && _leds.AsSpan().SequenceEqual(leds)) return;
        _off = off;
        _leds = (uint[])leds.Clone();
        Invalidate(Rectangle.Inflate(LineRect, 0, 2));
    }

    // ---- geometry: Mixtape's NowPlayingBar card, exactly ----
    private bool Figures => !Theme.Classic && Width >= 300;
    internal Rectangle TileRect => Theme.Classic ? new Rectangle(5, 5, 30, 30) : new Rectangle(7, 7, 40, 40);
    private int TextX => Theme.Classic ? 41 : 59;
    private Rectangle TextRect(int line)
    {
        int right = Width - (Theme.Classic ? 6 : 14) - (Figures ? 46 + 8 : 0);
        int w = Math.Max(0, right - TextX);
        if (Theme.Classic) return line == 0 ? new Rectangle(TextX, 3, w, 14) : new Rectangle(TextX, 16, w, 13);
        return line == 0 ? new Rectangle(TextX, 8, w, 17) : new Rectangle(TextX, 25, w, 15);
    }
    private Rectangle FigRect(int line) => new(Width - 14 - 46, line == 0 ? 8 : 25, 46, line == 0 ? 17 : 15);
    private Rectangle LineRect => Theme.Classic
        ? new Rectangle(TextX, 30, Math.Max(0, Width - TextX - 6), 6)
        : new Rectangle(TextX, Height - 7 - 3, Math.Max(0, Width - 14 - TextX), 3);

    // ---- hit testing: only the tile (and a clipped/error status line) takes the mouse ----
    private const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && !TakesMouse(PointToClient(Cursor.Position)))
        {
            m.Result = (IntPtr)HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Whether this point of the card takes the mouse (else it falls through to drag the window).</summary>
    internal bool TakesMouse(Point p) => TileRect.Contains(p) || (StatusTipWanted && TextRect(1).Contains(p));

    private bool StatusTipWanted => StatusError || _statusClipped;

    /// <summary>What the tooltip should say where the pointer is (the host owns the ToolTip).</summary>
    public string? TipAt(Point p) =>
        TileRect.Contains(p) ? TileTip
        : StatusTipWanted && TextRect(1).Contains(p) ? Status : null;

    /// <summary>Set by the host: the tile's tooltip for the current colour target / effect.</summary>
    public string TileTip { get; set; } = "Pick a color…";

    public event EventHandler? TipRegionChanged;
    private string? _lastTip;
    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool over = TileRect.Contains(e.Location);
        if (over != _tileHover) { _tileHover = over; Cursor = over && !Theme.Classic ? Cursors.Hand : Cursors.Default; Invalidate(TileRect); }
        var tip = TipAt(e.Location);
        if (tip != _lastTip) { _lastTip = tip; TipRegionChanged?.Invoke(this, EventArgs.Empty); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        if (_tileHover) { _tileHover = false; Cursor = Cursors.Default; Invalidate(TileRect); }
        base.OnMouseLeave(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool wasDown = Down;
        base.OnMouseUp(e);
        if (wasDown && e.Button == MouseButtons.Left && TileRect.Contains(e.Location)) TileClicked?.Invoke(this, EventArgs.Empty);
    }

    // ---- paint ----
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Theme.Classic) { PaintClassic(e.Graphics); return; }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var card = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Theme.RoundedRect(card, 10f))
        {
            using (var fill = new SolidBrush(Color.FromArgb(200, Theme.Blend(Theme.RailBg, Color.White, 0.07)))) g.FillPath(fill, path);
            using (var hair = new Pen(Color.FromArgb(34, 255, 255, 255))) g.DrawPath(hair, path);
        }

        // tile: the effect's art, a soft shadow under it; on hover a wash + the palette glyph = "pick a colour"
        var t = TileRect;
        using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
        using (var sp = Theme.RoundedRect(new Rectangle(t.X, t.Y + 2, t.Width, t.Height), 5))
            g.FillPath(sh, sp);
        EffectArt.Paint(g, t, Effect, BaseColor, EndColor, 5, LiveColor);
        if (_tileHover)
        {
            using (var wash = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
            using (var wp = Theme.RoundedRect(t, 5))
                g.FillPath(wash, wp);
            Glyphs.Draw(g, Glyphs.Palette, 16f, t, Theme.OnColor(BaseColor));
        }

        // text column
        var tr = TextRect(0);
        const TextFormatFlags F = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (tr.Width >= 60)
        {
            CrispText.DrawText(g, Title, Theme.UiFontShared(10f, FontStyle.Bold), tr, Theme.TextCol, F);
            var sr = TextRect(1);
            var sf = Theme.UiFontShared(8.75f);
            _statusClipped = TextRenderer.MeasureText(g, Status, sf, new Size(int.MaxValue, sr.Height), F).Width > sr.Width;
            if (StatusError)
            {
                // a small dot leads an error line, so it reads as a warning before it is read
                using (var dot = new SolidBrush(Theme.ErrorCol)) g.FillEllipse(dot, sr.X, sr.Y + sr.Height / 2 - 3, 6, 6);
                sr = new Rectangle(sr.X + 11, sr.Y, sr.Width - 11, sr.Height);
            }
            CrispText.DrawText(g, Status, sf, sr, StatusError ? Theme.ErrorCol : Theme.Subtle, F);
        }
        if (Figures) PaintFigures(g);

        PaintLiveLine(g);
    }

    private static bool SelfColouring(MainForm.Effect e) => e is MainForm.Effect.RainbowWave or MainForm.Effect.ColorCycle
        or MainForm.Effect.Fire or MainForm.Effect.Surprise;

    /// <summary>The right column: how bright (or "off"), and which colour - read from the LOOK, which
    /// repaints the whole card when it changes. Self-colouring effects say "auto" rather than a picked
    /// hex they ignore; Screen sync shows the sampled colour; Gradient its two ends.</summary>
    private void PaintFigures(Graphics g)
    {
        var R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var f = Theme.UiFontShared(8f);
        bool off = Effect == MainForm.Effect.Off;
        CrispText.DrawText(g, off ? "off" : $"{Brightness}%", f, FigRect(0), Theme.Subtle, R);
        var r1 = FigRect(1);
        if (off || SelfColouring(Effect)) { CrispText.DrawText(g, "auto", f, r1, Theme.Faint, R); return; }
        if (Effect == MainForm.Effect.Gradient)
        {
            // two 8 px dots, start -> end, right-aligned
            int cy = r1.Y + r1.Height / 2 - 4;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(EndColor)) g.FillEllipse(b, r1.Right - 8, cy, 8, 8);
            using (var b = new SolidBrush(BaseColor)) g.FillEllipse(b, r1.Right - 20, cy, 8, 8);
            using (var rim = new Pen(Color.FromArgb(40, 255, 255, 255)))
            {
                g.DrawEllipse(rim, r1.Right - 8, cy, 7.5f, 7.5f);
                g.DrawEllipse(rim, r1.Right - 20, cy, 7.5f, 7.5f);
            }
            return;
        }
        var c = Effect == MainForm.Effect.Ambient && LiveColor.A > 0 ? LiveColor : BaseColor;
        CrispText.DrawText(g, $"#{c.R:X2}{c.G:X2}{c.B:X2}", f, r1, Theme.Faint, R);
    }

    /// <summary>The live LED line: the frame's colours with hard stops, filled THROUGH the anti-aliased
    /// 3 px pill (a clip region is never anti-aliased), on whole pixel rows.</summary>
    private void PaintLiveLine(Graphics g)
    {
        var lr = LineRect;
        if (lr.Width < 4) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // the 3 px pill covers exactly 3 rows
        using var pill = Theme.RoundedRect(new RectangleF(lr.X, lr.Y, lr.Width, lr.Height), 1.5f);
        int n = _leds.Length;
        if (n == 0 || _off)
        {
            using var track = new SolidBrush(_off && n > 0 ? Color.FromArgb(90, Theme.Faint) : Color.FromArgb(38, 255, 255, 255));
            g.FillPath(track, pill);
            return;
        }
        // hard stops: each LED its own band; ColorBlend positions must start at 0 and end at 1
        var cols = new Color[n * 2];
        var pos = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            uint c = _leds[i];
            var col = Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            cols[2 * i] = cols[2 * i + 1] = col;
            pos[2 * i] = i / (float)n;
            pos[2 * i + 1] = (i + 1) / (float)n - (i < n - 1 ? 0.0001f : 0f);
        }
        pos[0] = 0f; pos[^1] = 1f;
        using var lg = new LinearGradientBrush(new RectangleF(lr.X - 0.5f, lr.Y, lr.Width + 1f, lr.Height), Color.Black, Color.Black, 0f)
        {
            InterpolationColors = new ColorBlend { Colors = cols, Positions = pos },
        };
        g.FillPath(lg, pill);
    }

    /// <summary>Classic: a sunken white field on the toolbar - a sunken colour well, the title in bold,
    /// the status under it, and the LEDs as discrete cells on a sunken black bed (the CD-Player readout).</summary>
    private void PaintClassic(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = Theme.TextHint;
        Theme.ClassicField(g, ClientRectangle);
        var t = TileRect;
        Theme.ClassicField(g, t, Theme.Face);
        EffectArt.Paint(g, Rectangle.Inflate(t, -2, -2), Effect, BaseColor, EndColor, 0, LiveColor);
        const TextFormatFlags F = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        CrispText.DrawText(g, Title, Theme.UiFontShared(9f, FontStyle.Bold), TextRect(0), Color.Black, F);
        var sr = TextRect(1);
        _statusClipped = TextRenderer.MeasureText(g, Status, Theme.UiFontShared(9f), new Size(int.MaxValue, sr.Height), F).Width > sr.Width;
        CrispText.DrawText(g, Status, Theme.UiFontShared(9f), sr, StatusError ? Theme.ErrorCol : Color.Black, F);

        var lr = LineRect;
        if (lr.Width < 8) return;
        using (var bed = new SolidBrush(Color.Black)) g.FillRectangle(bed, lr);
        Theme.Bevel(g, Rectangle.Inflate(lr, 1, 1), raised: false, thin: true);
        if (_leds.Length == 0) return;
        int cells = Math.Max(1, (lr.Width - 1) / 3);          // 2 px cells, 1 px black gaps
        using var seg = new SolidBrush(Color.Black);
        for (int i = 0; i < cells; i++)
        {
            uint c = _leds[i * _leds.Length / cells];
            seg.Color = _off ? Color.FromArgb(40, 40, 40)
                : Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            g.FillRectangle(seg, lr.X + 1 + i * 3, lr.Y + 1, 2, lr.Height - 2);
        }
    }
}

/// <summary>
/// The title strip: a plain container across the top of the window. It answers HTTRANSPARENT for every
/// point (empty strip falls through to the form's HTCAPTION), paints the wallpaper - and in Classic the
/// navy caption band and the raised toolbar - and lays its children out on Mixtape's metrics.
/// </summary>
internal sealed class TitleStrip : Panel
{
    /// <summary>Total height in BOTH skins, so a live skin switch never moves the body.</summary>
    public const int TopH = 78;

    public BrandButton? Brand { get; set; }
    public PowerButton? Power { get; set; }
    public DeckCard? Deck { get; set; }
    public FlatIconButton? Screen { get; set; }
    public FlatIconButton? Pin { get; set; }
    public FlatIconButton? Gear { get; set; }
    public CaptionButton? Min { get; set; }
    public CaptionButton? Max { get; set; }
    public CaptionButton? Close { get; set; }

    private bool _active = true;
    /// <summary>Classic: the caption band is navy while the window is active, grey when not.</summary>
    public bool CaptionActive { get => _active; set { if (_active == value) return; _active = value; Invalidate(CaptionBand, true); } }

    private readonly List<int> _separators = new();

    public TitleStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        Height = TopH;
    }

    private int Frame => Theme.Classic && FindForm()?.WindowState != FormWindowState.Maximized ? 3 : 0;
    private Rectangle CaptionBand => new(Frame, Frame, Math.Max(0, Width - 2 * Frame), 20);
    private Rectangle Toolbar => new(Frame, Frame + 21, Math.Max(0, Width - 2 * Frame), TopH - 4 - (Frame + 21));

    /// <summary>Where a press drags the window (HTCAPTION), in strip == form client coordinates: the whole
    /// strip in Modern (children take their own hits), only the caption band in Classic.</summary>
    public Rectangle DragRect => Theme.Classic ? CaptionBand : new Rectangle(0, 0, Width, TopH);

    private const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintSurface(e.Graphics);

    private void PaintSurface(Graphics g)
    {
        Backdrop.Paint(g, this);
        if (!Theme.Classic) return;
        g.SmoothingMode = SmoothingMode.None;
        var band = CaptionBand;
        Theme.ClassicCaption(g, band, _active);
        // The title is part of the band - pressing it drags the window, as in 1995; only the icon is a
        // button. White on navy while active, light grey on the grey band while not.
        CrispText.DrawText(g, "Prisma", Theme.UiFontShared(9f, FontStyle.Bold),
            new Rectangle(band.X + 21, band.Y, Math.Max(0, band.Width - 21 - 56), band.Height),
            _active ? Color.White : Theme.Face,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var tb = Toolbar;
        Theme.Bevel(g, tb, raised: true, thin: true);
        using var dark = new Pen(Theme.FaceShadow);
        using var light = new Pen(Theme.FaceHi);
        foreach (int x in _separators)                 // etched toolbar separators
        {
            g.DrawLine(dark, x, tb.Top + 4, x, tb.Bottom - 5);
            g.DrawLine(light, x + 1, tb.Top + 4, x + 1, tb.Bottom - 5);
        }
    }

    /// <summary>Paints what lies under <paramref name="child"/> (wallpaper, caption band, toolbar face),
    /// so a child can be opaque and still look seamless.</summary>
    public void PaintUnder(Graphics g, Control child)
    {
        var st = g.Save();
        g.TranslateTransform(-child.Left, -child.Top);
        // Backdrop.Paint works in the painted control's own coordinates; paint it for the strip so the
        // translated slice lines up exactly with the strip's own paint.
        var clip = new Rectangle(child.Left, child.Top, child.Width, child.Height);
        g.SetClip(clip);
        PaintSurface(g);
        g.Restore(st);
    }

    /// <summary>Lays the children out for the current skin and width.</summary>
    public void Relayout()
    {
        int W = Width;
        _separators.Clear();
        if (Theme.Classic) LayoutClassic(W); else LayoutModern(W);
        Invalidate(true);
    }

    private void LayoutModern(int W)
    {
        const int cy = 39;
        int bw = Brand?.PreferredWidthFor() ?? 0;
        Brand?.SetBounds(18, cy - 15, bw, 30);
        int brandRight = 18 + bw - 8;                          // the wordmark's own right edge
        Power?.SetBounds(brandRight + 22, cy - 18, 36, 36);

        Close?.SetBounds(W - 14 - 38, cy - 13, 38, 26);
        Max?.SetBounds(W - 14 - 76, cy - 13, 38, 26);
        Min?.SetBounds(W - 14 - 114, cy - 13, 38, 26);
        int gearX = W - 14 - 114 - 4 - 34;
        Gear?.SetBounds(gearX, cy - 13, 34, 26);
        int pinX = gearX - 14 - 24;
        Pin?.SetBounds(pinX, cy - 12, 24, 24);
        int screenX = pinX - 8 - 24;
        Screen?.SetBounds(screenX, cy - 12, 24, 24);

        if (Deck != null)
        {
            int spanL = (Power?.Right ?? brandRight) + 22, spanR = screenX - 22;
            int w = Math.Clamp(spanR - spanL, 0, 520);
            Deck.SetBounds(spanL + (spanR - spanL - w) / 2, 12, w, 54);
            Deck.Visible = w >= 200;
        }
    }

    private void LayoutClassic(int W)
    {
        int F = Frame;
        var cap = CaptionBand;
        int bw = Brand?.PreferredWidthFor() ?? 0;
        Brand?.SetBounds(F + 2, cap.Top + 2, bw, 16);
        Close?.SetBounds(W - F - 2 - 16, cap.Top + 3, 16, 14);
        Max?.SetBounds(W - F - 2 - 34, cap.Top + 3, 16, 14);
        Min?.SetBounds(W - F - 2 - 50, cap.Top + 3, 16, 14);

        var tb = Toolbar;
        int cy = tb.Top + tb.Height / 2;
        Power?.SetBounds(F + 5, cy - 13, 26, 26);
        int powerRight = F + 5 + 26;
        _separators.Add(powerRight + 5);
        int gearX = W - F - 5 - 26;
        Gear?.SetBounds(gearX, cy - 13, 26, 26);
        _separators.Add(gearX - 7);
        int pinX = gearX - 12 - 26;                             // 5 | separator | 5, like the power side
        Pin?.SetBounds(pinX, cy - 13, 26, 26);
        int screenX = pinX - 26;                                 // touching, like a 95 toolbar group
        Screen?.SetBounds(screenX, cy - 13, 26, 26);
        _separators.Add(screenX - 7);

        if (Deck != null)
        {
            int spanL = powerRight + 12, spanR = screenX - 12;
            int w = Math.Clamp(spanR - spanL, 0, 520);
            Deck.SetBounds(spanL + (spanR - spanL - w) / 2, cy - 20, w, 40);
            Deck.Visible = w >= 200;
        }
    }
}
