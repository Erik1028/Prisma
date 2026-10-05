using System.Text;

namespace RGBCommander;

internal static class Program
{
    private const string MutexName = "RGBCommander_SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        // The skin FIRST: it decides what the palette, the radii and the typeface even are, and
        // MainForm's field initialisers read those while constructing controls. Every visual test
        // harness below inherits it too, so a Classic screenshot needs no extra plumbing.
        Theme.SetSkin(ResolveClassicSkin());

        if (args.Contains("--test"))
        {
            RunHeadlessTest();
            return;
        }
        if (args.Contains("--test-mouse"))
        {
            RunMouseDiagnostic();
            return;
        }
        if (args.Contains("--test-gpu"))
        {
            RunGpuDiagnostic();
            return;
        }
        if (args.Contains("--test-gpu-native"))
        {
            // Read-only ADL/I2C probe of the native Sapphire GPU driver -> adl-diag.txt
            // next to the exe. Writes nothing to the card; safe to run, but stop the main
            // instance first so two ADL contexts don't read 0x28 at once.
            AdlDiag.Run();
            return;
        }
        if (args.Contains("--test-gpu-sensors"))
        {
            // Read-only probe of the ADL Overdrive8 + PMLog sensor surface (temps, fan,
            // clocks, power, load, fan-control caps) -> gpu-sensors.txt next to the exe.
            // Writes nothing to the card. Feeds the GPU Guardian design.
            GpuSensorDiag.Run();
            return;
        }
        if (args.Contains("--test-skin"))
        {
            // Design harness for both looks: the real controls, no mutex, no hardware.
            SkinPreview.Run();
            return;
        }
        if (args.Contains("--test-picker"))
        {
            // Visual harness for the color picker dialog; runs alongside the main
            // instance (no mutex), so the dialog can be screenshotted in isolation.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new ColorPickerDialog(Color.FromArgb(0, 200, 170)) { StartPosition = FormStartPosition.CenterScreen });
            return;
        }
        if (args.Contains("--test-about"))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            var info = new AboutInfo
            {
                Version = "2.0.0",
                BuildLine = "Built 2026-06-13  ·  .NET 8.0.27  ·  Windows 26200",
                Effect = "Rainbow wave", CurrentColor = Color.FromArgb(0, 200, 170), ColorHex = "#00C8AA",
                DevicesSummary = "6 of 7 lit", TotalLeds = 248, FrameRate = 25,
                RenderMode = "1 firmware · 5 streamed",
                StatusText = "Controlling 7 devices", Tuning = "55% bright · 37% speed",
                ProfileLine = "Custom  ·  3 saved", Uptime = "4h 12m",
            };
            info.Devices.Add(new AboutDevice("Gigabyte IT5702", "USB", "64 zones · Gigabyte USB", Color.FromArgb(255, 60, 0), true));
            info.Devices.Add(new AboutDevice("ENE DRAM", "RAM", "5 zones · OpenRGB", Color.FromArgb(0, 200, 170), true));
            info.Devices.Add(new AboutDevice("ASUS ROG STRIX B", "MB", "5 zones · OpenRGB", Color.FromArgb(0, 200, 170), true));
            info.Devices.Add(new AboutDevice("Sapphire Radeon RX 9060 XT", "GPU", "1 zone · OpenRGB", Color.FromArgb(0, 200, 170), true));
            info.Devices.Add(new AboutDevice("G502 X PLUS", "MOUSE", "1 zone · Logitech HID++", Color.FromArgb(120, 120, 130), false));
            info.Backends.Add(new AboutPill("OpenRGB SDK v4", Color.FromArgb(70, 200, 120)));
            info.Backends.Add(new AboutPill("Logitech HID++", Theme.Accent));
            info.Backends.Add(new AboutPill("Gigabyte Fusion", Theme.Accent));
            var popup = new AboutPopup(info) { StartPosition = FormStartPosition.CenterScreen, CloseOnDeactivate = false, TopMost = true };
            popup.Shown += (_, _) => popup.Activate();
            Application.Run(popup);
            return;
        }
        if (args.Contains("--test-menu"))
        {
            // Visual harness for the themed right-click menu design (no main instance).
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            ToolStripManager.Renderer = new ThemedMenuRenderer();
            var f = new Form { Text = "MenuPreview", ClientSize = new Size(440, 440), BackColor = Theme.Bg, StartPosition = FormStartPosition.CenterScreen };
            f.Shown += (_, _) =>
            {
                f.Activate();
                var m = Menus.Build();
                m.AutoClose = false; // keep it open for the screenshot even without focus
                m.Items.Add(Menus.Header("Sapphire Radeon RX 9060 XT"));
                m.Items.Add(Menus.Item("Turn off", "", (_, _) => { }));
                m.Items.Add(new ToolStripSeparator());
                var sc = new ToolStripMenuItem("Set colour…") { Image = Menus.Dot(Color.FromArgb(0, 200, 170)) };
                m.Items.Add(sc);
                m.Items.Add(Menus.Item("Use as colour target", null, (_, _) => { }, isChecked: true));
                m.Items.Add(Menus.Item("Identify (flash)", "", (_, _) => { }));
                m.Items.Add(new ToolStripSeparator());
                var fx = Menus.Submenu("Effect", "");
                fx.DropDownItems.Add(Menus.Item("Follow global effect", null, (_, _) => { }, isChecked: true));
                fx.DropDownItems.Add(new ToolStripSeparator());
                fx.DropDownItems.Add(Menus.Item("Rainbow wave", null, (_, _) => { }));
                m.Items.Add(fx);
                m.Items.Add(Menus.Item("Reset to global", "", (_, _) => { }));
                m.Items.Add(Menus.Header("1 zone  ·  OpenRGB"));
                m.Show(f, new Point(24, 18));
            };
            Application.Run(f);
            return;
        }
        if (args.Contains("--test-settings"))
        {
            // Visual harness for the Settings dialog, optionally opened on a given tab
            // index (--test-settings 3 = Commands). Runs alongside the main instance.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            int tab = 0;
            int idx = Array.IndexOf(args, "--test-settings");
            if (idx >= 0 && idx + 1 < args.Length) int.TryParse(args[idx + 1], out tab);
            var dlg = new SettingsForm(AppSettings.Load(), () => { }) { StartPosition = FormStartPosition.CenterScreen };
            dlg.OpenTab(tab);
            Application.Run(dlg);
            return;
        }

        // Single instance. Extra launches forward their command (e.g. a Stream Deck
        // button running "Prisma.exe --effect rainbow") to the running instance and
        // exit; a bare launch just surfaces the window.
        using var instanceLock = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            var forward = args.Where(a => a != "--minimized").ToArray();
            // a duplicate autostart (--minimized, nothing else) should stay silent
            if (forward.Length == 0 && args.Contains("--minimized")) return;
            string command = forward.Length > 0
                ? string.Join(RemoteControl.Sep, forward)
                : "--show";
            RemoteControl.Send(command);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var form = new MainForm(startMinimized: args.Contains("--minimized"));
        _ = form.Handle; // force handle creation so an early command's BeginInvoke has a target
        RemoteControl.StartServer(cmd =>
        {
            if (!form.IsHandleCreated) return;
            try { form.BeginInvoke(() => form.HandleRemoteCommand(cmd)); } catch { }
        });

        Application.Run(form);
    }

    /// <summary>
    /// GPU diagnostic: dumps the Sapphire device's mode table, switches it Off for
    /// 3 seconds (a visible change if hardware writes work), then static red, then
    /// re-reads the device state. Output: gpu-diag.txt next to the exe.
    /// </summary>
    /// <summary>Which skin to start in. PRISMA_CLASSIC=1|0 overrides the saved setting, which is what
    /// the screenshot harnesses use so neither look needs the app's own settings touched.</summary>
    private static bool ResolveClassicSkin()
    {
        string? env = Environment.GetEnvironmentVariable("PRISMA_CLASSIC");
        if (env == "1") return true;
        if (env == "0") return false;
        try { return AppSettings.Load().ClassicSkin; } catch { return false; }
    }

    private static void RunGpuDiagnostic()
    {
        var log = new StringBuilder();
        try
        {
            using var client = new OpenRgbClient();
            client.Connect();
            var gpu = client.Devices.FirstOrDefault(d => d.Name.Contains("Sapphire", StringComparison.OrdinalIgnoreCase));
            if (gpu == null)
            {
                log.AppendLine("No Sapphire device found. Devices:");
                foreach (var d in client.Devices) log.AppendLine("  " + d.Name);
            }
            else
            {
                log.AppendLine($"{gpu.Name}: index {gpu.Index}, {gpu.LedCount} LEDs, active mode {gpu.ActiveMode}");
                foreach (var m in gpu.Modes)
                    log.AppendLine($"  mode {m.Index}: '{m.Name}' flags=0x{m.Flags:X4} raw[{m.Raw.Length}]={BitConverter.ToString(m.Raw)}");

                var rainbow = gpu.Modes.FirstOrDefault(m => m.Name.Contains("Rainbow"));
                if (rainbow != null)
                {
                    client.UpdateMode(gpu.Index, rainbow);
                    log.AppendLine("STEP 0: sent hardware mode 'Rainbow Wave' - GPU should run a rainbow for 4 seconds");
                    Thread.Sleep(4000);
                }

                var off = gpu.Modes.FirstOrDefault(m => m.Name == "Off");
                if (off != null)
                {
                    client.UpdateMode(gpu.Index, off);
                    log.AppendLine("STEP 1: sent mode 'Off' - GPU lighting should be dark for 3 seconds");
                    Thread.Sleep(3000);
                }

                var direct = gpu.Modes.FirstOrDefault(m => (m.Flags & OpenRgbMode.FlagPerLedColor) != 0);
                if (direct == null)
                {
                    log.AppendLine("STEP 2 SKIPPED: no per-LED-color mode found");
                }
                else
                {
                    client.SetCustomMode(gpu.Index);
                    client.UpdateMode(gpu.Index, direct);
                    var colors = new uint[gpu.LedCount];
                    Array.Fill(colors, OpenRgbClient.Color(255, 0, 0));
                    client.UpdateLeds(gpu.Index, colors);
                    log.AppendLine($"STEP 2: sent mode '{direct.Name}' (index {direct.Index}) + red - GPU should be solid red now");
                    Thread.Sleep(3000);
                }

                client.RefreshDevices();
                var after = client.Devices.FirstOrDefault(d => d.Index == gpu.Index);
                log.AppendLine($"STEP 3: device re-read - active mode now {after?.ActiveMode} (expected {direct?.Index})");
            }
            log.AppendLine("RESULT: OK");
        }
        catch (Exception ex)
        {
            log.AppendLine("RESULT: FAILED - " + ex);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gpu-diag.txt"), log.ToString());
    }

    /// <summary>
    /// Deep diagnostic for the Logitech HID++ mouse driver: dumps raw discovery
    /// data, then holds red / green / blue for 3 seconds each, logging the raw
    /// response to every color command. Output: mouse-diag.txt next to the exe.
    /// </summary>
    private static void RunMouseDiagnostic()
    {
        var log = new StringBuilder();
        try
        {
            var mouse = LogitechHidppDevice.TryCreate(HidNative.Enumerate());
            if (mouse == null)
            {
                log.AppendLine("No Logitech HID++ device found");
            }
            else
            {
                using (mouse)
                {
                    log.AppendLine(mouse.DumpDiagnostics());
                    var sweep = new (string Name, byte R, byte G, byte B)[]
                    {
                        ("red", 255, 0, 0), ("green", 0, 255, 0), ("blue", 0, 0, 255)
                    };
                    foreach (var (name, r, g, b) in sweep)
                    {
                        for (byte led = 0; led < mouse.LedCount; led++)
                        {
                            string resp = mouse.SetColorVerbose(led, mouse.FixedEffectIndexOf(led), r, g, b);
                            log.AppendLine($"set zone {led} {name}: response {resp}");
                        }
                        Thread.Sleep(3000);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("FAILED: " + ex);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "mouse-diag.txt"), log.ToString());
    }

    /// <summary>
    /// Detects all devices (direct HID drivers + OpenRGB), then sweeps everything
    /// through red / green / blue / teal. Results go to test-log.txt next to the exe.
    /// </summary>
    private static void RunHeadlessTest()
    {
        var log = new StringBuilder();
        using var openRgb = new OpenRgbClient();
        try
        {
            var result = DeviceManager.DetectAll(openRgb, tryConnectOpenRgb: true);
            log.AppendLine($"OpenRGB connected: {result.OpenRgbConnected}");
            foreach (var note in result.Notes) log.AppendLine("NOTE: " + note);
            log.AppendLine($"Devices found: {result.Devices.Count}");
            foreach (var d in result.Devices)
                log.AppendLine($"  {d.Name} - {d.LedCount} zones via {d.Source}");

            foreach (var d in result.Devices)
            {
                try { d.Prepare(); }
                catch (Exception ex) { log.AppendLine($"PREPARE FAILED ({d.Name}): {ex.Message}"); }
            }

            var sweep = new (string Name, int R, int G, int B)[]
            {
                ("red", 255, 0, 0), ("green", 0, 255, 0), ("blue", 0, 0, 255), ("teal", 0, 200, 170)
            };
            foreach (var (name, r, g, b) in sweep)
            {
                foreach (var d in result.Devices)
                {
                    if (d.LedCount == 0) continue;
                    var colors = new uint[d.LedCount];
                    Array.Fill(colors, OpenRgbClient.Color(r, g, b));
                    try
                    {
                        d.SetColors(colors);
                        log.AppendLine($"Set {d.Name} to {name}");
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"FAILED to set {d.Name} to {name}: {ex.Message}");
                    }
                }
                Thread.Sleep(700);
            }
            log.AppendLine("RESULT: OK");

            foreach (var d in result.Devices)
                if (d is IDisposable disp) disp.Dispose();
        }
        catch (Exception ex)
        {
            log.AppendLine("RESULT: FAILED - " + ex);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-log.txt"), log.ToString());
    }
}
