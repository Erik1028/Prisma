using System.Drawing.Drawing2D;

namespace RGBCommander;

/// <summary>
/// One of the two floating cards of the refreshed body - the left rail (devices, profiles) or the content
/// card (effects, colour, live stage, tuning). It paints its slice of the wallpaper for the corners, then
/// its own rounded surface: Mixtape's layered brightness, no outline (the 14 px wallpaper gap and the
/// brightness step do the separating). In Classic it is just the grey window face: the sections inside
/// draw the 1995 group boxes.
/// </summary>
public sealed class SurfacePanel : GradientPanel
{
    public enum Kind { Rail, Content }
    public Kind Surface { get; init; } = Kind.Content;

    /// <summary>The surface colour, read at paint time (never baked: Classic shares one face for both).</summary>
    public Color SurfaceColor => Surface == Kind.Rail ? Theme.RailBg : Theme.PanelBg;

    private int _ruleY = -1;
    /// <summary>A hairline across the card at this y (the rail footer, the TUNING dock); -1 = none.</summary>
    public int RuleY { get => _ruleY; set { if (_ruleY == value) return; _ruleY = value; Invalidate(); } }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Backdrop.Paint(g, this);
        if (Theme.Classic)
        {
            if (_ruleY >= 0)   // an etched separator line, the way a 95 dialog divided its parts
            {
                using var dark = new Pen(Theme.FaceShadow);
                using var light = new Pen(Theme.FaceHi);
                g.DrawLine(dark, 8, _ruleY, Width - 9, _ruleY);
                g.DrawLine(light, 8, _ruleY + 1, Width - 9, _ruleY + 1);
            }
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 10f))
        using (var b = new SolidBrush(SurfaceColor))
            g.FillPath(b, path);
        if (_ruleY >= 0)
        {
            g.SmoothingMode = SmoothingMode.None;
            using var pen = new Pen(Surface == Kind.Rail ? Theme.Border : Theme.HairLine);
            int inset = Surface == Kind.Rail ? 12 : 22;   // the content card's page inset (PAD)
            g.DrawLine(pen, inset, _ruleY, Width - inset - 1, _ruleY);
        }
    }

    /// <summary>Mixtape's soft card shadow, painted by the FORM under the card (it falls on the
    /// wallpaper gap around it, which the card itself never paints).</summary>
    public static void PaintShadow(Graphics g, Rectangle card)
    {
        if (Theme.Classic || card.Width <= 0 || card.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 5; i >= 1; i--)
        {
            using var p = Theme.RoundedRect(new RectangleF(card.X - i, card.Y - i + 3, card.Width + 2 * i, card.Height + 2 * i), 10f + i);
            using var b = new SolidBrush(Color.FromArgb(6, 0, 0, 0));
            g.FillPath(b, p);
        }
    }
}

/// <summary>
/// A saved lighting profile in the rail - Mixtape's playlist rows: a pill on hover, the profile's own
/// effect art as an 18 px tile in the device rows' enable-circle column, its name in their name column,
/// and the process that triggers it (if any) on the right. The applied profile shows its name in
/// AccentBright with a small accent dot. Click or Enter/Space applies, the menu key or a right-click
/// asks for the menu, Delete asks to delete.
/// </summary>
public sealed class ProfileRow : Control, IFocusCues
{
    bool IFocusCues.ShowCues => ShowFocusCues;
    private bool _hover, _keyHeld;
    public LightingProfile Profile { get; }
    private bool _applied;
    public bool Applied { get => _applied; set { if (_applied == value) return; _applied = value; Invalidate(); } }

    public event EventHandler? ApplyRequested;
    /// <summary>The menu, at this point of the row (the cursor, or under the row for the keyboard).</summary>
    public event EventHandler<Point>? MenuRequested;
    public event EventHandler? DeleteRequested;

    public const int RowH = 34;

