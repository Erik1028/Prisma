using System.Text.Json;

namespace RGBCommander;

/// <summary>A saved lighting setup: effect, colors, sliders and device selection,
/// applied in one click from the Profiles menu (header button or tray).</summary>
public class LightingProfile
{
    public string Name { get; set; } = "";
    public string Effect { get; set; } = "Static";
    public int ColorArgb { get; set; }
    public int GradientEndArgb { get; set; }
    public int Speed { get; set; } = 50;
    public int Brightness { get; set; } = 100;
    public Dictionary<string, int> DeviceColors { get; set; } = new();
    public Dictionary<string, string> DeviceEffects { get; set; } = new();
    public Dictionary<string, bool> Devices { get; set; } = new();
    /// <summary>Process name (e.g. "cs2") that auto-activates this profile while running.</summary>
    public string TriggerProcess { get; set; } = "";
}

/// <summary>Persisted app state, auto-saved on change and restored at startup.</summary>
public class AppSettings
{
    public string Effect { get; set; } = "Static";
    public int ColorArgb { get; set; } = Color.FromArgb(0, 200, 170).ToArgb();
    public int Speed { get; set; } = 50;
    public int Brightness { get; set; } = 100;
    public Dictionary<string, bool> Devices { get; set; } = new();
    /// <summary>Per-device base color overrides, keyed like <see cref="Devices"/>. A
    /// device without an entry follows the global <see cref="ColorArgb"/>.</summary>
    public Dictionary<string, int> DeviceColors { get; set; } = new();
    /// <summary>Per-device effect overrides (Effect enum names). A device without an
    /// entry follows the global effect.</summary>
    public Dictionary<string, string> DeviceEffects { get; set; } = new();
    /// <summary>User-saved custom colors shown as extra swatches (ARGB).</summary>
    public List<int> SavedColors { get; set; } = new();
    /// <summary>End color of the two-color Gradient effect (the start is ColorArgb).</summary>
    public int GradientEndArgb { get; set; } = Color.FromArgb(150, 80, 255).ToArgb();
    /// <summary>Saved lighting profiles, applied from the Profiles menus.</summary>
    public List<LightingProfile> Profiles { get; set; } = new();
    /// <summary>Accent color driving the whole UI theme.</summary>
    public int AccentArgb { get; set; } = Color.FromArgb(0, 200, 170).ToArgb();
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    /// <summary>Turn all lighting off while the Windows session is locked, restore on unlock.</summary>
    public bool OffWhenLocked { get; set; } = false;
    /// <summary>Turn all lighting off while the PC sleeps/suspends, restore on resume. Split
    /// out from <see cref="OffWhenLocked"/> so locking and sleeping can be controlled apart
    /// (e.g. stay lit while only locked, but go dark on real sleep).</summary>
    public bool OffWhenSleeps { get; set; } = false;
    /// <summary>Turn all lighting off when the app actually exits (not when hiding to tray).</summary>
    public bool OffOnExit { get; set; } = false;
    /// <summary>Show a tray notification when a device connects or disconnects.</summary>
    public bool NotifyDeviceChanges { get; set; } = true;
    /// <summary>Turn all lighting off during the configured night hours.</summary>
    public bool NightOffEnabled { get; set; } = false;
    public int NightStartHour { get; set; } = 22;
    public int NightEndHour { get; set; } = 7;
    /// <summary>At night, dim to a faint glow instead of turning fully off.</summary>
    public bool NightDimInstead { get; set; } = false;
    /// <summary>Turn all lighting off after this many minutes without keyboard/mouse input.</summary>
    public bool IdleOffEnabled { get; set; } = false;
    public int IdleOffMinutes { get; set; } = 10;
    /// <summary>LEDs per fan ring: how often the rainbow wave repeats along an ARGB chain.</summary>
    public int FanRingLeds { get; set; } = 12;
    /// <summary>Run the rainbow wave in the opposite direction.</summary>
    public bool WaveReverse { get; set; } = false;
    /// <summary>Screen sync colour boost: 0 natural, 1 boosted, 2 vivid.</summary>
    public int AmbientVividness { get; set; } = 1;
    /// <summary>GPU light bar follows the ARGB sync cable (Sapphire "External Control"
    /// mode) instead of being driven over I2C.</summary>
    public bool GpuExternalControl { get; set; } = false;
    /// <summary>Main window opacity percent (75-100).</summary>
    public int WindowOpacity { get; set; } = 100;
    /// <summary>Acrylic blur-behind ("liquid glass") on the main window.</summary>
    public bool GlassEffect { get; set; } = false;
    /// <summary>Keep the main window above other windows (header pin button).</summary>
    public bool AlwaysOnTop { get; set; } = false;
    /// <summary>Strength of the window's gradient backdrop, 0-100 (0 = flat dark).</summary>
    public int BackdropGlow { get; set; } = 100;
    /// <summary>System-wide hotkeys: Ctrl+Alt+L lights, Ctrl+Alt+P profile, Ctrl+Alt+R rescan.</summary>
    public bool GlobalHotkeys { get; set; } = true;
    /// <summary>Surface a warning when another RGB app (AURA, SignalRGB, iCUE, ...) is running
    /// and contending for the same SMBus/I2C — the usual cause of RAM/board/GPU flicker.</summary>
    public bool WarnOnRgbConflicts { get; set; } = true;
    /// <summary>Don't detect or manage the Logitech mouse — leave its RGB (and macros/bindings)
    /// to Logitech G HUB. Skips the HID++ probe entirely, so detection is instant and Prisma
    /// never contends with G HUB for the receiver's channel.</summary>
    public bool IgnoreLogitechMouse { get; set; } = false;
    /// <summary>Schema version, bumped when a migration must run once on an older settings
    /// file (see <see cref="Load"/>). 0 = pre-versioning (lock+sleep were one toggle).</summary>
    /// <summary>Settings ▸ Appearance ▸ Look: the Windows 95 skin (see Theme.SetSkin).</summary>
    public bool ClassicSkin { get; set; } = false;

