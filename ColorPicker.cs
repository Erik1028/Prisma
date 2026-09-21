using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RGBCommander;

/// <summary>Hue ring with an inscribed saturation/value square — the modern wheel
/// picker look — replacing the stock Windows ColorDialog.</summary>
public class ColorWheel : Control
{
    private Bitmap? _ring;     // cached, depends on control size only
    private Bitmap? _svBox;    // cached, depends on size + current hue
    private int _svHue = -1;
    private bool _dragRing, _dragSv;

    private double _hue;       // 0-360
    private double _sat = 1;   // 0-1
    private double _val = 1;   // 0-1

    /// <summary>Raised for user interaction only, not for programmatic sets.</summary>
    public event EventHandler? ColorChanged;

    public ColorWheel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    public Color SelectedColor
    {
        get => Theme.HsvToColor(_hue, _sat, _val);
        set
        {
            double r = value.R / 255.0, g = value.G / 255.0, b = value.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            _val = max;
            _sat = max <= 0 ? 0 : (max - min) / max;
            if (max > min) _hue = value.GetHue(); // greys keep the previous hue
            Invalidate();
        }
    }

    // ---- geometry: everything derives from the square inscribed in the control ----
    private int WheelSize => Math.Min(Width, Height);
    private float OuterR => WheelSize / 2f - 2;
    private float RingW => Math.Max(16, OuterR * 0.17f);
    private float InnerR => OuterR - RingW;

    private Rectangle SvRect
    {
        get
        {
            int side = (int)((InnerR - 7) * 1.41421f);
            return new Rectangle(Width / 2 - side / 2, Height / 2 - side / 2, side, side);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _ring?.Dispose(); _ring = null;
        _svBox?.Dispose(); _svBox = null;
        _svHue = -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _ring ??= RenderRing(WheelSize, InnerR, OuterR);
        var sv = SvRect;
        if (_svBox == null || _svHue != (int)_hue)
        {
            _svBox?.Dispose();
            _svBox = RenderSv(sv.Width, (int)_hue);
            _svHue = (int)_hue;
        }

        g.DrawImageUnscaled(_ring, (Width - WheelSize) / 2, (Height - WheelSize) / 2);
        g.DrawImageUnscaled(_svBox, sv.X, sv.Y);
        using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, sv.X - 1, sv.Y - 1, sv.Width + 1, sv.Height + 1);

        // hue indicator: a hue-filled dot riding the middle of the ring
        float mid = (OuterR + InnerR) / 2;
        float ax = Width / 2f + mid * (float)Math.Cos(_hue * Math.PI / 180);
        float ay = Height / 2f - mid * (float)Math.Sin(_hue * Math.PI / 180);
        DrawDot(g, ax, ay, 7.5f, Theme.HsvToColor(_hue, 1, 1));

        // sat/val indicator
        float sx = sv.X + (float)(_sat * (sv.Width - 1));
        float sy = sv.Y + (float)((1 - _val) * (sv.Height - 1));
        DrawDot(g, sx, sy, 6f, SelectedColor);
    }

    private static void DrawDot(Graphics g, float x, float y, float r, Color fill)
    {
        using var b = new SolidBrush(fill);
        g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        using var w = new Pen(Color.White, 2f);
        g.DrawEllipse(w, x - r, y - r, r * 2, r * 2);
        using var d = new Pen(Color.FromArgb(120, 0, 0, 0));
        g.DrawEllipse(d, x - r - 1, y - r - 1, r * 2 + 2, r * 2 + 2);
    }

    // ---- interaction ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        float dx = e.X - Width / 2f, dy = e.Y - Height / 2f;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        var sv = SvRect;
        if (r >= InnerR - 2 && r <= OuterR + 4)
        {
            _dragRing = true;
            UpdateRing(e.Location);
        }
        else if (e.X >= sv.Left - 4 && e.X <= sv.Right + 4 && e.Y >= sv.Top - 4 && e.Y <= sv.Bottom + 4)
        {
            _dragSv = true;
            UpdateSv(e.Location);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragRing) UpdateRing(e.Location);
        else if (_dragSv) UpdateSv(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragRing = _dragSv = false;
    }