    public ProfileRow(LightingProfile profile)
    {
        Profile = profile;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = RowH;
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Apply profile " + profile.Name;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override Cursor Cursor { get => Theme.Pointing(base.Cursor); set => base.Cursor = value; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rail = Theme.Classic ? Color.White : Theme.RailBg;
        g.Clear(rail);
        g.SmoothingMode = Theme.ShapeMode;
        g.TextRenderingHint = Theme.TextHint;
        if (_hover && !Theme.Classic)
            using (var pill = Theme.RoundedRect(new Rectangle(0, 2, Width, Height - 4), 8))
            using (var b = new SolidBrush(Theme.Blend(rail, Color.White, 0.06)))
                g.FillPath(b, pill);

        var fx = Enum.TryParse(Profile.Effect, out MainForm.Effect pe) ? pe : MainForm.Effect.Static;
        // the device rows' columns: the tile where their enable circle is, the name where theirs starts
        var tile = Theme.Classic ? new Rectangle(5, (Height - 16) / 2, 16, 16) : new Rectangle(12, (Height - 18) / 2, 18, 18);
        if (Theme.Classic) Theme.ClassicField(g, Rectangle.Inflate(tile, 1, 1), Theme.Face);
        EffectArt.Paint(g, tile, fx, Color.FromArgb(Profile.ColorArgb),
            Profile.GradientEndArgb != 0 ? Color.FromArgb(Profile.GradientEndArgb) : Color.FromArgb(Profile.ColorArgb),
            Theme.Classic ? 0 : 4);

        int textX = Theme.Classic ? 28 : DeviceRowArt.NameX;
        int right = Width - 12;
        const TextFormatFlags F = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (_applied)
        {
            using (var dot = new SolidBrush(Theme.Classic ? Theme.ClassicNavy : Theme.Accent))
                g.FillEllipse(dot, right - 6, Height / 2 - 3, 6, 6);
            right -= 14;
        }
        if (!string.IsNullOrWhiteSpace(Profile.TriggerProcess))
        {
            var tf = Theme.UiFontShared(8.75f);
            int tw = Math.Min(90, TextRenderer.MeasureText(g, Profile.TriggerProcess, tf, Size.Empty, TextFormatFlags.NoPadding).Width);
            CrispText.DrawText(g, Profile.TriggerProcess, tf, new Rectangle(right - tw, 0, tw, Height),
                Theme.Classic ? Theme.FaceShadow : Theme.Faint, F | TextFormatFlags.Right);
            right -= tw + 8;
        }
        Color name = Theme.Classic ? Color.Black : _applied ? Theme.AccentBright : Theme.TextCol;
        CrispText.DrawText(g, Profile.Name, Theme.UiFontShared(10f, _applied ? FontStyle.Bold : FontStyle.Regular),
            new Rectangle(textX, 0, Math.Max(0, right - textX), Height), name, F);
        DeviceRowArt.PaintFocus(g, this);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) ApplyRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseUp(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x007B)   // WM_CONTEXTMENU: right-click (at the cursor), or the menu key / Shift+F10 (lParam -1)
        {
            var at = m.LParam == (IntPtr)(-1) ? new Point(DeviceRowArt.NameX, Height) : PointToClient(Cursor.Position);
            MenuRequested?.Invoke(this, at);
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter or Keys.Delete || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            if (!_keyHeld) { _keyHeld = true; if (e.KeyCode == Keys.Enter) ApplyRequested?.Invoke(this, EventArgs.Empty); }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete) { DeleteRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _keyHeld) ApplyRequested?.Invoke(this, EventArgs.Empty);
        if (e.KeyCode is Keys.Space or Keys.Enter) _keyHeld = false;
        base.OnKeyUp(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyHeld = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

/// <summary>
/// A rail list - the devices, the profiles - with its OWN scroll bar. A native AutoScroll panel gave the
/// light Windows 11 overlay bar on the dark rail and on the 1995 skin alike; this one draws Mixtape's:
/// a slim pill in the gutter in the modern look, the 16 px 95 bar (dither trough, raised arrow buttons,
/// raised thumb) in Classic. The rows live in <see cref="Content"/>, a FlowLayoutPanel that sizes to
/// them and is moved, not scrolled, inside a clipping viewport. In Classic the list is a sunken white
/// field: it paints the two-ring edge itself, so <see cref="Edge"/> px each side are frame.
/// </summary>
public sealed class ScrollList : Control
{
    private readonly Panel _view;
    public FlowLayoutPanel Content { get; }
    private int _offset;
    private bool _overflow;
    private int _dragFrom = -1, _dragOffset;
    private bool _barHover;
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 400 };
    private int _repeatDir;
    private readonly EventHandler _onChildFocus;

    /// <summary>The row width changed (the bar came or went, or the list was resized).</summary>
    public event EventHandler? RowWidthChanged;

    public ScrollList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        _view = new Panel { BackColor = ListBack };
        Content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = ListBack, Location = Point.Empty,
        };
        _view.Controls.Add(Content);
        Controls.Add(_view);
        Content.SizeChanged += (_, _) => Reflow();
        // a focused row (Tab) must never sit outside the viewport; rows are re-added on every rescan,
        // so the one handler is detached again when a row leaves
        _onChildFocus = (s, _) => EnsureVisible((Control)s!);
        Content.ControlAdded += (_, e) => { e.Control!.GotFocus -= _onChildFocus; e.Control.GotFocus += _onChildFocus; };
        Content.ControlRemoved += (_, e) => e.Control!.GotFocus -= _onChildFocus;
        _repeat.Tick += (_, _) => { _repeat.Interval = 50; Step(_repeatDir * 24); };
    }

    /// <summary>The list's background: white in Classic, the rail surface otherwise.</summary>
    public static Color ListBack => Theme.Classic ? Color.White : Theme.RailBg;

    public int Edge => Theme.Classic ? 2 : 0;
    private int BarW => !_overflow ? 0 : Theme.Classic ? 16 : 10;
    private int ViewH => Math.Max(0, Height - 2 * Edge);
    /// <summary>The width a row should take: the viewport, less the bar when it shows.</summary>
    public int RowWidth => Math.Max(0, Width - 2 * Edge - BarW);
    private int MaxOffset => Math.Max(0, Content.Height - ViewH);

    /// <summary>Re-reads the skin (background, edge, bar width) after a switch.</summary>
    public void Restyle()
    {
        _view.BackColor = Content.BackColor = ListBack;
        Reflow();
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }

    private void Reflow()
    {
        _overflow = Content.Height > ViewH;
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        _view.SetBounds(Edge, Edge, RowWidth, ViewH);
        Content.Top = -_offset;
        if (_lastRowW != RowWidth) { _lastRowW = RowWidth; RowWidthChanged?.Invoke(this, EventArgs.Empty); }
        Invalidate();
    }
    private int _lastRowW = -1;

    public void ScrollToTop() { _offset = 0; Reflow(); }

    public void EnsureVisible(Control c)
    {
        if (c.Parent != Content) return;
        int top = c.Top, bottom = c.Bottom;
        if (top < _offset) _offset = top;
        else if (bottom > _offset + ViewH) _offset = bottom - ViewH;
        Reflow();
    }

    private void Step(int dy)
    {
        int to = Math.Clamp(_offset + dy, 0, MaxOffset);
        if (to == _offset) return;
        _offset = to;
        Content.Top = -_offset;
        Invalidate(BarRect);
    }

    // ---- the bar ----
    private Rectangle BarRect => new(Width - Edge - BarW, Edge, BarW, ViewH);
    private int TrackTop => BarRect.Y + (Theme.Classic ? 16 : 0);
    private int TrackLen => Math.Max(1, ViewH - (Theme.Classic ? 32 : 0));
    private Rectangle ThumbRect
    {
        get
        {
            var b = BarRect;
            int len = TrackLen;
            int th = Math.Max(Theme.Classic ? 10 : 24, (int)((long)len * ViewH / Math.Max(1, Content.Height)));
            th = Math.Min(th, len);
            int ty = TrackTop + (MaxOffset == 0 ? 0 : (int)((long)(len - th) * _offset / MaxOffset));
            return new Rectangle(b.X, ty, b.Width, th);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(ListBack)) g.FillRectangle(bg, ClientRectangle);
        if (Theme.Classic) Theme.Bevel(g, ClientRectangle, raised: false);
        if (!_overflow) return;
        var bar = BarRect;
        var thumb = ThumbRect;
        if (Theme.Classic)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (var dither = new HatchBrush(HatchStyle.Percent50, Theme.FaceHi, Theme.Face))
                g.FillRectangle(dither, bar.X, bar.Y + 16, 16, Math.Max(0, bar.Height - 32));
            var up = new Rectangle(bar.X, bar.Y, 16, 16);
            var dn = new Rectangle(bar.X, bar.Bottom - 16, 16, 16);
            Theme.ClassicPanel(g, up, raised: _repeatDir != -1 || _dragFrom >= 0);
            Theme.ClassicPanel(g, dn, raised: _repeatDir != 1 || _dragFrom >= 0);
            int cx = bar.X + 7;
            for (int i = 0; i < 4; i++) g.FillRectangle(Brushes.Black, cx - i, up.Y + 6 + i, 1 + 2 * i, 1);
            for (int i = 0; i < 4; i++) g.FillRectangle(Brushes.Black, cx - 3 + i, dn.Y + 6 + i, 7 - 2 * i, 1);
            Theme.ClassicPanel(g, thumb, raised: true);
            return;
        }
        // Mixtape's modern bar: a 4 px pill centred in the 10 px gutter, brighter under the pointer
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        int w = _barHover || _dragFrom >= 0 ? 6 : 4;
        var pill = new RectangleF(thumb.X + (thumb.Width - w) / 2f, thumb.Y + 2, w, Math.Max(8, thumb.Height - 4));
        using var path = Theme.RoundedRect(pill, w / 2f);
        using var b = new SolidBrush(Color.FromArgb(_barHover || _dragFrom >= 0 ? 110 : 60, 255, 255, 255));
        g.FillPath(b, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_overflow || e.Button != MouseButtons.Left || !BarRect.Contains(e.Location)) return;
        if (Theme.Classic && e.Y < BarRect.Y + 16) { StartRepeat(-1); return; }
        if (Theme.Classic && e.Y >= BarRect.Bottom - 16) { StartRepeat(1); return; }
        var t = ThumbRect;
        if (t.Contains(e.Location)) { _dragFrom = e.Y; _dragOffset = _offset; Capture = true; Invalidate(BarRect); return; }
        Step(e.Y < t.Y ? -ViewH : ViewH);   // the trough pages
    }

    private void StartRepeat(int dir)
    {
        _repeatDir = dir;
        Step(dir * 24);
        _repeat.Interval = 400;
        _repeat.Start();
        Capture = true;
        Invalidate(BarRect);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = _overflow && BarRect.Contains(e.Location);
        if (over != _barHover) { _barHover = over; Invalidate(BarRect); }
        if (_dragFrom < 0) return;
        int free = Math.Max(1, TrackLen - ThumbRect.Height);
        int to = _dragOffset + (int)((long)(e.Y - _dragFrom) * MaxOffset / free);
        Step(to - _offset);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _repeat.Stop();
        _repeatDir = 0;
        _dragFrom = -1;
        Capture = false;
        Invalidate(BarRect);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_barHover) { _barHover = false; Invalidate(BarRect); }
    }

    /// <summary>The wheel, from anywhere over the list: rows pass it up (DefWindowProc forwards an
    /// unhandled WM_MOUSEWHEEL to the parent), so it arrives here.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_overflow) Step(-Math.Sign(e.Delta) * 48);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _repeat.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// A slider row's caption or value (and the COLOR note). Modern: drawn in its ForeColor, or Faint while