    public int SettingsVersion { get; set; } = 0;
    /// <summary>Turn all lighting off while the display sleeps, restore when it wakes.</summary>
    public bool OffWhenDisplaySleeps { get; set; } = false;
    public int FrameRate { get; set; } = 25;
    public int WinX { get; set; } = -1;
    public int WinY { get; set; } = -1;
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool Maximized { get; set; }

    private static string SettingsPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGBCommander");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    /// <summary>Current settings schema version. Bump when adding a <see cref="Migrate"/> step.</summary>
    private const int CurrentVersion = 1;

    /// <summary>One-time fix-ups for settings written by an older build, applied on load.
    /// Idempotent — safe to re-run if a save didn't persist before the next launch.</summary>
    private void Migrate()
    {
        if (SettingsVersion < 1)
            // v0 had a single "when Windows locks or sleeps" toggle. Mirror it onto the new
            // sleep toggle so an upgrading user keeps the exact behaviour they had.
            OffWhenSleeps = OffWhenLocked;
        SettingsVersion = CurrentVersion;
    }

    public static AppSettings Load()
    {
        var s = LoadRaw();
        s.Migrate();
        return s;
    }

    private static AppSettings LoadRaw()
    {
        string path = SettingsPath();
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A corrupt/truncated file would otherwise silently reset every profile,
            // swatch and per-device colour. Preserve it as .bad so it isn't overwritten
            // by the next Save() and the data stays recoverable.
            try
            {
                string bad = path + ".bad";
                if (File.Exists(path) && !File.Exists(bad)) File.Move(path, bad);
            }
            catch { }
            DebugLog.Log("settings load failed (kept as .bad): " + ex.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            // Atomic: write a temp file, then swap it in, so a crash/power loss
            // mid-write can never truncate the live settings.json.
            string path = SettingsPath();
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch { }
    }
}