    private void UpdateRing(Point p)
    {
        _hue = (Math.Atan2(-(p.Y - Height / 2.0), p.X - Width / 2.0) * 180 / Math.PI + 360) % 360;
        Invalidate();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSv(Point p)
    {
        var sv = SvRect;
        _sat = Math.Clamp((p.X - sv.X) / (double)(sv.Width - 1), 0, 1);
        _val = 1 - Math.Clamp((p.Y - sv.Y) / (double)(sv.Height - 1), 0, 1);
        Invalidate();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- bitmap rendering (managed pixel buffers, no unsafe) ----

    private static Bitmap RenderRing(int size, float ri, float ro)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var px = new int[size * size];
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                if (r < ri - 1 || r > ro + 1) continue;
                float edge = Math.Min(r - (ri - 1), ro + 1 - r); // 1px anti-aliased rims
                int alpha = (int)(255 * Math.Clamp(edge, 0f, 1f));
                double hue = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
                var col = Theme.HsvToColor(hue, 1, 1);
                px[y * size + x] = alpha << 24 | col.R << 16 | col.G << 8 | col.B;
            }
        }
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    private static Bitmap RenderSv(int side, int hue)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        var px = new int[side * side];
        for (int y = 0; y < side; y++)
        {
            double val = 1 - y / (side - 1.0);
            for (int x = 0; x < side; x++)
            {
                double sat = x / (side - 1.0);
                var col = Theme.HsvToColor(hue, sat, val);
                px[y * side + x] = unchecked((int)0xFF000000) | col.R << 16 | col.G << 8 | col.B;
            }
        }
        var data = bmp.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }
}

/// <summary>Dark-themed color picker dialog: wheel, live preview, hex entry and a
/// quick-swatch row. Drop-in replacement for ColorDialog across the app.</summary>
public class ColorPickerDialog : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly ColorWheel _wheel;
    private readonly RoundedButton _preview;
    private readonly TextBox _hex;
    private readonly Label _rgb;
    private bool _syncing;

    public Color SelectedColor => _wheel.SelectedColor;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
    }

    public ColorPickerDialog(Color initial)
    {
        Text = "Pick a color";
        ClientSize = new Size(312, 446);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont(9.5f);

        _wheel = new ColorWheel { Left = 16, Top = 14, Width = 280, Height = 280, SelectedColor = initial };
        _wheel.ColorChanged += (_, _) => SyncFromWheel();
        Controls.Add(_wheel);

        _preview = new RoundedButton
        {
            Left = 16, Top = 306, Width = 62, Height = 32, CornerRadius = 8,
            Interactive = false, FillColor = initial, HoverFillColor = initial
        };
        Controls.Add(_preview);

        _hex = new TextBox
        {
            Left = 88, Top = 309, Width = 90, BackColor = Theme.RowBg, ForeColor = Theme.TextCol,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.UiFont(10f),
            TextAlign = HorizontalAlignment.Center
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true; // no ding
            ParseHex();
        };
        _hex.Leave += (_, _) => ParseHex();
        Controls.Add(_hex);

        _rgb = new Label
        {
            Left = 184, Top = 312, Width = 112, Height = 18, ForeColor = Theme.Subtle,
            Font = Theme.UiFont(8.5f), TextAlign = ContentAlignment.MiddleRight
        };
        Controls.Add(_rgb);

        var presets = new[]
        {
            Color.Black, Color.White, Color.FromArgb(255, 0, 0), Color.FromArgb(255, 140, 0),
            Color.FromArgb(255, 220, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 255),
            Color.FromArgb(0, 0, 255), Color.FromArgb(150, 80, 255), Color.FromArgb(255, 70, 160)
        };
        for (int i = 0; i < presets.Length; i++)
        {
            var c = presets[i];
            var sw = new RoundedButton
            {
                Left = 16 + i * 28, Top = 352, Width = 25, Height = 25, CornerRadius = 7,
                FillColor = c, HoverFillColor = c, Tag = c
            };
            sw.Click += (s, _) =>
            {
                _wheel.SelectedColor = (Color)((RoundedButton)s!).Tag!;
                SyncFromWheel();
            };
            Controls.Add(sw);
        }

        // [OK][Cancel] — the Windows order, matching the profile-save dialog
        var cancel = new RoundedButton { Text = "Cancel", Left = 210, Top = 396, Width = 86, Height = 34 };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var ok = new RoundedButton
        {
            Text = "OK", Left = 118, Top = 396, Width = 84, Height = 34,
            FillColor = Theme.AccentDim, HoverFillColor = Theme.AccentDim, BorderColor = Theme.Accent,
            ForeColor = Color.White
        };
        ok.Click += (_, _) => DialogResult = DialogResult.OK;
        Controls.Add(ok);

        SyncFromWheel();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (keyData == Keys.Enter && ActiveControl != _hex)
        {
            DialogResult = DialogResult.OK;
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private void SyncFromWheel()
    {
        var c = _wheel.SelectedColor;
        _preview.FillColor = c;
        _preview.HoverFillColor = c;
        _preview.Invalidate();
        _syncing = true;
        _hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _syncing = false;
        _rgb.Text = $"R {c.R}  G {c.G}  B {c.B}";
    }

    private void ParseHex()
    {
        if (_syncing) return;
        string s = _hex.Text.Trim().TrimStart('#');
        if (s.Length == 6 && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v))
        {
            _wheel.SelectedColor = Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            SyncFromWheel();
        }
        else
        {
            SyncFromWheel(); // invalid input snaps back to the current color
        }
    }
}