/// <see cref="Blocked"/>. Classic, as 1995 drew static text: black, a caption followed by ':', and a
/// blocked or disabled one EMBOSSED (a white ghost down-right under grey) like the disabled push buttons
/// beside it - WinForms' own disabled Label is just flat grey. Blocked keeps the label enabled, so its
/// tooltip still says why.
/// </summary>
public sealed class InlineLabel : Label
{
    private bool _blocked;
    public bool Caption { get; init; }
    public bool Blocked { get => _blocked; set { if (_blocked == value) return; _blocked = value; Invalidate(); } }

    public InlineLabel()
    {
        AutoSize = false;
        BackColor = Color.Transparent;
        UseMnemonic = false;
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        string text = Caption && Theme.Classic && Text.Length > 0 ? Text + ":" : Text;
        if (text.Length == 0) return;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        flags |= TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
            ? TextFormatFlags.Right : TextFormatFlags.Left;
        var r = ClientRectangle;
        bool dim = _blocked || !Enabled;
        if (Theme.Classic)
        {
            if (dim)
            {
                var ghost = r; ghost.Offset(1, 1);
                CrispText.DrawText(g, text, Font, ghost, Theme.FaceHi, flags);
                CrispText.DrawText(g, text, Font, r, Theme.FaceShadow, flags);
            }
            else CrispText.DrawText(g, text, Font, r, Color.Black, flags);
            return;
        }
        CrispText.DrawText(g, text, Font, r, dim ? Theme.Faint : ForeColor, flags);
    }
}
