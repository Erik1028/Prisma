using System.Drawing.Drawing2D;

namespace RGBCommander;

/// <summary>
/// Design harness for the two skins (<c>--test-skin</c>). Lays the real custom controls out on a plain
/// form — no single-instance mutex, no OpenRGB, no SMBus/I2C — so the look can be iterated on and
/// screenshotted while the live app keeps driving the hardware untouched. The "Switch look" button
/// exercises the runtime re-skin path (<see cref="Theme.SetSkin"/> + <see cref="Restyle.Apply"/>),
/// which is the same code the Settings toggle uses.
/// </summary>
internal static class SkinPreview
{
    private sealed class FakeDevice : IRgbDevice
    {
        public FakeDevice(string name, int leds, string source, string kind)
        { Name = name; LedCount = leds; Source = source; Kind = kind; }
        public string Name { get; }
        public int LedCount { get; }
        public string Source { get; }
        public string Kind { get; }
        public void SetColors(uint[] colors) { }
    }

    public static void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var form = new Form
        {
            Text = "Prisma — skin preview",
            ClientSize = new Size(760, 620),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Theme.Bg,
        };

        Build(form);

        form.Shown += (_, _) => form.Activate();
        Application.Run(form);
    }

    private static void Build(Form form)
    {
        form.Controls.Clear();
        form.BackColor = Theme.Bg;

        var title = new RainbowTitle { Left = 16, Top = 12, Width = 200, Height = 34 };
        form.Controls.Add(title);

        var look = new RoundedButton
        {
            Text = Theme.Classic ? "Switch look: Modern" : "Switch look: Windows 95",
            Left = 560, Top = 14, Width = 180, Height = 30,
        };
        look.Click += (_, _) =>
        {
            Theme.SetSkin(!Theme.Classic);
            Build(form);
            form.Invalidate(true);
        };
        form.Controls.Add(look);

        // ---- a card of buttons ------------------------------------------------------------------
        var card = new CardPanel { Title = "EFFECTS", Left = 16, Top = 58, Width = 360, Height = 150, CornerRadius = Theme.RadCard };
        form.Controls.Add(card);
        string[] names = { "Static color", "Rainbow wave", "Color cycle", "Breathing", "Strobe", "Gradient" };
        for (int i = 0; i < names.Length; i++)
        {
            var b = new RoundedButton
            {
                Text = names[i],
                Left = 14 + (i % 2) * 170,
                Top = 38 + (i / 2) * 36,
                Width = 160, Height = 30,
                CornerRadius = Theme.RadControl,
            };
            if (i == 0) { b.FillColor = Theme.AccentDim; b.BorderColor = Theme.Accent; }
            card.Controls.Add(b);
        }

        // ---- sliders ----------------------------------------------------------------------------
        var tune = new CardPanel { Title = "TUNING", Left = 392, Top = 58, Width = 352, Height = 150, CornerRadius = Theme.RadCard };
        form.Controls.Add(tune);
        var bright = new SleekSlider { Left = 14, Top = 44, Width = 320, Height = 22, Value = 60 };
        var hue = new SleekSlider { Left = 14, Top = 96, Width = 320, Height = 22, Value = 40, RainbowTrack = true };
        tune.Controls.Add(bright);
        tune.Controls.Add(hue);
        tune.Controls.Add(new Label
        {
            Text = "BRIGHTNESS", Left = 14, Top = 26, AutoSize = true,
            ForeColor = Theme.Subtle, BackColor = Color.Transparent, Font = Theme.UiFontShared(8f, FontStyle.Bold),
        });
        tune.Controls.Add(new Label
        {
            Text = "HUE", Left = 14, Top = 78, AutoSize = true,
            ForeColor = Theme.Subtle, BackColor = Color.Transparent, Font = Theme.UiFontShared(8f, FontStyle.Bold),
        });

        // ---- device rows + group ------------------------------------------------------------------
        var devCard = new CardPanel { Title = "DEVICES", Left = 16, Top = 222, Width = 360, Height = 250, CornerRadius = Theme.RadCard, Translucent = false };
        form.Controls.Add(devCard);

        var rows = new List<DeviceRow>();
        var defs = new (string Name, int Leds, string Src, string Kind, Color Col, bool On)[]
        {
            ("Gigabyte IT5702-GIGABYTE", 64, "Gigabyte USB", "USB", Color.FromArgb(255, 60, 0), true),
            ("AMD Radeon RX 9060 XT", 1, "OpenRGB", "GPU", Color.FromArgb(0, 200, 170), true),
            ("ASUS ROG STRIX B550-F", 5, "OpenRGB", "MB", Color.FromArgb(0, 200, 170), true),
            ("G502 X PLUS", 1, "Logitech HID++", "MOUSE", Color.FromArgb(120, 120, 130), false),
        };
        int y = 36;
        foreach (var d in defs)
        {
            var row = new DeviceRow(new FakeDevice(d.Name, d.Leds, d.Src, d.Kind), d.On)
            {
                Left = 10, Top = y, Width = 340, DisplayColor = d.Col,
            };
            devCard.Controls.Add(row);
            rows.Add(row);
            y += row.Height + 5;
        }

        var members = new List<DeviceRow>();
        for (int i = 0; i < 4; i++)
            members.Add(new DeviceRow(new FakeDevice("ENE DRAM", 5, "OpenRGB", "RAM"), true)
            { DisplayColor = Color.FromArgb(0, 200, 170) });
        var grp = new DeviceGroupRow(members) { Left = 10, Top = y, Width = 340 };
        devCard.Controls.Add(grp);

        // ---- live preview + options ----------------------------------------------------------------
        var right = new CardPanel { Title = "LIVE PREVIEW", Left = 392, Top = 222, Width = 352, Height = 250, CornerRadius = Theme.RadCard };
        form.Controls.Add(right);

        var preview = new LivePreview { Left = 14, Top = 36, Width = 320, Height = 60 };
        right.Controls.Add(preview);
        var strip = new uint[48];
        for (int i = 0; i < strip.Length; i++)
        {
            var c = Theme.HsvToColor(i * 360.0 / strip.Length);
            strip[i] = (uint)(c.R | (c.G << 8) | (c.B << 16));
        }
        preview.SetColors(strip);

        var toggle = new ToggleRow("Start with Windows", true) { Left = 14, Top = 108, Width = 320 };
        right.Controls.Add(toggle);
        var toggle2 = new ToggleRow("Close to tray", false) { Left = 14, Top = 150, Width = 320 };
        right.Controls.Add(toggle2);

        var opt = new OptionRow("Rescan devices") { Left = 14, Top = 196, Width = 320 };
        right.Controls.Add(opt);

        // ---- swatches ------------------------------------------------------------------------------
        var sw = new CardPanel { Title = "COLOR", Left = 16, Top = 486, Width = 728, Height = 110, CornerRadius = Theme.RadCard };
        form.Controls.Add(sw);
        Color[] cols =
        {
            Color.Red, Color.Lime, Color.Blue, Color.Orange, Color.Gold,
            Color.FromArgb(0, 200, 170), Color.Magenta, Color.HotPink, Color.White,
        };
        for (int i = 0; i < cols.Length; i++)
        {
            var b = new RoundedButton
            {
                Left = 14 + i * 62, Top = 38, Width = 54, Height = 30,
                CornerRadius = Theme.RadChip,
                FillColor = cols[i], HoverFillColor = cols[i], BorderColor = Theme.Border,
            };
            sw.Controls.Add(b);
        }
        var custom = new RoundedButton { Text = "Custom...", Left = 14, Top = 74, Width = 160, Height = 28, CornerRadius = Theme.RadControl };
        sw.Controls.Add(custom);

        Restyle.Apply(form);
    }
}
