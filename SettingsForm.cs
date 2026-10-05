using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RGBCommander;

public class SettingsForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
    }

    // Settings apply live, so dismissing with the keyboard loses nothing. An open
    // hour-picker menu swallows ESC first, which is the right precedence.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData is Keys.Escape or Keys.Enter)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RGBCommander";

    private readonly AppSettings _settings;
    private readonly Action _applyChanges;
    private readonly List<RoundedButton> _fpsButtons = new();
    private readonly List<(RoundedButton Btn, int Value, Func<int> Current)> _groupButtons = new();
    private readonly List<NavButton> _tabButtons = new();
    private readonly List<Panel> _tabPanels = new();
    private readonly List<Label> _sectionLabels = new();
    private int _activeTab;
    private Label _maintenanceStatus = null!;
    private Label _conflictStatus = null!;
    private RoundedButton _conflictFixBtn = null!;

    public SettingsForm(AppSettings settings, Action applyChanges)
    {
        _settings = settings;
        _applyChanges = applyChanges;

        Text = "Settings";
        ClientSize = new Size(652, 536);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont(9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildUi();
    }

    private void BuildUi()
    {
        // Windows Settings-style layout: category nav rail on the left, one content
        // panel on the right per category.
        var titles = new[] { "⚙   General", "🌙   Auto-off", "✨   Effects", "🎛   Commands", "🛡   Reliability", "🛠   System" };
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var btn = new NavButton(titles[i]) { Left = 16, Top = 18 + i * 46, Width = 184, Height = 40 };
            btn.Click += (_, _) => SelectTab(index);
            _tabButtons.Add(btn);
            Controls.Add(btn);

            var panel = new Panel { Left = 224, Top = 18, Width = 412, Height = 460, BackColor = Theme.Bg };
            _tabPanels.Add(panel);
            Controls.Add(panel);
        }

        BuildGeneralTab(_tabPanels[0]);
        BuildAutoOffTab(_tabPanels[1]);
        BuildEffectsTab(_tabPanels[2]);
        BuildCommandsTab(_tabPanels[3]);
        BuildReliabilityTab(_tabPanels[4]);
        BuildSystemTab(_tabPanels[5]);
        UpdateGroupButtons();
        SelectTab(0);

        var closeBtn = new RoundedButton { Text = "Close", Width = 120, Height = 38, Top = ClientSize.Height - 52 };
        closeBtn.Left = 224 + 412 - closeBtn.Width; // bottom-right of the content column
        closeBtn.Click += (_, _) => Close();
        Controls.Add(closeBtn);
    }

    /// <summary>Left-rail navigation item: left-aligned label, hover wash, and an
    /// accent pill on the left edge when selected — the Windows Settings look.</summary>
    private sealed class NavButton : Control
    {
        public bool Selected;
        private bool _hover;

        public NavButton(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(10f);
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Selected || _hover)
                using (var bg = new SolidBrush(Selected ? Theme.RowBg : Theme.RowHover))
                    g.FillPath(bg, Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8));
            if (Selected)
                using (var pill = new SolidBrush(Theme.Accent))
                    g.FillPath(pill, Theme.RoundedRect(new Rectangle(0, Height / 2 - 9, 4, 18), 2));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(16, 0, Width - 20, Height),
                Selected ? Color.White : Theme.TextCol,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------------------ tabs

    private void BuildGeneralTab(Panel p)
    {
        p.Controls.Add(SectionLabel("GENERAL", 0));
        var startupRow = new ToggleRow("Start with Windows", IsStartupEnabled()) { Left = 0, Top = 28, Width = 412 };
        var minimizedRow = new ToggleRow("Start minimized to tray", _settings.StartMinimized) { Left = 0, Top = 76, Width = 412 };
        var trayRow = new ToggleRow("Close button hides to tray (effects keep running)", _settings.CloseToTray) { Left = 0, Top = 124, Width = 412 };
        var notifyRow = new ToggleRow("Notify when a device connects or disconnects", _settings.NotifyDeviceChanges) { Left = 0, Top = 172, Width = 412 };

        startupRow.CheckedChanged += (_, _) => { WriteStartupEntry(startupRow.Checked, minimizedRow.Checked); };
        minimizedRow.CheckedChanged += (_, _) =>
        {
            _settings.StartMinimized = minimizedRow.Checked;
            if (startupRow.Checked) WriteStartupEntry(true, minimizedRow.Checked);
            _applyChanges();
        };
        trayRow.CheckedChanged += (_, _) => { _settings.CloseToTray = trayRow.Checked; _applyChanges(); };
        notifyRow.CheckedChanged += (_, _) => { _settings.NotifyDeviceChanges = notifyRow.Checked; _applyChanges(); };
        p.Controls.Add(startupRow);
        p.Controls.Add(minimizedRow);
        p.Controls.Add(trayRow);
        p.Controls.Add(notifyRow);

        p.Controls.Add(SectionLabel("APPEARANCE", 224));
        var accents = new[]
        {
            Color.FromArgb(0, 200, 170), Color.FromArgb(0, 140, 255), Color.FromArgb(150, 80, 255),
            Color.FromArgb(255, 70, 160), Color.FromArgb(70, 200, 90), Color.FromArgb(255, 140, 0),
            Color.FromArgb(240, 70, 70)
        };
        for (int i = 0; i < accents.Length; i++)
        {
            var c = accents[i];
            var sw = new RoundedButton
            {
                Left = i * 46, Top = 252, Width = 38, Height = 38, CornerRadius = 10,
                FillColor = c, HoverFillColor = c, Tag = c
            };
            sw.Click += (s, _) => ApplyAccent((Color)((RoundedButton)s!).Tag!);
            p.Controls.Add(sw);
        }
        var accentCustom = new RoundedButton { Text = "Custom...", Left = 7 * 46, Top = 252, Width = 90, Height = 38 };
        accentCustom.Click += (_, _) =>
        {
            using var dlg = new ColorPickerDialog(Color.FromArgb(_settings.AccentArgb));
            if (dlg.ShowDialog(this) == DialogResult.OK) ApplyAccent(dlg.SelectedColor);
        };
        p.Controls.Add(accentCustom);

        var opacityRow = new OptionRow("Window opacity") { Left = 0, Top = 300, Width = 412 };
        var opacityValue = new Label
        {
            Text = $"{Math.Clamp(_settings.WindowOpacity, 75, 100)}%", Left = 360, Top = 11, Width = 44,
            ForeColor = Theme.Subtle, Font = Theme.UiFont(9f), BackColor = Theme.RowBg,
            TextAlign = ContentAlignment.MiddleRight
        };
        var opacityBar = new SleekSlider
        {
            Left = 160, Top = 7, Width = 194, Height = 28, Minimum = 75, Maximum = 100,
            Value = Math.Clamp(_settings.WindowOpacity, 75, 100), BackColor = Theme.RowBg
        };
        opacityBar.ValueChanged += (_, _) =>
        {
            _settings.WindowOpacity = opacityBar.Value;
            opacityValue.Text = $"{opacityBar.Value}%";
            _applyChanges();
        };
        opacityRow.Controls.Add(opacityBar);
        opacityRow.Controls.Add(opacityValue);
        p.Controls.Add(opacityRow);

        var glowRow = new OptionRow("Background glow") { Left = 0, Top = 348, Width = 412 };
        var glowValue = new Label
        {
            Text = $"{Math.Clamp(_settings.BackdropGlow, 0, 100)}%", Left = 360, Top = 11, Width = 44,
            ForeColor = Theme.Subtle, Font = Theme.UiFont(9f), BackColor = Theme.RowBg,
            TextAlign = ContentAlignment.MiddleRight
        };
        var glowBar = new SleekSlider
        {
            Left = 160, Top = 7, Width = 194, Height = 28, Minimum = 0, Maximum = 100,
            Value = Math.Clamp(_settings.BackdropGlow, 0, 100), BackColor = Theme.RowBg
        };
        glowBar.ValueChanged += (_, _) =>
        {
            _settings.BackdropGlow = glowBar.Value;
            glowValue.Text = $"{glowBar.Value}%";
            _applyChanges();
        };
        glowRow.Controls.Add(glowBar);
        glowRow.Controls.Add(glowValue);
        p.Controls.Add(glowRow);

        var glassRow = new ToggleRow("Liquid glass (acrylic blur behind the window)", _settings.GlassEffect) { Left = 0, Top = 396, Width = 412 };
        opacityRow.Enabled = !_settings.GlassEffect; // glass suspends the opacity slider
        glassRow.CheckedChanged += (_, _) =>
        {
            _settings.GlassEffect = glassRow.Checked;
            opacityRow.Enabled = !glassRow.Checked;
            _applyChanges();
        };
        p.Controls.Add(glassRow);

        var lookRow = new ToggleRow("Windows 95 look (square, grey, 3D edges)", _settings.ClassicSkin)
        { Left = 0, Top = 438, Width = 412 };
        lookRow.CheckedChanged += (_, _) =>
        {
            _settings.ClassicSkin = lookRow.Checked;
            // Applies in place - no restart, so no device re-detect. See MainForm.ApplySkinChange.
            _applyChanges();
            glassRow.Enabled = opacityRow.Enabled = glowRow.Enabled = !lookRow.Checked;
        };
        glassRow.Enabled = opacityRow.Enabled = glowRow.Enabled = !lookRow.Checked;
        p.Controls.Add(lookRow);
    }

    private void BuildAutoOffTab(Panel p)
    {
        // macOS-style: every line is a card row; sub-options sit in indented rows
        // under their toggle and grey out while it is off.
        p.Controls.Add(SectionLabel("TURN LIGHTS OFF", 0));
        var lockRow = new ToggleRow("When Windows locks", _settings.OffWhenLocked) { Left = 0, Top = 28, Width = 412 };
        var sleepRow = new ToggleRow("When the PC sleeps", _settings.OffWhenSleeps) { Left = 0, Top = 76, Width = 412 };
        var displayRow = new ToggleRow("When the display turns off", _settings.OffWhenDisplaySleeps) { Left = 0, Top = 124, Width = 412 };
        var exitRow = new ToggleRow("When the app exits", _settings.OffOnExit) { Left = 0, Top = 172, Width = 412 };
        var nightRow = new ToggleRow("At night", _settings.NightOffEnabled) { Left = 0, Top = 220, Width = 412 };
        lockRow.CheckedChanged += (_, _) => { _settings.OffWhenLocked = lockRow.Checked; _applyChanges(); };
        sleepRow.CheckedChanged += (_, _) => { _settings.OffWhenSleeps = sleepRow.Checked; _applyChanges(); };
        displayRow.CheckedChanged += (_, _) => { _settings.OffWhenDisplaySleeps = displayRow.Checked; _applyChanges(); };
        exitRow.CheckedChanged += (_, _) => { _settings.OffOnExit = exitRow.Checked; _applyChanges(); };
        p.Controls.Add(lockRow);
        p.Controls.Add(sleepRow);
        p.Controls.Add(displayRow);
        p.Controls.Add(exitRow);
        p.Controls.Add(nightRow);

        // TextInset 16: these sub-rows sit at Left=24, so 24+16 lines their label ink up
        // exactly with the parent toggles' captions at 0+40 (they were 4px out before).
        var hoursRow = new OptionRow("Night hours") { Left = 24, Top = 268, Width = 388, Enabled = nightRow.Checked, TextInset = 16 };
        HourPick(hoursRow, _settings.NightStartHour, 190, 6, h => { _settings.NightStartHour = h; _applyChanges(); });
        hoursRow.Controls.Add(new Label
        {
            // centred in the gap between the two 88px pills (190..278 and 292..380 → gap 278..292)
            Text = "–", Left = 278, Top = 6, Width = 14, Height = 30, ForeColor = Theme.Subtle,
            TextAlign = ContentAlignment.MiddleCenter, Font = Theme.UiFont(9.5f), BackColor = Theme.RowBg
        });
        HourPick(hoursRow, _settings.NightEndHour, 292, 6, h => { _settings.NightEndHour = h; _applyChanges(); });
        p.Controls.Add(hoursRow);

        var actionRow = new OptionRow("Action") { Left = 24, Top = 316, Width = 388, Enabled = nightRow.Checked, TextInset = 16 };
        GroupButton(actionRow, "Turn off", 0, 200, 6, 86, () => _settings.NightDimInstead ? 1 : 0, v => _settings.NightDimInstead = v == 1, 30);
        GroupButton(actionRow, "Dim", 1, 294, 6, 86, () => _settings.NightDimInstead ? 1 : 0, v => _settings.NightDimInstead = v == 1, 30);
        p.Controls.Add(actionRow);

        nightRow.CheckedChanged += (_, _) =>
        {
            _settings.NightOffEnabled = nightRow.Checked;
            hoursRow.Enabled = actionRow.Enabled = nightRow.Checked;
            _applyChanges();
        };

        var idleRow = new ToggleRow("When away from the PC", _settings.IdleOffEnabled) { Left = 0, Top = 364, Width = 412 };
        p.Controls.Add(idleRow);
        var idleSub = new OptionRow("After") { Left = 24, Top = 412, Width = 388, Enabled = idleRow.Checked, TextInset = 16 };
        GroupButton(idleSub, "5 min", 5, 154, 6, 70, () => _settings.IdleOffMinutes, v => _settings.IdleOffMinutes = v, 30);
        GroupButton(idleSub, "10 min", 10, 232, 6, 70, () => _settings.IdleOffMinutes, v => _settings.IdleOffMinutes = v, 30);
        GroupButton(idleSub, "20 min", 20, 310, 6, 70, () => _settings.IdleOffMinutes, v => _settings.IdleOffMinutes = v, 30);
        p.Controls.Add(idleSub);
        idleRow.CheckedChanged += (_, _) =>
        {
            _settings.IdleOffEnabled = idleRow.Checked;
            idleSub.Enabled = idleRow.Checked;
            _applyChanges();
        };
    }

    private void BuildEffectsTab(Panel p)
    {
        p.Controls.Add(SectionLabel("EFFECT SMOOTHNESS", 0));
        var fpsOptions = new (string Label, int Fps)[]
        {
            ("Smooth (25 fps)", 25), ("Balanced (15 fps)", 15), ("Gentle (10 fps)", 10)
        };
        for (int i = 0; i < fpsOptions.Length; i++)
        {
            var btn = new RoundedButton
            {
                Text = fpsOptions[i].Label, Tag = fpsOptions[i].Fps,
                Left = i * 140, Top = 28, Width = 132, Height = 38
            };
            btn.Click += (s, _) =>
            {
                _settings.FrameRate = (int)((RoundedButton)s!).Tag!;
                UpdateFpsButtons();
                _applyChanges();
            };
            _fpsButtons.Add(btn);
            p.Controls.Add(btn);
        }
        UpdateFpsButtons();

        // Section ladder: header sits 12px under the previous section's content, content
        // 28px under its header — the WAVE header used to float a full extra spacing unit low.
        p.Controls.Add(SectionLabel("WAVE & GRADIENT", 78));
        var ringRow = new OptionRow("LEDs per fan ring") { Left = 0, Top = 106, Width = 412 };
        GroupButton(ringRow, "8", 8, 214, 6, 58, () => _settings.FanRingLeds, v => _settings.FanRingLeds = v, 30);
        GroupButton(ringRow, "12", 12, 280, 6, 58, () => _settings.FanRingLeds, v => _settings.FanRingLeds = v, 30);
        GroupButton(ringRow, "16", 16, 346, 6, 58, () => _settings.FanRingLeds, v => _settings.FanRingLeds = v, 30);
        p.Controls.Add(ringRow);

        var reverseRow = new ToggleRow("Reverse direction (wave and gradient)", _settings.WaveReverse) { Left = 0, Top = 154, Width = 412 };
        reverseRow.CheckedChanged += (_, _) => { _settings.WaveReverse = reverseRow.Checked; _applyChanges(); };
        p.Controls.Add(reverseRow);

        p.Controls.Add(SectionLabel("SCREEN SYNC", 208));
        var vividRow = new OptionRow("Colour boost") { Left = 0, Top = 236, Width = 412 };
        GroupButton(vividRow, "Natural", 0, 130, 6, 86, () => _settings.AmbientVividness, v => _settings.AmbientVividness = v, 30);
        GroupButton(vividRow, "Boosted", 1, 224, 6, 86, () => _settings.AmbientVividness, v => _settings.AmbientVividness = v, 30);
        GroupButton(vividRow, "Vivid", 2, 318, 6, 86, () => _settings.AmbientVividness, v => _settings.AmbientVividness = v, 30);
        p.Controls.Add(vividRow);

        p.Controls.Add(SectionLabel("GPU LIGHT BAR", 290));
        var externalRow = new ToggleRow("Follow ARGB cable (Sapphire External Control)", _settings.GpuExternalControl) { Left = 0, Top = 318, Width = 412 };
        externalRow.CheckedChanged += (_, _) => { _settings.GpuExternalControl = externalRow.Checked; _applyChanges(); };
        p.Controls.Add(externalRow);
    }

    private void BuildCommandsTab(Panel p)
    {
        p.Controls.Add(SectionLabel("STREAM DECK / COMMAND LINE", 0));
        p.Controls.Add(new Label
        {
            Left = 2, Top = 28, Width = 410, Height = 38, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f),
            Text = "Drive Prisma from a Stream Deck, script or shortcut. Each launch forwards its " +
                   "command to the running app and exits — no second window."
        });

        p.Controls.Add(new Label
        {
            Left = 2, Top = 72, Width = 410, Height = 196, ForeColor = Theme.TextCol,
            Font = new Font("Consolas", 9f),
            Text =
                "--toggle             lights on / off\n" +
                "--off                all lights off\n" +
                "--effect <name>      static · rainbow · cycle\n" +
                "                     breathing · strobe · gradient\n" +
                "                     comet · twinkle · screen\n" +
                "                     fire · music\n" +
                "--color #RRGGBB      set colour (e.g. #FF3300)\n" +
                "--brightness N       set brightness (0-100, or +10 / -10)\n" +
                "--speed N            set effect speed (or +10 / -10)\n" +
                "--profile \"<name>\"   apply a saved profile\n" +
                "--next-profile       cycle to the next profile\n" +
                "--show               open the window"
        });

        // Widths sized to their captions (~15px breathing per side) so the three labels get
        // uniform insets — the old fixed 134/150/112 gave "Create shortcuts" only ~6px.
        var pathBtn = new RoundedButton { Text = "Copy app path", Left = 0, Top = 280, Width = 115, Height = 36 };
        var argsBtn = new RoundedButton { Text = "Copy current as args", Left = 123, Top = 280, Width = 153, Height = 36 };
        var lnkBtn = new RoundedButton { Text = "Create shortcuts", Left = 284, Top = 280, Width = 128, Height = 36 };
        var done = new Label { Left = 2, Top = 326, Width = 410, Height = 40, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f) };

        pathBtn.Click += (_, _) =>
        {
            TrySetClipboard(Environment.ProcessPath ?? Application.ExecutablePath);
            done.ForeColor = Theme.Subtle;
            done.Text = "App path copied. In Stream Deck: action \"Open\", paste as the program, then add the arguments.";
        };
        argsBtn.Click += (_, _) =>
        {
            var c = Color.FromArgb(_settings.ColorArgb);
            string a = $"--effect {_settings.Effect.ToLowerInvariant()} --color #{c.R:X2}{c.G:X2}{c.B:X2}";
            TrySetClipboard(a);
            done.ForeColor = Theme.Subtle;
            done.Text = "Arguments for the current look copied:\n" + a;
        };
        lnkBtn.Click += (_, _) =>
        {
            try
            {
                string dir = CreateCommandShortcuts();
                done.ForeColor = Theme.Subtle;
                done.Text = "Shortcuts created in \"Prisma Commands\" on your Desktop — drag them onto Stream Deck buttons.";
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                done.ForeColor = Theme.ErrorCol;
                done.Text = "Could not create shortcuts: " + ex.Message;
            }
        };
        p.Controls.Add(pathBtn);
        p.Controls.Add(argsBtn);
        p.Controls.Add(lnkBtn);
        p.Controls.Add(done);
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    /// <summary>Drops a "Prisma Commands" folder of .lnk files on the Desktop — one per
    /// common command and one per saved profile — that a Stream Deck button can launch.</summary>
    private string CreateCommandShortcuts()
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Prisma Commands");
        Directory.CreateDirectory(dir);

        var items = new List<(string Name, string Args)>
        {
            ("Toggle lights", "--toggle"),
            ("All off", "--off"),
            ("Rainbow wave", "--effect rainbow"),
            ("Static color", "--effect static"),
            ("Screen sync", "--effect screen"),
            ("Music", "--effect music"),
            ("Brighter", "--brightness +10"),
            ("Dimmer", "--brightness -10"),
            ("Faster", "--speed +10"),
            ("Slower", "--speed -10"),
            ("Next profile", "--next-profile"),
            ("Open Prisma", "--show"),
        };
        foreach (var p in _settings.Profiles)
            items.Add(($"Profile - {p.Name}", $"--profile \"{p.Name.Replace("\"", "")}\"")); // strip quotes: they'd break the token

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host unavailable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, args) in items)
        {
            string safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            if (safe.Length == 0) safe = "command";
            string baseName = safe;
            for (int n = 2; !used.Add(safe); n++) safe = $"{baseName} ({n})"; // de-collide names
            dynamic sc = shell.CreateShortcut(Path.Combine(dir, $"Prisma - {safe}.lnk"));
            sc.TargetPath = exe;
            sc.Arguments = args;
            sc.Description = "Prisma: " + args;
            sc.IconLocation = exe + ",0";
            sc.Save();
        }
        return dir;
    }

    private void BuildReliabilityTab(Panel p)
    {
        p.Controls.Add(SectionLabel("RGB SOFTWARE CONFLICTS", 0));
        p.Controls.Add(new Label
        {
            Left = 2, Top = 28, Width = 410, Height = 50, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f),
            Text = "Other RGB apps (Aura, SignalRGB, iCUE, MSI, Razer…) fight Prisma for the same " +
                   "bus — the usual cause of RAM/board/GPU flicker, a jumping device count, or the GPU " +
                   "going dark. Prisma can stop and disable them in one step."
        });

        var warnRow = new ToggleRow("Warn when conflicting RGB software is running", _settings.WarnOnRgbConflicts) { Left = 0, Top = 78, Width = 412 };
        warnRow.CheckedChanged += (_, _) => { _settings.WarnOnRgbConflicts = warnRow.Checked; _applyChanges(); };
        p.Controls.Add(warnRow);

        // Peripheral apps like Logitech G HUB own the mouse's HID++ channel (its RGB + the
        // user's macros/bindings); let Prisma stay out of its way entirely with one switch.
        var mouseRow = new ToggleRow("Leave the Logitech mouse to G HUB", _settings.IgnoreLogitechMouse) { Left = 0, Top = 122, Width = 412 };
        mouseRow.CheckedChanged += (_, _) => { _settings.IgnoreLogitechMouse = mouseRow.Checked; _applyChanges(); };
        p.Controls.Add(mouseRow);

        _conflictStatus = new Label
        {
            // 2 lines max (worst case: "Running now: …\nThese can cause flicker…") — the old
            // 52px + fixed button Top left a ~43px dead band under the 1-line no-conflict text.
            Left = 2, Top = 170, Width = 410, Height = 36, ForeColor = Theme.TextCol, Font = Theme.UiFont(9f)
        };
        p.Controls.Add(_conflictStatus);

        _conflictFixBtn = new RoundedButton { Text = "Disable conflicting software", Left = 0, Top = 214, Width = 220, Height = 38 };
        _conflictFixBtn.Click += (_, _) =>
        {
            var active = RgbConflicts.Scan();
            if (active.Count == 0) { RefreshConflictStatus(); return; }
            _conflictFixBtn.Enabled = false;
            bool ok = RgbConflicts.Disable(active, out string msg);
            _conflictStatus.ForeColor = ok ? Theme.Subtle : Theme.ErrorCol;
            _conflictStatus.Text = msg + (ok ? "\nThen restart OpenRGB (System tab) and Rescan in Prisma." : "");
            _conflictFixBtn.Enabled = true;
        };
        p.Controls.Add(_conflictFixBtn);

        var rescanBtn = new RoundedButton { Text = "Re-scan", Left = 232, Top = 214, Width = 110, Height = 38 };
        rescanBtn.Click += (_, _) => RefreshConflictStatus();
        p.Controls.Add(rescanBtn);

        RefreshConflictStatus();

        p.Controls.Add(SectionLabel("GPU LIGHTING RECOVERY", 276));
        p.Controls.Add(new Label
        {
            // 3 wrapped lines at this width/font — 46px clipped the last line mid-sentence
            Left = 2, Top = 302, Width = 410, Height = 62, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f),
            Text = "If the GPU light bar goes dark and won't come back (its I2C controller wedged), " +
                   "only a sleep/wake or reboot re-inits it — an OpenRGB restart or Rescan won't. " +
                   "This sleeps the PC now; wake it and the bar returns."
        });
        var sleepBtn = new RoundedButton { Text = "Sleep PC to recover GPU", Left = 0, Top = 372, Width = 220, Height = 38 };
        sleepBtn.Click += (_, _) => { try { SetSuspendState(false, false, false); } catch { } };
        p.Controls.Add(sleepBtn);
    }

    /// <summary>Re-scans for running RGB contenders and repaints the conflicts status line +
    /// fix button enablement.</summary>
    private void RefreshConflictStatus()
    {
        var active = RgbConflicts.Scan();
        if (active.Count == 0)
        {
            _conflictStatus.ForeColor = Theme.Subtle;
            _conflictStatus.Text = "No conflicting RGB software is running.  ✓";
            _conflictFixBtn.Enabled = false;
        }
        else
        {
            _conflictStatus.ForeColor = Theme.ErrorCol;
            _conflictStatus.Text = "Running now: " + string.Join(", ", active.Select(a => a.Contender.Name)) +
                                   ".\nThese can cause flicker or a wedged GPU.";
            _conflictFixBtn.Enabled = true;
        }
    }

    private void BuildSystemTab(Panel p)
    {
        p.Controls.Add(SectionLabel("MAINTENANCE", 0));
        var restartBtn = new RoundedButton { Text = "Restart OpenRGB server", Left = 0, Top = 28, Width = 200, Height = 38 };
        restartBtn.Click += async (_, _) =>
        {
            // A real restart: END the running server first, then RUN it, so it
            // re-detects hardware from scratch. The GPU's RGB controller sits on the
            // I2C/SMBus and is sometimes missed at the server's original startup (bus
            // busy, or contention with a vendor app like Sapphire TRIXX); a fresh
            // detection brings it back. A plain /run while a server is already running
            // just spawns duplicate instances that fight over the bus and fix nothing.
            restartBtn.Enabled = false;
            _maintenanceStatus.Text = "Restarting OpenRGB server (re-detecting hardware)...";
            try
            {
                await Task.Run(() =>
                {
                    RunSchtasks("/end /tn \"OpenRGB Server\"");
                    Thread.Sleep(800); // let the old instance exit before relaunching
                    // schtasks /end reports SUCCESS yet routinely leaves the elevated
                    // OpenRGB process alive (it only ends the *task instance*), so the
                    // /run below would stack a duplicate server that fights over the
                    // SMBus. Kill survivors for real; they run elevated, so this pops
                    // one UAC prompt.
                    if (Process.GetProcessesByName("OpenRGB").Length > 0)
                    {
                        var kill = new ProcessStartInfo("taskkill", "/F /IM OpenRGB.exe")
                        {
                            UseShellExecute = true, Verb = "runas",
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        try { Process.Start(kill)?.WaitForExit(15000); } catch { } // user may cancel UAC
                        Thread.Sleep(500);
                    }
                    RunSchtasks("/run /tn \"OpenRGB Server\"");
                });
                _maintenanceStatus.Text = "OpenRGB server restarted - wait ~5s for its scan, then press Rescan devices.";
            }
            catch (Exception ex)
            {
                _maintenanceStatus.Text = "Could not restart the OpenRGB task: " + ex.Message;
            }
            finally { restartBtn.Enabled = true; }
        };
        p.Controls.Add(restartBtn);

        var folderBtn = new RoundedButton { Text = "Open settings folder", Left = 212, Top = 28, Width = 200, Height = 38 };
        folderBtn.Click += (_, _) =>
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBCommander");
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
        };
        p.Controls.Add(folderBtn);

        var logBtn = new RoundedButton { Text = "Open debug log", Left = 0, Top = 74, Width = 200, Height = 38 };
        logBtn.Click += (_, _) =>
        {
            string log = Path.Combine(AppContext.BaseDirectory, "rgbc-debug.log");
            try { if (File.Exists(log)) Process.Start(new ProcessStartInfo(log) { UseShellExecute = true }); } catch { }
        };
        p.Controls.Add(logBtn);

        _maintenanceStatus = new Label
        {
            Left = 2, Top = 120, Width = 410, Height = 30,
            ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f)
        };
        p.Controls.Add(_maintenanceStatus);

        p.Controls.Add(SectionLabel("ABOUT", 162));
        string version = Application.ProductVersion.Split('+')[0]; // strip SDK source-link suffix
        DateTime built;
        // Environment.ProcessPath also works in single-file publishes, where
        // Assembly.Location is an empty string.
        try { built = File.GetLastWriteTime(Environment.ProcessPath!); }
        catch { built = DateTime.Now; }
        p.Controls.Add(new Label
        {
            Left = 2, Top = 190, Width = 410, Height = 24, ForeColor = Theme.TextCol,
            Font = Theme.UiFont(10.5f, FontStyle.Bold),
            Text = $"Prisma {version}"
        });
        p.Controls.Add(new Label
        {
            // "(addressable)" dropped: it pushed the drivers line past the label width, wrapping
            // to a one-word orphan and shoving this block into the SHORTCUTS header below.
            // 6 lines at ~16.3px leading — 100px clears the descenders (92 shaved the last line)
            Left = 2, Top = 216, Width = 410, Height = 100, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f),
            Text = $"Unified control for all your RGB devices.\n" +
                   $"Built {built:yyyy-MM-dd HH:mm}  ·  .NET {Environment.Version}  ·  Windows {Environment.OSVersion.Version.Build}\n\n" +
                   "Direct drivers: Logitech HID++ 2.0  ·  Gigabyte RGB Fusion 2 USB\n" +
                   "Network backend: OpenRGB SDK protocol v4  ·  Audio: WASAPI loopback\n" +
                   "13 effects, per-device assignment, profiles with game triggers."
        });

        p.Controls.Add(SectionLabel("SHORTCUTS", 330));
        var hotkeyRow = new ToggleRow("Global hotkeys (work from any app)", _settings.GlobalHotkeys) { Left = 0, Top = 358, Width = 412 };
        hotkeyRow.CheckedChanged += (_, _) => { _settings.GlobalHotkeys = hotkeyRow.Checked; _applyChanges(); };
        p.Controls.Add(hotkeyRow);
        p.Controls.Add(new Label
        {
            Left = 2, Top = 404, Width = 410, Height = 18, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f),
            Text = "Ctrl+Alt+L lights on/off  ·  Ctrl+Alt+P next profile  ·  Ctrl+Alt+R rescan devices"
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Opens a specific tab by index (used by the --test-settings harness).</summary>
    public void OpenTab(int index) => SelectTab(Math.Clamp(index, 0, _tabPanels.Count - 1));

    private void SelectTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabPanels.Count; i++) _tabPanels[i].Visible = i == index;
        UpdateTabButtons();
    }

    private void UpdateTabButtons()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            _tabButtons[i].Selected = i == _activeTab;
            _tabButtons[i].Invalidate();
        }
    }

    private Label SectionLabel(string text, int y)
    {
        var label = new Label
        {
            Text = text, Left = 0, Top = y, AutoSize = true,
            // UseMnemonic off: a literal '&' in a heading ("WAVE & GRADIENT") was being
            // swallowed as a mnemonic prefix, rendering as a double space.
            UseMnemonic = false,
            ForeColor = Theme.Accent, Font = Theme.UiFont(9f, FontStyle.Bold)
        };
        _sectionLabels.Add(label);
        return label;
    }

    /// <summary>Themed hour picker: a pill button opening a dark menu. (The stock ComboBox
    /// keeps native light-theme chrome — bright border + ButtonFace drop button — that no
    /// FlatStyle/owner-draw combination fully suppresses; it read as a foreign control.)</summary>
    private static RoundedButton HourPick(Control parent, int selectedHour, int x, int y, Action<int> apply)
    {
        var btn = new RoundedButton
        {
            Left = x, Top = y, Width = 88, Height = 30,
            Text = $"{Math.Clamp(selectedHour, 0, 23):00}:00  ▾"
        };
        btn.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip();
            for (int h = 0; h < 24; h++)
            {
                int hh = h;
                var it = new ToolStripMenuItem($"{hh:00}:00") { Checked = btn.Text.StartsWith($"{hh:00}:") };
                it.Click += (_, _) => { btn.Text = $"{hh:00}:00  ▾"; apply(hh); };
                menu.Items.Add(it);
            }
            Theme.StyleMenu(menu);
            menu.Show(btn, new Point(0, btn.Height + 2));
        };
        parent.Controls.Add(btn);
        return btn;
    }

    /// <summary>A button belonging to a mutually-exclusive value group (like the fps row);
    /// selection highlight follows the current settings value.</summary>
    private void GroupButton(Control parent, string text, int value, int x, int y, int w, Func<int> current, Action<int> apply, int h = 38)
    {
        var btn = new RoundedButton { Text = text, Left = x, Top = y, Width = w, Height = h };
        btn.Click += (_, _) => { apply(value); UpdateGroupButtons(); _applyChanges(); };
        _groupButtons.Add((btn, value, current));
        parent.Controls.Add(btn);
    }

    private void UpdateGroupButtons()
    {
        foreach (var (btn, value, current) in _groupButtons)
        {
            bool selected = current() == value;
            btn.FillColor = selected ? Theme.AccentDim : Theme.RowBg;
            btn.HoverFillColor = selected ? Theme.AccentDim : Theme.RowHover;
            btn.ForeColor = selected ? Color.White : Theme.TextCol;
            btn.BorderColor = selected ? Theme.Accent : Theme.Border;
            btn.Invalidate();
        }
    }

    private void ApplyAccent(Color c)
    {
        _settings.AccentArgb = c.ToArgb();
        Theme.SetAccent(c);
        UpdateFpsButtons();   // recolor the selected fps button with the new accent
        UpdateGroupButtons();
        UpdateTabButtons();
        foreach (var label in _sectionLabels) label.ForeColor = Theme.Accent;
        Invalidate(true);     // repaint this dialog
        _applyChanges();      // save + repaint the main window
    }

    private void UpdateFpsButtons()
    {
        foreach (var btn in _fpsButtons)
        {
            bool selected = (int)btn.Tag! == _settings.FrameRate;
            btn.FillColor = selected ? Theme.AccentDim : Theme.RowBg;
            btn.HoverFillColor = selected ? Theme.AccentDim : Theme.RowHover;
            btn.ForeColor = selected ? Color.White : Theme.TextCol;
            btn.BorderColor = selected ? Theme.Accent : Theme.Border;
            btn.Invalidate();
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) != null;
        }
        catch { return false; }
    }

    private static void RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks", arguments) { CreateNoWindow = true, UseShellExecute = false };
        Process.Start(psi)?.WaitForExit(5000);
    }

    private static void WriteStartupEntry(bool enabled, bool minimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (enabled)
                key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"{(minimized ? " --minimized" : "")}");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
