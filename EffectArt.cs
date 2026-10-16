using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RGBCommander;

/// <summary>
/// Prisma's "cover art": one small static picture per effect, painted from the colours the effect would
/// use, at full value. Mixtape gets its colour from album art; a lighting app should get it from the
/// lights. The same painter fills the effect tiles, the deck tile, each device row's tile and each
/// profile row, so a glance tells you WHAT the lights are doing - a Rainbow device shows a spectrum, not
/// the stale picked green.
///
/// Static by design: nothing here is animated or per-frame (only the deck's live line is live), so it
/// costs nothing on the UI thread that also paces the device writes.
/// </summary>
internal static class EffectArt
{
    private static readonly Color[] Spectrum =
        { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };

    /// <summary>Paints the effect's picture into <paramref name="r"/>, as a rounded square.
    /// <paramref name="live"/> is the sampled screen colour for Screen sync (Empty when unknown).
    /// <paramref name="dim"/> 0..1 pulls the whole picture toward <paramref name="dimTo"/> (an unchecked
    /// device).</summary>
    public static void Paint(Graphics g, Rectangle r, MainForm.Effect fx, Color baseC, Color endC, int radius,
        Color live = default, double dim = 0, Color dimTo = default)
    {
        if (r.Width < 2 || r.Height < 2) return;
        var sm = g.SmoothingMode;
        var po = g.PixelOffsetMode;

        // The picture is painted offscreen and then FILLED through the rounded path: a clip region is
        // never anti-aliased, so clipping left stair-stepped corners beside the smooth swatches.
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb))
        {
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.SmoothingMode = Theme.ShapeMode;
                bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                PaintPicture(bg, new Rectangle(0, 0, r.Width, r.Height), fx, baseC, endC, live, dim, dimTo);
            }
            g.SmoothingMode = Theme.ShapeMode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // texel i lands exactly on pixel r.X + i
            using var tb = new TextureBrush(bmp, WrapMode.Clamp);
            tb.TranslateTransform(r.X, r.Y);
            using var shape = Theme.RoundedRect(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
            g.FillPath(tb, shape);
        }

        if (!Theme.Classic)
        {
            // a faint inner rim, so a dark picture still reads as a tile on a dark surface - centred on
            // the outermost pixel ring, so it is the same on all four sides
            using var rim = new Pen(Color.FromArgb(28, 255, 255, 255));
            using var path = Theme.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f),
                Math.Max(0f, radius - 0.5f));
            g.DrawPath(rim, path);
        }
        g.SmoothingMode = sm;
        g.PixelOffsetMode = po;
    }

    /// <summary>The colour a pattern is drawn in: the picked hue and saturation at (nearly) full value,
    /// and near-greys as light grey. A dark pick (a deep forest green) otherwise drew its comet, sparkles
    /// and bars dark-on-black, and the pictures stopped telling the effects apart. Static keeps the
    /// TRUE colour - it stands beside the #hex figure for the real LED output.</summary>
    private static Color Motif(Color c)
    {
        int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
        if (mx == 0 || (mx - mn) / (double)mx < 0.12) return Color.FromArgb(224, 224, 226);
        if (mx >= 230) return c;
        double k = 230.0 / mx;   // scaling every channel keeps the hue and the HSV saturation exactly
        return Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
    }

    private static void PaintPicture(Graphics g, Rectangle r, MainForm.Effect fx, Color baseC, Color endC,
        Color live, double dim, Color dimTo)
    {
        Color D(Color c) => dim <= 0 ? c : Theme.Blend(c, dimTo, dim);
        Color m = Motif(baseC);
        Color dark = D(Theme.Blend(m, Color.Black, 0.82));

        switch (fx)
        {
            case MainForm.Effect.Static:
                Fill(g, r, D(baseC));
                break;

            case MainForm.Effect.Breathing:
                using (var br = new LinearGradientBrush(Rectangle.Inflate(r, 1, 1), D(m), D(Theme.Blend(m, Color.Black, 0.72)), 90f))
                    g.FillRectangle(br, r);
                break;

            case MainForm.Effect.Strobe:
                Fill(g, r, D(m));
                using (var b = new SolidBrush(dark))
                {
                    g.FillRectangle(b, r.X + r.Width * 0.30f, r.Y, r.Width * 0.14f, r.Height);
                    g.FillRectangle(b, r.X + r.Width * 0.62f, r.Y, r.Width * 0.14f, r.Height);
                }
                break;

            case MainForm.Effect.Gradient:
                using (var br = new LinearGradientBrush(Rectangle.Inflate(r, 1, 1), D(m), D(Motif(endC)), 60f))
                    g.FillRectangle(br, r);
                break;

            case MainForm.Effect.RainbowWave:
                using (var br = new LinearGradientBrush(Rectangle.Inflate(r, 1, 1), Color.Red, Color.Red, 60f))
                {
                    br.InterpolationColors = new ColorBlend
                    {
                        Colors = Spectrum.Select(D).ToArray(),
                        Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f },
                    };
                    g.FillRectangle(br, r);
                }
                break;

            case MainForm.Effect.ColorCycle:
            {
                // four quarter-turns of the colour wheel, starting at the picked hue
                double h0 = baseC.GetSaturation() > 0.05 ? baseC.GetHue() : 0;
                int hw = r.Width / 2, hh = r.Height / 2;
                Fill(g, new Rectangle(r.X, r.Y, hw, hh), D(Theme.HsvToColor(h0)));
                Fill(g, new Rectangle(r.X + hw, r.Y, r.Width - hw, hh), D(Theme.HsvToColor((h0 + 90) % 360)));
                Fill(g, new Rectangle(r.X + hw, r.Y + hh, r.Width - hw, r.Height - hh), D(Theme.HsvToColor((h0 + 180) % 360)));
                Fill(g, new Rectangle(r.X, r.Y + hh, hw, r.Height - hh), D(Theme.HsvToColor((h0 + 270) % 360)));
                break;
            }

            case MainForm.Effect.Comet:
            {
                Fill(g, r, dark);
                // a streak fading in from the left to a bright head
                var streak = new RectangleF(r.X - r.Width * 0.1f, r.Y + r.Height * 0.40f, r.Width * 0.95f, r.Height * 0.20f);
                using (var br = new LinearGradientBrush(streak, Color.FromArgb(0, D(m)), D(m), 0f))
                    g.FillRectangle(br, streak);
                float hd = r.Height * 0.30f;
                using (var b = new SolidBrush(D(Theme.Blend(m, Color.White, 0.35))))
                    g.FillEllipse(b, r.X + r.Width * 0.80f - hd / 2, r.Y + r.Height * 0.5f - hd / 2, hd, hd);
                break;
            }

            case MainForm.Effect.Twinkle:
            {
                Fill(g, r, dark);
                var spots = new[] { (0.28f, 0.30f, 0.26f), (0.70f, 0.42f, 0.20f), (0.40f, 0.74f, 0.22f) };
                using var b = new SolidBrush(D(m));
                using var core = new SolidBrush(D(Theme.Blend(m, Color.White, 0.6)));
                foreach (var (x, y, s) in spots)
                {
                    float d = r.Width * s;
                    g.FillEllipse(b, r.X + r.Width * x - d / 2, r.Y + r.Height * y - d / 2, d, d);
                    g.FillEllipse(core, r.X + r.Width * x - d / 6, r.Y + r.Height * y - d / 6, d / 3, d / 3);
                }
                break;
            }

            case MainForm.Effect.Ambient:
            {
                // the screen's own colour when sampled; otherwise a "screen" wash of the picked colour
                var c = live.IsEmpty || live.A == 0 ? Theme.Blend(m, Color.Black, 0.25) : live;
                using (var br = new LinearGradientBrush(Rectangle.Inflate(r, 1, 1), D(Theme.Blend(c, Color.White, 0.18)),
                           D(Theme.Blend(c, Color.Black, 0.35)), 60f))
                    g.FillRectangle(br, r);
                break;
            }

            case MainForm.Effect.Surprise:
            {
                int hw = r.Width / 2, hh = r.Height / 2;
                Fill(g, new Rectangle(r.X, r.Y, hw, hh), D(Color.FromArgb(255, 70, 160)));
                Fill(g, new Rectangle(r.X + hw, r.Y, r.Width - hw, hh), D(Color.FromArgb(255, 160, 0)));
                Fill(g, new Rectangle(r.X + hw, r.Y + hh, r.Width - hw, r.Height - hh), D(Color.FromArgb(0, 200, 255)));
                Fill(g, new Rectangle(r.X, r.Y + hh, hw, r.Height - hh), D(Color.FromArgb(120, 230, 60)));
                break;
            }

            case MainForm.Effect.Fire:
                using (var br = new LinearGradientBrush(Rectangle.Inflate(r, 1, 1), Color.Red, Color.Yellow, 90f))
                {
                    br.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { D(Color.FromArgb(120, 10, 0)), D(Color.FromArgb(235, 50, 0)), D(Color.FromArgb(255, 150, 0)), D(Color.FromArgb(255, 230, 90)) },
                        Positions = new[] { 0f, 0.38f, 0.72f, 1f },
                    };
                    g.FillRectangle(br, r);
                }
                break;

            case MainForm.Effect.Music:
            {
                Fill(g, r, dark);
                using var b = new SolidBrush(D(m));
                float bw = r.Width * 0.18f, gap = r.Width * 0.09f;
                float x = r.X + (r.Width - (3 * bw + 2 * gap)) / 2f;
                foreach (var h in new[] { 0.45f, 0.80f, 0.60f })
                {
                    float bh = r.Height * h;
                    g.FillRectangle(b, x, r.Bottom - r.Height * 0.12f - bh, bw, bh);
                    x += bw + gap;
                }
                break;
            }

            default: // Off
                Fill(g, r, Theme.Classic ? Theme.FaceShadow : Color.FromArgb(49, 50, 53));
                break;
        }
    }

    private static void Fill(Graphics g, Rectangle r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }
}
