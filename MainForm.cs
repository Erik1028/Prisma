using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RGBCommander;

public class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Custom window chrome: the OS title bar is removed in WM_NCCALCSIZE while the native
    // resize frame, drop shadow, Aero-snap and maximise animations are kept.
    private const int WM_NCCALCSIZE = 0x0083, WM_NCLBUTTONDOWN = 0x00A1, HTCAPTION = 2;
    private const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS { public RECT Proposed, Before, Source; public IntPtr lppos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    private static Guid s_displayStateGuid = new("6FE69556-704A-47A0-8F24-C28D936FDA47"); // GUID_CONSOLE_DISPLAY_STATE
    private const int WM_HOTKEY = 0x0312;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2;
    private const int HK_LIGHTS = 1, HK_PROFILE = 2, HK_RESCAN = 3;

    protected override void OnHandleDestroyed(EventArgs e)
    {
        // a re-created handle (start minimised flips ShowInTaskbar) must re-register, or display-sleep
        // auto-off stays bound to the dead window
        if (_powerNotify != IntPtr.Zero) { UnregisterPowerSettingNotification(_powerNotify); _powerNotify = IntPtr.Zero; }
        base.OnHandleDestroyed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowChrome();
        if (_settings != null) ApplyHotkeys();
        if (_powerNotify == IntPtr.Zero)
            if (!Harness) _powerNotify = RegisterPowerSettingNotification(Handle, ref s_displayStateGuid, 0);
    }

    // The wallpaper, plus the soft shadows the floating cards cast on it (the cards never paint
    // outside their own bounds, so the form paints their shadows into the gaps).
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Backdrop.Paint(e.Graphics, this);
        if (_leftCol != null) SurfacePanel.PaintShadow(e.Graphics, _leftCol.Bounds);
        if (_rightCol != null) SurfacePanel.PaintShadow(e.Graphics, _rightCol.Bounds);
    }

    /// <summary>Per-skin DWM chrome, re-applied on every live skin switch (Prisma never restarts to
    /// switch): dark mode and Win11 rounded corners for Modern, square corners and no transitions for
    /// Classic, never the 1 px DWM border (the strip meets the window edge directly). Glass and the
    /// opacity slider are Modern-only.</summary>
    private void ApplyWindowChrome()
    {
        if (!IsHandleCreated) return;
        int dark = Theme.Classic ? 0 : 1;
        int corner = Theme.Classic ? 1 /*DONOTROUND*/ : 2 /*ROUND*/;
        int border = unchecked((int)0xFFFFFFFE);          // DWMWA_COLOR_NONE
        int noTransitions = Theme.Classic ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(Handle, 3, ref noTransitions, sizeof(int));
        }
        catch { }
        // NOT Opacity: this runs from OnHandleCreated, and a start-minimised window keeps Opacity 0
        // until Shown hides it - resetting it here flashed the window at every boot. Opacity survives
        // handle re-creation; the callers that change it set it themselves.
        if (_settings != null) Acrylic.Apply(this, _settings.GlassEffect && !Theme.Classic);
    }

    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    /// <summary>What a press on the FORM itself (no child took it) means: the top resize band, the
    /// caption, or plain client. A pure function of the point, so --hitmap can draw it.</summary>
    private int ClientHitCode(Point pt)
    {
        // the resize band is the skin's own top frame: 3 px of raised 95 frame, so the navy band below
        // it drags all the way up; 5 px in the modern look
        if (!IsZoomed(Handle) && pt.Y < (Theme.Classic ? 3 : 5))
            return pt.X < 16 ? HTTOPLEFT : pt.X >= ClientSize.Width - 16 ? HTTOPRIGHT : HTTOP;
        return _strip.DragRect.Contains(pt) ? HTCAPTION : HTCLIENT;
    }

    /// <summary>
    /// Harness only (<c>--test-main --hitmap file.png</c>): walks the client and paints what a press at
    /// each point would do, following the same HTTRANSPARENT chain Windows does (deck card -> strip ->
    /// form). Blue = drags the window (HTCAPTION), red = resizes (top band), green = a control takes it,
    /// grey = plain client.
    /// </summary>
    private void WriteHitMap(string path)
    {
        var size = ClientSize;
        // pure colour codes (DrawToBitmap would add a caption the window never has and shift everything)
        using var shot = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(shot);
        using var cap = new SolidBrush(Color.FromArgb(40, 110, 255));
        using var top = new SolidBrush(Color.FromArgb(255, 40, 40));
        using var ctl = new SolidBrush(Color.FromArgb(40, 200, 80));
        using var cli = new SolidBrush(Color.FromArgb(70, 70, 70));
        const int step = 2;
        for (int y = 0; y < size.Height; y += step)
            for (int x = 0; x < size.Width; x += step)
            {
                var p = new Point(x, y);
                // geometric descent in z-order (index 0 is topmost) - ChildWindowFromPointEx returns
                // nothing on the click-through ghost window the harness uses
                Control hit = this;
                var local = p;
                for (bool found = true; found;)
                {
                    found = false;
                    foreach (Control c in hit.Controls)
                    {
                        if (!c.Visible || !c.Bounds.Contains(local)) continue;
                        local = new Point(local.X - c.Left, local.Y - c.Top);
                        hit = c;
                        found = true;
                        break;
                    }
                }
                // the transparent chain: a deck point that does not take the mouse, and the strip itself
                if (hit is DeckCard dk && !dk.TakesMouse(local)) hit = dk.Parent ?? this;
                if (hit is TitleStrip) hit = this;
                Brush b = hit != this ? ctl : ClientHitCode(p) switch
                {
                    HTCAPTION => cap,
                    HTCLIENT => cli,
                    _ => top,
                };
                g.FillRectangle(b, x, y, step, step);
            }
        shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        DebugLog.Log("hitmap written: " + path);
    }
    private const int WM_NCHITTEST = 0x84, WM_CONTEXTMENU = 0x7B;
    private const int HTCLIENT = 1, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

    internal enum Effect { Static, RainbowWave, ColorCycle, Breathing, Strobe, Gradient, Comet, Twinkle, Ambient, Surprise, Fire, Music, Off }

    private readonly OpenRgbClient _openRgb = new();
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 40 }; // 25 fps
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _frameWasRunningBeforeSizeMove; // restore the frame loop after an interactive drag
    private readonly List<DeviceRow> _deviceRows = new();
    private readonly List<DeviceGroupRow> _deviceGroups = new(); // header rows for runs of identical devices (RAM sticks)
    private readonly HashSet<DeviceRow> _hwRows = new(); // devices running an onboard effect; not streamed
    private System.Windows.Forms.Timer? _gpuReassert;    // one-shot delayed GPU mode re-send
    private readonly AudioLevelMeter _audio = new();     // system-audio level for the Music effect
    private static float s_audioLevel;                   // sampled once per frame for BuildColorsCore
    private static float s_audioBass;                    // bass band, drives the on-beat punch
    private string? _autoProfileActive;                  // profile auto-applied by a running trigger process
    private LightingProfile? _preAutoSnapshot;           // lighting state to restore when the trigger exits
    private bool _autoOff;                               // lights off by night schedule / idle timeout
    private bool _nightDim;                              // night hours active in "dim" mode
    private bool _gpuExternalApplied;                    // last GPU external-control state written
    private bool _ignoreLogitechApplied;                 // last "leave mouse to G HUB" state applied
    private static int s_ringLeds = 12;                  // rainbow wave: LEDs per fan ring
    private static bool s_waveReverse;                   // wave & gradient direction
    private static double s_ambientBoost = 1.4;          // screen sync saturation boost factor
    private static Color s_gradientEnd = Color.FromArgb(150, 80, 255); // two-color gradient end
    private bool _detecting;
    private bool _openRgbEverConnected;
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 10000 };
    private readonly System.Windows.Forms.Timer _resumeReassert = new() { Interval = 3500 }; // one-shot: re-assert direct control after wake-from-sleep
    private readonly System.Windows.Forms.Timer _bootSettle = new() { Interval = 4000 };      // post-boot: re-assert once the SMBus settles (unstick a late RAM/board device)
    private int _bootSettlePasses;                                                            // remaining boot-settle re-asserts
    private readonly DateTime _launchedUtc = DateTime.UtcNow;                                 // for the "boot window" gate on the settle re-assert

    private ScrollList _deviceList = null!;                       // the DEVICES list (its own scroll bar)
    private FlowLayoutPanel _devicePanel = null!;                 // == _deviceList.Content: the rows
    private ScrollList _profileList = null!;                      // the PROFILES list
    private SurfacePanel _leftCol = null!, _rightCol = null!;   // the rail and content cards
    private TitleStrip _strip = null!;                            // the title bar (Chrome.cs)
    private DeckCard _deck = null!;                               // "now lighting" card in the strip
    private CaptionButton? _maxBtn;                               // maximise/restore (glyph follows the state)
    private Label _status = null!;                                // full status, rail footer (wraps)
    private BrandButton _title = null!;                           // logo + wordmark; opens About
    private AboutPopup? _about;               // the About panel it opens (null when closed)
    private SleekSlider _speedBar = null!;
    private SleekSlider _brightBar = null!;
    private SleekSlider _hueBar = null!;
    private InlineLabel _speedValue = null!;
    private InlineLabel _brightValue = null!;
    private InlineLabel _hueValue = null!;
    private bool _syncingHue;
    private RoundedButton _rescanButton = null!;
    private readonly List<RoundedButton> _effectButtons = new();
    private readonly List<RoundedButton> _swatchButtons = new();
    private readonly List<RoundedButton> _savedSwatchButtons = new();
    private const int SwatchCols = 10;          // unified swatch grid width (presets + saved)
    private Panel _swatchGrid = null!;                            // square colour wells, laid out by LayoutSwatches
    private RoundedButton _targetButton = null!;
    private RoundedButton _gradientBtn = null!;
    private RoundedButton _customBtn = null!, _saveBtn = null!;   // COLOR action row (rebuilt 2/3-col by effect)
    private Panel _actionRow = null!;                             // Custom / Save / Gradient end, natural widths
    private InlineLabel _speedLabel = null!, _brightLabel = null!, _hueLabel = null!; // inline slider captions
    private InlineLabel _autoColorNote = null!;                   // replaces the Applies-to chip for self-colouring effects
    private CardPanel _tuningCard = null!;                        // TUNING, docked at the content card's bottom
    private CardPanel _effectsCard = null!, _colorCard = null!, _stageCard = null!; // content sections
    private Panel _effectGrid = null!;                            // effect tiles, laid out by LayoutEffectTiles
    private PowerButton _lightsBtn = null!;                       // strip: lights on / off
    private FlatIconButton _screenBtn = null!, _pinBtn = null!;   // strip: screen sync, always on top
    private LivePreview _preview = null!;                         // the LIVE stage
    private CardPanel _devicesCard = null!, _profilesCard = null!; // rail sections
    private RoundedButton _profileAdd = null!;                    // "+" on the PROFILES caption: save current look
    private Label _profileHint = null!;                           // shown while there are no profiles
    private readonly List<ProfileRow> _profileRows = new();
    private string? _appliedProfile;                              // name of the profile whose look is showing
    private bool _gradientShown = true;                           // DESIRED visibility of Gradient end: Control.Visible
                                                                  // reads false while the form is hidden, so never compare it
    private int _applyingProfile;                                 // >0 while ApplyProfile drives SetColor/SelectEffect
    private Color _ambientNow;                                    // last sampled screen colour (Screen sync art)
    private int _ambientArtShown;                                 // the _ambientNow the tiles last painted
    private string? _preAutoApplied;                              // the applied-profile marker when a trigger took over
    private long _aboutClosedAt;                                  // re-click-to-close guard for the About popup

    private Effect _effect = Effect.Static;
    private Color _currentColor = Color.FromArgb(0, 200, 170);
    private string? _colorTarget; // null = all devices; otherwise a DeviceRow.Key

    // the live-preview strip's own static-color fade state
    private readonly FadeState _pvFade = new();

    // screen-sync (ambient) sampling state, reused across frames to avoid per-frame allocations
    private Bitmap? _grabFull;
    private Bitmap? _grabSmall;
    private byte[]? _grabBuf;                       // reusable pixel buffer for the ambient sample
    private Color _ambientColor = Color.Black;
    private DateTime _lastAmbientSample = DateTime.MinValue;

    private readonly ToolTip _tips = new();
    private ToolStripMenuItem _trayEffectMenu = null!;
    private ToolStripMenuItem _trayColorMenu = null!;
    private ToolStripMenuItem _trayProfileMenu = null!;
    private int _profileCycle = -1; // tray middle-click cycles through profiles

    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 800 };

    private readonly bool _startMinimized;
    private bool _exitRequested;
    private NotifyIcon _tray = null!;
    private Icon? _trayDynIcon;                     // generated tray icon (current color dot)
    private int? _trayIconArgb;                     // fill the tray icon currently shows
    private DateTime _trayIconAt;                   // when it was last regenerated
    private readonly System.Windows.Forms.Timer _trayIconRetry = new() { Interval = 200 };
    private Label _statusDot = null!;

    private bool _blackedOut;                       // lights forced off (Windows locked / asleep)
    private double _rescanPendingSince = -1;        // clock secs a DeviceListUpdated burst began (-1 = none)
    private double _lastDirtyAt;                     // clock secs of the most recent dirty event
    private DeviceRow? _identifyRow;                // device currently blinking white (Identify)
    private double _identifyUntil;                  // clock seconds when the blink ends
    private FormWindowState _stateBeforeMinimize = FormWindowState.Normal;
    private IntPtr _powerNotify;                    // display-state notification registration
    private Effect _effectBeforeOff = Effect.Static; // what the lights-toggle hotkey restores
    private Dictionary<string, string>? _knownDevices; // rowKey -> name, for connect/disconnect notices
    private string? _deviceFault;                   // a wedged/unresponsive device, surfaced once
    private bool _conflictWarned;                   // contending RGB software balloon shown this session

    /// <summary><c>--test-main</c>: build the real window on pretend hardware (HarnessRig) with no hotkeys,
    /// no watchdog, no tray icon and read-only settings. See Program.RunMainHarness.</summary>
    internal static bool Harness;
    /// <summary>Harness window drawn at 2% opacity, click-through and never activated, so a screenshot run
    /// cannot catch a real click or drag while Erik works (PrintWindow still captures it in full).</summary>
    internal static bool HarnessGhost;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (Harness && HarnessGhost)
                cp.ExStyle |= 0x20 /*WS_EX_TRANSPARENT*/ | 0x80 /*WS_EX_TOOLWINDOW*/ | 0x08000000 /*WS_EX_NOACTIVATE*/;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => Harness && HarnessGhost;

    public MainForm(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        _settings = AppSettings.Load();
        if (Harness)
        {
            // In memory only (Save is a no-op in harness mode): no tray hide, no global hotkeys
            // fighting the live instance, no conflict balloon, no start-hidden.
            _settings.CloseToTray = false;
            _settings.GlobalHotkeys = false;
            _settings.WarnOnRgbConflicts = false;
            // Renders must be deterministic: an idle desk at night would otherwise black the
            // preview out mid-capture (RenderFrame returns before the preview is fed).
            _settings.NightOffEnabled = _settings.IdleOffEnabled = false;
            _settings.OffWhenLocked = _settings.OffWhenSleeps = _settings.OffWhenDisplaySleeps = false;
            _settings.GlassEffect = Environment.GetEnvironmentVariable("PRISMA_HARNESS_GLASS") == "1";
            // PRISMA_CLASSIC picked the skin before settings were read; keep the in-memory setting in step,
            // or the first settings change would flip the window back to the saved skin.
            _settings.ClassicSkin = Theme.Classic;
        }
        if (Enum.TryParse(_settings.Effect, out Effect savedEffect)) _effect = savedEffect;
        _currentColor = Color.FromArgb(_settings.ColorArgb);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _frameTimer.Interval = Math.Max(20, 1000 / Math.Clamp(_settings.FrameRate, 5, 30));
        Theme.SetAccent(Color.FromArgb(_settings.AccentArgb));
        Backdrop.Intensity = Math.Clamp(_settings.BackdropGlow, 0, 100);
        Backdrop.Glass = _settings.GlassEffect && !Theme.Classic;
        ToolStripManager.Renderer = new ThemedMenuRenderer(); // themes every menu + nested submenu uniformly

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.ResizeRedraw, true); // the form itself paints the backdrop margins
        SuspendLayout();
        BuildUi();
        ResumeLayout();
        BuildTray();
        if (_startMinimized)
        {
            // shown invisibly for one frame so the window handle exists, then hidden to tray
            Opacity = 0;
            ShowInTaskbar = false;
        }
        else
        {
            Opacity = EffectiveOpacity();
        }
        _frameTimer.Tick += (_, _) => RenderFrame();
        // Trailing-edge refresh for the throttled tray icon (see UpdateTrayIcon).
        _trayIconRetry.Tick += (_, _) => { _trayIconRetry.Stop(); UpdateTrayIcon(); };
        // Fires a few seconds after wake-from-sleep to re-assert direct control (see
        // OnPowerModeChanged): ASUS Aura boards revert to their firmware rainbow default on
        // resume, and only re-Preparing the device (the mode write) pulls it back.
        _resumeReassert.Tick += (_, _) =>
        {
            _resumeReassert.Stop();
            // _autoOff: don't re-light in the middle of a scheduled night/idle off window on wake
            // (matches the _bootSettle guard below; ReassertLighting -> ApplyCurrentEffect has no
            // _autoOff check, so a firmware effect would come back on autonomously and stay lit).
            if (!IsHandleCreated || _blackedOut || _autoOff || _detecting || _deviceRows.Count == 0) return;
            if (_openRgb.Connected) ReassertLighting();
            else _ = DetectDevicesAsync();   // socket died over sleep -> full reconnect + re-prepare
        };
        // Cold-boot cure: a RAM stick / board zone that enumerates late on the SMBus (the boot
        // detect grows e.g. 5 -> 7 devices) can take its first Prepare while its controller is
        // still settling and then ignore colour writes until the OpenRGB server is restarted
        // (observed: leftmost RAM stuck after power-on, fixed only by a server restart). Re-assert
        // direct mode a couple more times over the seconds AFTER the boot detect, once the bus has
        // settled, so the late device gets a clean mode write (Prepare = re-issue Direct) without
        // a manual restart. Only armed inside the boot window (see DetectDevicesCore).
        _bootSettle.Tick += (_, _) =>
        {
            _bootSettle.Stop();
            if (!IsHandleCreated || _blackedOut || _autoOff || _detecting || _deviceRows.Count == 0) return;
            if (_openRgb.Connected) ReassertLighting("boot-settle");
            if (--_bootSettlePasses > 0) _bootSettle.Start();
        };
        // If OpenRGB gets closed or crashes, restart it and reconnect automatically.
        _watchdog.Tick += async (_, _) =>
        {
            if (_detecting) return;
            if (_openRgbEverConnected && !_openRgb.Connected)
            {
                await DetectDevicesAsync();
                return;
            }
            // Boot race: we may connect while the server is still scanning and get a
            // partial list (no GPU). Cheap count probe catches a missed push notice.
            if (_openRgb.Connected)
            {
                int? count = _openRgb.TryGetControllerCount();
                // Defer to a pending debounce (a wake/hot-plug burst still settling) so the
                // watchdog doesn't preempt the coalesced rescan and re-introduce the flicker.
                if (_rescanPendingSince < 0 && count.HasValue && count.Value != _openRgb.Devices.Count)
                {
                    DebugLog.Log($"server count {count} != cached {_openRgb.Devices.Count} -> rescan");
                    await DetectDevicesAsync();
                    return;
                }
            }
            CheckProfileTriggers();
        };
        if (!Harness) _watchdog.Start();   // nothing to reconnect on pretend hardware
        SyncEffectTuning();
        _gpuExternalApplied = _settings.GpuExternalControl;
        _ignoreLogitechApplied = _settings.IgnoreLogitechMouse;
        // Optional: drop all lighting while the session is locked or the machine sleeps.
        if (!Harness)
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        Shown += async (_, _) =>
        {
            if (_startMinimized)
            {
                Hide();
                Opacity = EffectiveOpacity();
                ShowInTaskbar = true;
            }
            await DetectDevicesAsync();
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) Hide();
            else _stateBeforeMinimize = WindowState; // so the tray restores maximized windows maximized
            UpdateMaxGlyph();
        };
        // While the user drags the window border/title bar, Windows runs a modal size/move
        // loop that still pumps WM_TIMER — so the 25fps frame loop would keep firing and do
        // BLOCKING device writes (GPU I2C, OpenRGB socket) on the UI thread between paints,
        // stalling the resize. Park the frame loop for the duration of the drag; effects
        // freeze for that fraction of a second and resume the instant the drag ends.
        ResizeBegin += (_, _) =>
        {
            _frameWasRunningBeforeSizeMove = _frameTimer.Enabled;
            _frameTimer.Stop();
        };
        ResizeEnd += (_, _) =>
        {
            if (_frameWasRunningBeforeSizeMove) _frameTimer.Start();
            SaveSoon();
        };
        FormClosing += (_, e) =>
        {
            // only the user's X click hides to tray; shutdown/logoff must proceed
            if (!_exitRequested && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray)
            {
                e.Cancel = true; // closing the window keeps effects running in the tray
                Hide();
            }
        };
        FormClosed += (_, _) =>
        {
            SaveNow();
            UnregisterHotKey(Handle, HK_LIGHTS);
            UnregisterHotKey(Handle, HK_PROFILE);
            UnregisterHotKey(Handle, HK_RESCAN);
            if (_powerNotify != IntPtr.Zero) { UnregisterPowerSettingNotification(_powerNotify); _powerNotify = IntPtr.Zero; }
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _watchdog.Stop();
            _resumeReassert.Stop();
            _resumeReassert.Dispose();
            _bootSettle.Stop();
            _bootSettle.Dispose();
            _frameTimer.Stop();
            _saveTimer.Stop();
            _saveTimer.Dispose();
            _trayIconRetry.Stop();
            _trayIconRetry.Dispose();
            _gpuReassert?.Stop();
            _gpuReassert?.Dispose();
            _audio.Dispose();
            if (_settings.OffOnExit) BlackoutDevices(); // leave the hardware dark on a real exit
            DisposeDevices();
            _grabFull?.Dispose();
            _grabSmall?.Dispose();
            _openRgb.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        };
    }

    // ---------------------------------------------------------------- UI

    private void BuildUi()
    {
        Text = "Prisma";
        // Sized from the frame our WM_NCCALCSIZE actually leaves (sides + bottom, no caption), so the
        // client really is MIN_CW x DEF_CH. ClientSize= would add a caption height the window never has.
        Size = OuterSizeFor(new Size(MIN_CW, DEF_CH));
        MinimumSize = OuterSizeFor(new Size(MIN_CW, MinClientH)); // clamps stale saved bounds too
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.UiFont(9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // restore last window placement if it still fits a connected screen
        if (_settings.WinW > 0)
        {
            var saved = new Rectangle(_settings.WinX, _settings.WinY, _settings.WinW, _settings.WinH);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
                // The window may have grown since the position was saved (MinimumSize
                // clamps the height up) — keep it inside the work area, off the taskbar.
                var wa = Screen.FromRectangle(saved).WorkingArea;
                Location = new Point(
                    Math.Max(wa.Left, Math.Min(Left, wa.Right - Width)),
                    Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height)));
            }
            if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        }

        // ================================================================ the title strip
        // One borderless strip that IS the title bar, on Mixtape's metrics: logo + wordmark, the power
        // disc, the deck, two flat toggles, the gear and the caption buttons. The strip answers
        // HTTRANSPARENT and WndProc turns its empty area into HTCAPTION: native drag, Aero snap,
        // double-click maximise and drag-restore - no ReleaseCapture hack.
        string version = Application.ProductVersion.Split('+')[0];
        _strip = new TitleStrip
        {
            Left = 0, Top = 0, Width = ClientSize.Width, Height = TitleStrip.TopH,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, TabIndex = 0,
        };
        Controls.Add(_strip);

        _title = new BrandButton { TabIndex = 0 };
        _title.Click += (_, _) => ShowAboutPanel();
        _tips.SetToolTip(_title, $"About Prisma {version} — devices, backends");

        _lightsBtn = new PowerButton { TabIndex = 1 };
        _lightsBtn.Click += (_, _) => ToggleLights();
        _tips.SetToolTip(_lightsBtn, "Lights on / off  (Ctrl+Alt+L)");

        _deck = new DeckCard();
        _deck.TileClicked += (_, _) => PickColour();
        _deck.TipRegionChanged += (_, _) => _tips.SetToolTip(_deck, _deck.TipAt(_deck.PointToClient(Cursor.Position)));
        // a right-click on the deck reaches WndProc's WM_CONTEXTMENU like the rest of the strip (one menu)

        _screenBtn = new FlatIconButton { Glyph = Glyphs.Monitor, ClassicIcon = ClassicIcons.Id.Monitor, TabIndex = 2 };
        _screenBtn.Click += (_, _) => SelectEffect(_effect == Effect.Ambient ? Effect.Static : Effect.Ambient);
        _tips.SetToolTip(_screenBtn, "Screen sync on / off");
        _pinBtn = new FlatIconButton { Glyph = Glyphs.Pin, ActiveGlyph = Glyphs.Pinned, ClassicIcon = ClassicIcons.Id.Pin, TabIndex = 3 };
        _pinBtn.Click += (_, _) => { TopMost = !TopMost; _settings.AlwaysOnTop = TopMost; UpdateHeaderButtons(); SaveSoon(); };
        _tips.SetToolTip(_pinBtn, "Keep window on top");
        var gearBtn = new FlatIconButton { Glyph = Glyphs.Gear, ClassicIcon = ClassicIcons.Id.Gear, Ghost = true, TabIndex = 4 };
        gearBtn.Click += (_, _) => OpenSettings();
        _tips.SetToolTip(gearBtn, "Settings — appearance, auto-off, effects");

        var minBtn = new CaptionButton { Which = CaptionButton.Kind.Minimize };
        minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _tips.SetToolTip(minBtn, "Minimize to tray");
        _maxBtn = new CaptionButton { Which = CaptionButton.Kind.Maximize };
        _maxBtn.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized;
        var closeBtn = new CaptionButton { Which = CaptionButton.Kind.Close };
        closeBtn.Click += (_, _) => Close();
        _tips.SetToolTip(closeBtn, "Close");

        _strip.Brand = _title; _strip.Power = _lightsBtn; _strip.Deck = _deck;
        _strip.Screen = _screenBtn; _strip.Pin = _pinBtn; _strip.Gear = gearBtn;
        _strip.Min = minBtn; _strip.Max = _maxBtn; _strip.Close = closeBtn;
        _strip.Controls.AddRange(new Control[] { _title, _lightsBtn, _deck, _screenBtn, _pinBtn, gearBtn, minBtn, _maxBtn, closeBtn });
        _strip.Resize += (_, _) => _strip.Relayout();
        Activated += (_, _) => _strip.CaptionActive = true;
        Deactivate += (_, _) => _strip.CaptionActive = false;
        UpdateMaxGlyph();
        TopMost = _settings.AlwaysOnTop;

        // ================================================================ the rail (left card)
        // Mixtape's rail: DEVICES (its library), PROFILES (its playlists), and a footer with the full
        // status line and Rescan. Sections are positioned by LayoutLeftColumn.
        int bodyTop = TitleStrip.TopH, bodyH = ClientSize.Height - bodyTop - GAP;
        var left = _leftCol = new SurfacePanel
        {
            Surface = SurfacePanel.Kind.Rail, Left = GAP, Top = bodyTop, Width = RAIL_W, Height = bodyH,
            Anchor = AnchorStyles.Top | AnchorStyles.Left, TabIndex = 1,
        };
        Controls.Add(left);
        const int railSecW = RAIL_W - 4;   // section inner = RAIL_W - 24: 12 px from each card edge
        _devicesCard = new CardPanel
        {
            Section = true, OnRail = true, Title = "DEVICES", Left = 2, Top = 16, Width = railSecW, Height = 200,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        left.Controls.Add(_devicesCard);
        _deviceList = new ScrollList
        {
            Left = CardPanel.SectionPad, Top = CardPanel.SectionTop, Width = railSecW - 2 * CardPanel.SectionPad, Height = 150,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _devicePanel = _deviceList.Content;
        _devicesCard.Controls.Add(_deviceList);
        // rows track the list's row width, so the side margins stay symmetric whether or not the
        // scroll bar is showing
        _deviceList.RowWidthChanged += (_, _) => FitDeviceRows();

        _profilesCard = new CardPanel
        {
            Section = true, OnRail = true, Title = "PROFILES", Left = 2, Top = 260, Width = railSecW, Height = 80,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        left.Controls.Add(_profilesCard);
        _profileAdd = new RoundedButton
        {
            Variant = RoundedButton.Look.Chip, Text = "＋", CornerRadius = 6, Width = 26, Height = 22,
            Left = railSecW - CardPanel.SectionPad - 26 + 10, Top = 0, Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _profileAdd.Click += (_, _) => SaveCurrentAsProfile();
        _tips.SetToolTip(_profileAdd, "Save the current look as a profile");
        _profilesCard.Controls.Add(_profileAdd);
        _profileHint = new CrispLabel
        {
            Text = "Save the current look with +", AutoSize = false, Left = CardPanel.SectionPad, Top = CardPanel.SectionTop,
            Width = railSecW - 2 * CardPanel.SectionPad, Height = 22, ForeColor = Theme.Faint, BackColor = Color.Transparent,
            Font = Theme.UiFont(8.5f, FontStyle.Italic), TextAlign = ContentAlignment.MiddleLeft,
        };
        _profilesCard.Controls.Add(_profileHint);
        _profileList = new ScrollList
        {
            Left = CardPanel.SectionPad, Top = CardPanel.SectionTop, Width = railSecW - 2 * CardPanel.SectionPad, Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Left, Visible = false,
        };
        _profileList.RowWidthChanged += (_, _) => FitProfileRows();
        _profilesCard.Controls.Add(_profileList);

        // footer: the full status (wraps up to three lines - the fix sits at the END of the long ones),
        // a hairline rule, then Rescan
        _statusDot = new CrispLabel
        {
            Text = "●", AutoSize = true, Left = 12, ForeColor = Theme.Accent, BackColor = Color.Transparent,
            Font = Theme.UiFont(7.5f),
        };
        left.Controls.Add(_statusDot);
        _status = new CrispLabel
        {
            Text = "Detecting RGB devices...", AutoSize = false, AutoEllipsis = true, Left = 26,
            Width = RAIL_W - 26 - 12, Height = 46, ForeColor = Theme.Subtle, BackColor = Color.Transparent,
            Font = Theme.UiFont(8.75f), TextAlign = ContentAlignment.BottomLeft,   // sits on the rule, not afloat
        };
        left.Controls.Add(_status);
        _rescanButton = new RoundedButton
        {
            Variant = RoundedButton.Look.Secondary, Text = "Rescan devices", CornerRadius = 8,
            Left = 12, Width = RAIL_W - 24, Height = 30,
        };
        _rescanButton.Click += async (_, _) => await DetectDevicesAsync();
        _tips.SetToolTip(_rescanButton, "Rescan devices  (Ctrl+Alt+R)");
        left.Controls.Add(_rescanButton);
        RebuildProfileRows();

        // ================================================================ the content card
        // ONE surface (Mixtape: no box in a box): EFFECTS, COLOR, the LIVE stage flexing in the middle,
        // TUNING docked at the bottom. Sections are positioned by LayoutContent.
        var right = _rightCol = new SurfacePanel
        {
            Surface = SurfacePanel.Kind.Content, Left = GAP + RAIL_W + GAP, Top = bodyTop,
            Width = ClientSize.Width - (GAP + RAIL_W + GAP) - GAP, Height = bodyH,
            Anchor = AnchorStyles.Top | AnchorStyles.Left, TabIndex = 2,
        };
        Controls.Add(right);

        // Right-click the empty background of either card for the quick menu.
        foreach (Control bg in new Control[] { left, right })
            bg.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowBackgroundMenu((Control)s!, e.Location); };

        int secW = right.Width - 2 * (PAD - CardPanel.SectionPad);   // a section spans the page inset minus its own pad
        int inner = secW - 2 * CardPanel.SectionPad;                 // == the card width minus the 22 px page inset each side
        CardPanel AddSection(string title)
        {
            var c = new CardPanel
            {
                Section = true, Title = title, Left = PAD - CardPanel.SectionPad, Top = 0, Width = secW, Height = 100,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            right.Controls.Add(c);
            return c;
        }

        // ---------------- EFFECTS: tiles with their own art, left-aligned labels
        _effectsCard = AddSection("EFFECTS");
        _effectGrid = new Panel
        {
            BackColor = Color.Transparent, Left = CardPanel.SectionPad, Top = CardPanel.SectionTop, Width = inner, Height = 100,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _effectsCard.Controls.Add(_effectGrid);
        // "All off" is not a tile: off lives on the strip's power disc (Ctrl+Alt+L), its right-click menu
        // and the tray menu.
        var effects = new[]
        {
            Effect.Static, Effect.RainbowWave, Effect.ColorCycle, Effect.Breathing, Effect.Strobe, Effect.Gradient,
            Effect.Comet, Effect.Twinkle, Effect.Ambient, Effect.Surprise, Effect.Fire, Effect.Music,
        };
        foreach (var fx in effects)
        {
            var btn = new RoundedButton
            {
                Variant = RoundedButton.Look.Tile, Text = EffectLabel(fx), Tag = fx, CornerRadius = 8, Height = 40,
                Art = (g, r) => EffectArt.Paint(g, r, fx, ActivePickColor, Color.FromArgb(_settings.GradientEndArgb), 6, _ambientNow),
            };
            btn.Click += (s, _) => SelectEffect((Effect)((RoundedButton)s!).Tag!);
            btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowEffectMenu((RoundedButton)s!, (Effect)((RoundedButton)s!).Tag!); };
            _effectButtons.Add(btn);
            _effectGrid.Controls.Add(btn);
        }
        _effectGrid.Resize += (_, _) => { if (LayoutEffectTiles()) LayoutContent(); };

        // ---------------- COLOR: the Applies-to chip on the caption band, wells, actions, hue
        _colorCard = AddSection("COLOR");
        _targetButton = new RoundedButton
        {
            Variant = RoundedButton.Look.Chip, Text = "Applies to: All devices  ▾", CornerRadius = 8, Height = 22, Top = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _targetButton.Click += (_, _) => ShowTargetMenu();
        _colorCard.Controls.Add(_targetButton);
        _autoColorNote = new InlineLabel
        {
            Text = "Colors are automatic for this effect", Top = 0, Height = 22, Width = 260,
            TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Faint,
            Font = Theme.UiFont(8.75f), Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _colorCard.Controls.Add(_autoColorNote);

        _swatchGrid = new Panel
        {
            BackColor = Color.Transparent, Left = CardPanel.SectionPad, Top = CardPanel.SectionTop, Width = inner, Height = 46,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _colorCard.Controls.Add(_swatchGrid);
        var swatches = new[]
        {
            Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255),
            Color.FromArgb(255, 140, 0), Color.FromArgb(255, 220, 0), Color.FromArgb(0, 200, 170),
            Color.FromArgb(190, 60, 255), Color.FromArgb(255, 70, 160), Color.White,
            Color.FromArgb(255, 244, 229)
        };
        foreach (var c in swatches)
        {
            var sw = MakeSwatch(c);
            sw.Click += (s, _) => SetColor((Color)((RoundedButton)s!).Tag!);
            sw.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowSwatchMenu((RoundedButton)s!, (Color)((RoundedButton)s!).Tag!, saved: false); };
            _swatchButtons.Add(sw);
            _swatchGrid.Controls.Add(sw);
        }
        _swatchGrid.Resize += (_, _) => { if (LayoutSwatches()) LayoutContent(); };

        _actionRow = new Panel
        {
            BackColor = Color.Transparent, Left = CardPanel.SectionPad, Width = inner, Height = 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _colorCard.Controls.Add(_actionRow);
        var customBtn = new RoundedButton { Variant = RoundedButton.Look.Secondary, Text = "Custom…", CornerRadius = 8, Height = 30 };
        customBtn.Click += (_, _) => PickColour();
        var saveBtn = new RoundedButton { Variant = RoundedButton.Look.Secondary, Text = "＋ Save color", CornerRadius = 8, Height = 30 };
        _tips.SetToolTip(saveBtn, "Save the current color as a swatch (right-click a saved swatch to remove)");
        saveBtn.Click += (_, _) => SaveCurrentColor();
        _gradientBtn = new RoundedButton { Variant = RoundedButton.Look.Swatch, Text = "Gradient end", CornerRadius = 8, Height = 30 };
        _tips.SetToolTip(_gradientBtn, "Second color of the Gradient effect - the strip blends from the picked color into this one");
        _gradientBtn.Click += (_, _) =>
        {
            using var dlg = new ColorPickerDialog(Color.FromArgb(_settings.GradientEndArgb));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _settings.GradientEndArgb = dlg.SelectedColor.ToArgb();
            SyncEffectTuning();
            UpdateGradientButton();
            UpdateSwatchSelection();   // the gradient art on the tiles and the deck
            UpdateDeviceChips();       // ...and on every device running Gradient
            LookEdited();
            SaveSoon();
            if (_effect != Effect.Gradient) SelectEffect(Effect.Gradient); // picking an end color implies the gradient
            UpdateDeck();
        };
        _actionRow.Controls.AddRange(new Control[] { customBtn, saveBtn, _gradientBtn });
        _customBtn = customBtn; _saveBtn = saveBtn;
        UpdateGradientButton();
        RebuildSavedSwatches();

        _hueBar = AddInlineSlider(_colorCard, "Hue", 0, inner, out _hueValue, out _hueLabel);
        _hueBar.Minimum = 0; _hueBar.Maximum = 360; _hueBar.RainbowTrack = true;
        _hueBar.Value = (int)_currentColor.GetHue();
        _hueBar.ValueChanged += (_, _) =>
        {
            _hueValue.Text = $"{_hueBar.Value}°";
            if (!_syncingHue) SetColor(Theme.HsvToColor(_hueBar.Value));
        };
        _hueValue.Text = $"{_hueBar.Value}°";

        // ---------------- the LIVE stage: the lights' own colours, as large as the space allows
        _stageCard = AddSection("LIVE PREVIEW");
        _stageCard.CaptionInClassicOnly = true;
        _preview = new LivePreview
        {
            Left = CardPanel.SectionPad, Top = 0, Width = inner, Height = 80,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _stageCard.Controls.Add(_preview);

        // ---------------- TUNING, docked at the bottom: brightness never jumps when speed is blocked
        _tuningCard = AddSection("TUNING");
        _speedBar = AddInlineSlider(_tuningCard, "Speed", CardPanel.SectionTop, inner, out _speedValue, out _speedLabel);
        _speedBar.Value = Math.Clamp(_settings.Speed, 1, 100);
        _speedBar.ValueChanged += (_, _) => { _speedValue.Text = $"{_speedBar.Value}%"; LookEdited(); SaveSoon(); };
        _speedValue.Text = $"{_speedBar.Value}%";
        _brightBar = AddInlineSlider(_tuningCard, "Brightness", CardPanel.SectionTop + RowH + 8, inner, out _brightValue, out _brightLabel);
        _brightBar.Value = Math.Clamp(_settings.Brightness, 1, 100);
        _brightBar.ValueChanged += (_, _) => { _brightValue.Text = $"{_brightBar.Value}%"; LookEdited(); SaveSoon(); UpdateDeck(); };
        _brightValue.Text = $"{_brightBar.Value}%";

        UpdateEffectButtons();
        UpdateSwatchSelection();
        LayoutColumns();
        _strip.Relayout();
        // The rail (device list, profiles, footer) is laid out by LayoutLeftColumn against the current
        // card height; it must re-run on every resize too, or a maximise->expand->restore leaves the
        // footer below the edge.
        Resize += (_, _) => { LayoutColumns(); if (WindowState != FormWindowState.Minimized) LayoutLeftColumn(); };
    }

    // ---- shell metrics (Mixtape's): the cards float 14 px from every edge and from each other ----
    private const int GAP = 14;           // between the cards and to every window edge
    private const int RAIL_W = 300;       // the rail card
    private const int PAD = 22;           // the content card's page inset (Mixtape: 22 everywhere)
    private const int BLOCK_MAX = 1202;   // maximised: rail + content stop growing and the block centres
    private const int RowH = 32;          // an inline slider row
    private const int MIN_CW = 920, MIN_CH = 780, DEF_CH = 800;   // client sizes
    private const int MIN_CH_CLASSIC = 804;   // the 95 group boxes (stage caption + padding) need 24 px more
    private static int MinClientH => Theme.Classic ? MIN_CH_CLASSIC : MIN_CH;
    private int _blockLeft = -1, _blockTop = -1;   // last placement, so moves repaint their wallpaper slice

    /// <summary>The outer window size whose client our WM_NCCALCSIZE leaves at <paramref name="client"/>
    /// (side and bottom frame only - the caption is removed).</summary>
    internal static Size OuterSizeFor(Size client)
    {
        int fx = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        int fy = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        return new Size(client.Width + 2 * fx, client.Height + fy);
    }

    /// <summary>The device list's back colour: opaque (an AutoScroll panel cannot be transparent), so it
    /// is re-asserted after every skin change instead of being remapped (RailBg is never baked).</summary>
    private static Color DeviceListBack => Theme.Classic ? Color.White : Theme.RailBg;

    /// <summary>Places the rail and the content card: the content card runs to the right edge, 14 px
    /// in, like the strip above it. Only a MAXIMISED window caps the block at BLOCK_MAX and centres it,
    /// letting the wallpaper breathe at the sides of a wide screen.</summary>
    private void LayoutColumns()
    {
        if (WindowState == FormWindowState.Minimized || _rightCol == null) return;
        int W = ClientSize.Width, H = ClientSize.Height;
        bool maxed = IsHandleCreated ? IsZoomed(Handle) : WindowState == FormWindowState.Maximized;
        int full = W - (GAP + RAIL_W + GAP) - GAP;
        int contentW = Math.Max(300, maxed ? Math.Min(full, BLOCK_MAX - RAIL_W - GAP) : full);
        int x0 = maxed ? Math.Max(GAP, (W - (RAIL_W + GAP + contentW)) / 2) : GAP;
        int top = TitleStrip.TopH, h = Math.Max(200, H - top - GAP);
        _leftCol.SetBounds(x0, top, RAIL_W, h);
        _rightCol.SetBounds(x0 + RAIL_W + GAP, top, contentW, h);
        LayoutContent();
        if (x0 != _blockLeft || top != _blockTop)
        {
            _blockLeft = x0; _blockTop = top;
            Invalidate(true); // panels moved: repaint their slice of the shared wallpaper
        }
    }

    /// <summary>Stacks the content card: EFFECTS and COLOR from the top, TUNING docked at the bottom, the
    /// LIVE stage taking what is left between them (48..240 px; any extra space falls above the dock).</summary>
    private void LayoutContent()
    {
        if (_rightCol == null || _tuningCard == null) return;
        const int SectionGap = 16;                              // + the 10 px section bottom pad = 26 px rhythm
        bool c95 = Theme.Classic;
        // Classic: the cards are invisible (plain face), so the group boxes share the rail's geometry -
        // 2 px in, frame lines from y 12 - and line up with the Devices box beside them
        int secX = c95 ? 2 : PAD - CardPanel.SectionPad;
        int H = _rightCol.Height, secW = c95 ? _rightCol.Width - 4 : _rightCol.Width - 2 * (PAD - CardPanel.SectionPad);
        int y = c95 ? 12 : PAD - 6;                              // modern: caption glyphs land on the 22 px page inset

        LayoutEffectTiles();
        _effectsCard.SetBounds(secX, y, secW, CardPanel.SectionTop + _effectGrid.Height + 10);
        y = _effectsCard.Bottom + SectionGap;

        LayoutSwatches();
        _actionRow.Top = _swatchGrid.Bottom + 8;
        LayoutActionRow();
        PlaceInlineRow(_hueLabel, _hueBar, _hueValue, _actionRow.Bottom + 10);
        _colorCard.SetBounds(secX, y, secW, _hueLabel.Bottom + 10);
        y = _colorCard.Bottom + SectionGap;

        int dockH = CardPanel.SectionTop + RowH + 8 + RowH + 10;
        // Classic: the Tuning box ends where the rail's Rescan button does (14 px above the edge)
        int dockTop = H - (c95 ? 14 : PAD - 10) - dockH;
        _tuningCard.SetBounds(secX, dockTop, secW, dockH);
        _rightCol.RuleY = c95 ? -1 : dockTop - SectionGap / 2;

        int stageTop0 = c95 ? CardPanel.SectionTop : 0;              // Classic: a captioned group box
        // the stage is the flexible part: below 48 px of room it shrinks rather than run into the dock
        int room = dockTop - SectionGap - y - stageTop0 - 10;
        int stageH = Math.Clamp(room, Math.Min(48, Math.Max(16, room)), 240);
        _stageCard.SetBounds(secX, y, secW, stageTop0 + stageH + (c95 ? 10 : 0));
        _preview.SetBounds(CardPanel.SectionPad, stageTop0, secW - 2 * CardPanel.SectionPad, stageH);

        // The Applies-to chip and the auto-colour note: on the caption band in the modern look; inside
        // the 95 group box (under its frame line, above the wells) in Classic - only the caption may
        // break a group box's frame.
        int noteW = Math.Min(secW - 2 * CardPanel.SectionPad, 280);
        _targetButton.Width = Math.Min(noteW, ChipWidth(_targetButton.Text));
        if (c95)
        {
            _targetButton.SetBounds(secW - 8 - _targetButton.Width, 9, _targetButton.Width, 18);
            _autoColorNote.SetBounds(secW - 8 - noteW, 9, noteW, 18);
        }
        else
        {
            _targetButton.SetBounds(secW - CardPanel.SectionPad - _targetButton.Width + 4, 0, _targetButton.Width, 22);
            _autoColorNote.SetBounds(secW - CardPanel.SectionPad - noteW, 0, noteW, 22);
        }
    }

    private static int ChipWidth(string text) =>
        TextRenderer.MeasureText(text, Theme.UiFontShared(8.75f), Size.Empty, TextFormatFlags.NoPadding).Width + 20;

    /// <summary>Lays the effect tiles out: 3 columns in a narrow card, 4 from 720 px. Returns whether
    /// the grid's height changed (the caller then re-stacks the card).</summary>
    private bool LayoutEffectTiles()
    {
        int w = _effectGrid.Width, n = _effectButtons.Count;
        if (w <= 0 || n == 0) return false;
        const int TileH = 40, Gap = 8;
        int cols = w < 720 ? 3 : 4, rows = (n + cols - 1) / cols;
        int baseW = (w - Gap * (cols - 1)) / cols, rem = (w - Gap * (cols - 1)) % cols;
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            int x = c * (baseW + Gap) + Math.Min(c, rem);
            _effectButtons[i].SetBounds(x, r * (TileH + Gap), baseW + (c < rem ? 1 : 0), TileH);
        }
        int h = rows * TileH + (rows - 1) * Gap;
        if (_effectGrid.Height == h) return false;
        _effectGrid.Height = h;
        return true;
    }

    /// <summary>Square colour wells in a 10-column grid: the side follows the column width up to 52 px;
    /// past that the gaps grow, so the row still spans the full width. Saved colours get their own row.</summary>
    private bool LayoutSwatches()
    {
        int w = _swatchGrid.Width;
        if (w <= 0) return false;
        const int MinGap = 8, Max = 52;
        int side = Math.Min(Max, (w - MinGap * (SwatchCols - 1)) / SwatchCols);
        double pitch = SwatchCols > 1 ? (w - side) / (double)(SwatchCols - 1) : 0;
        void Row(List<RoundedButton> wells, int y)
        {
            for (int i = 0; i < wells.Count; i++)
                wells[i].SetBounds((int)Math.Round(i * pitch), y, side, side);
        }
        Row(_swatchButtons, 0);
        Row(_savedSwatchButtons, side + MinGap);
        int h = _savedSwatchButtons.Count > 0 ? side * 2 + MinGap : side;
        if (_swatchGrid.Height == h) return false;
        _swatchGrid.Height = h;
        return true;
    }

    /// <summary>Custom… / ＋ Save color / Gradient end at their natural widths, left-aligned; the
    /// gradient-end button only for the Gradient effect.</summary>
    private void LayoutActionRow()
    {
        int x = 0;
        foreach (var b in new[] { _customBtn, _saveBtn, _gradientBtn })
        {
            if (b == _gradientBtn && !_gradientShown) continue;
            var f = Theme.UiFontShared(9.5f, FontStyle.Bold);
            int w = Math.Max(96, TextRenderer.MeasureText(b.Text, f, Size.Empty, TextFormatFlags.NoPadding).Width + 28
                + (Theme.Classic && b.Variant == RoundedButton.Look.Swatch ? 22 : 0));   // the 95 colour square
            b.SetBounds(x, 0, w, 30);
            x += w + 8;
        }
    }

    /// <summary>An inline slider row: caption in a fixed 92 px column, the track, and the value
    /// right-aligned in the last 48 px - every row shares the columns, so the tracks line up.</summary>
    private SleekSlider AddInlineSlider(CardPanel parent, string caption, int top, int inner, out InlineLabel value, out InlineLabel label)
    {
        // InlineLabel draws without GDI's side padding, so the ink sits exactly on the section inset
        // (and the value's right edge on the far one); Classic adds the ':' and draws them black
        label = new InlineLabel
        {
            Caption = true, Text = caption, TextAlign = ContentAlignment.MiddleLeft, Left = CardPanel.SectionPad,
            Width = 92, Height = RowH, Top = top, ForeColor = Theme.Subtle,
            Font = Theme.UiFont(9.25f),
        };
        parent.Controls.Add(label);
        value = new InlineLabel
        {
            TextAlign = ContentAlignment.MiddleRight, Left = CardPanel.SectionPad + inner - 48,
            Width = 48, Height = RowH, Top = top, ForeColor = Theme.Faint,
            Font = Theme.UiFont(8.75f), Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        parent.Controls.Add(value);
        var slider = new SleekSlider
        {
            Left = CardPanel.SectionPad + 92, Top = top + (RowH - 24) / 2, Width = inner - 92 - 56, Height = 24,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        parent.Controls.Add(slider);
        return slider;
    }

    private static void PlaceInlineRow(Label label, SleekSlider slider, Label value, int top)
    {
        label.Top = value.Top = top;
        slider.Top = top + (RowH - 24) / 2;
    }

    private static Label MakeSectionLabel(string text, int x, int y) => new()
    {
        Text = text, Left = x, Top = y, AutoSize = true,
        ForeColor = Theme.Accent, Font = Theme.UiFont(9f, FontStyle.Bold)
    };

    /// <summary>A colour well for the swatch grid (preset or saved); LayoutSwatches sizes it square.</summary>
    private static RoundedButton MakeSwatch(Color c) => new()
    {
        Variant = RoundedButton.Look.Well, CornerRadius = 8, Width = 46, Height = 46,
        FillColor = c, HoverFillColor = c, Tag = c
    };

    /// <summary>The colour picker, for the current colour target (COLOR > Custom..., the deck tile).</summary>
    private void PickColour()
    {
        using var dlg = new ColorPickerDialog(ActivePickColor);
        if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(dlg.SelectedColor);
    }

    private void OpenSettings()
    {
        SaveNow(); // flush live effect/colour so the Commands tab copies the current look
        using var dlg = new SettingsForm(_settings, ApplySettingsChanged);
        dlg.ShowDialog(this);
    }

    /// <summary>Pushes the current look into the strip's deck card: effect art + name, brightness, and
    /// a tile tooltip that says what a click will actually do.</summary>
    private void UpdateDeck()
    {
        if (_deck == null || _brightBar == null) return;
        _deck.SetLook(_effect, ActivePickColor, Color.FromArgb(_settings.GradientEndArgb), EffectLabel(_effect), _brightBar.Value);
        string? target = _colorTarget == null ? null : _deviceRows.FirstOrDefault(r => r.Key == _colorTarget)?.Device.Name;
        bool selfColouring = _effect is Effect.RainbowWave or Effect.ColorCycle or Effect.Fire or Effect.Ambient or Effect.Surprise;
        string pick = target != null ? $"Pick a color for {target}…" : "Pick a color…";
        _deck.TileTip = selfColouring ? pick + " (switches to Static color)" : pick;
    }

    private static RoundedButton MakeButton(string text, int x, int y, int w, int h) => new()
    {
        Text = text, Left = x, Top = y, Width = w, Height = h
    };

    // ---------------------------------------------------------------- state

    private void SelectEffect(Effect effect)
    {
        if (_applyingProfile == 0 && _appliedProfile != null && effect != _effect) MarkAppliedProfile(null); // a manual change: no longer that profile's look
        _effect = effect;
        UpdateEffectButtons();
        UpdateTrayText();
        ApplyCurrentEffect();
        SaveSoon();
    }

    /// <summary>Routes the current effect per device: single-colour devices that offer a
    /// matching onboard mode run it on their own firmware (smooth); everything else is
    /// rendered in software each frame. Re-run whenever the effect or device set changes.</summary>
    /// <summary>The effect a row renders: its per-device override, or the global effect.</summary>
    private Effect RowEffect(DeviceRow row) =>
        _settings.DeviceEffects.TryGetValue(row.Key, out var name) && Enum.TryParse(name, out Effect e) ? e : _effect;

    private void ApplyCurrentEffect()
    {
        if (_detecting) { DebugLog.Log($"apply {_effect}: skipped, detecting"); return; }
        var prevHw = new HashSet<DeviceRow>(_hwRows);
        _hwRows.Clear();
        DebugLog.Log($"apply {_effect} (prevHw={prevHw.Count})");
        foreach (var row in _deviceRows)
        {
            bool wasHw = prevHw.Contains(row);
            var rowEffect = RowEffect(row);
            var kind = _nightDim ? null : EffectToHw(rowEffect); // dimming only works on streamed frames
            if (!row.Checked)
            {
                if (wasHw) Try(() => row.Device.Prepare()); // stop a firmware animation in place
                continue;
            }
            bool onHardware = false;
            if (kind.HasValue)
            {
                // The device decides if it can run this on its own firmware (GPU via an OpenRGB
                // mode, Gigabyte via an IT8297 effect); multi-zone strips decline and stay software.
                try { onHardware = row.Device.TrySetHardwareEffect(kind.Value, EffectiveColor(row)); } catch { }
            }
            if (onHardware) { _hwRows.Add(row); DebugLog.Log($"  {row.Device.Name}: on firmware"); }
            else if (wasHw)
            {
                // Leaving a firmware effect: return to direct control IN PLACE. Routing this
                // through a full re-detect used to black out every device for 10-20s, because
                // writing the GPU mode then immediately rescanning races the card's slow I2C
                // scan and stalls the OpenRGB server. The GPU MCU sometimes drops this in-place
                // write; ScheduleGpuReassert re-sends it 2.5s later (the manual Rescan button
                // remains the recovery if it ever truly sticks).
                DebugLog.Log($"  {row.Device.Name}: leaving firmware (prepare)");
                Try(() => row.Device.Prepare());
            }
        }
        UpdateAudioCapture();
        ScheduleGpuReassert();
    }

    /// <summary>The Music effect needs the system-audio meter; run it only while some
    /// active row (or the global effect) actually uses it.</summary>
    private void UpdateAudioCapture()
    {
        bool need = _effect == Effect.Music ||
                    _deviceRows.Any(r => r.Checked && RowEffect(r) == Effect.Music);
        if (need) _audio.Start();
        else _audio.Stop();
    }

    /// <summary>The Sapphire GPU's MCU sometimes silently drops a mode write that lands
    /// close after another one — it keeps playing its old onboard effect ("stuck in
    /// rainbow"). One delayed re-send of whatever the GPU should currently be doing,
    /// after the chip has settled, catches the dropped write.</summary>
    private void ScheduleGpuReassert()
    {
        _gpuReassert?.Stop();
        _gpuReassert?.Dispose();
        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        _gpuReassert = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (_gpuReassert == timer) _gpuReassert = null;
            if (_detecting || _blackedOut) return; // a blackout must not be re-lit by a pending reassert
            foreach (var row in _deviceRows)
            {
                // Matches both the OpenRGB GPU controller and the native ADL GPU driver
                // (both expose Kind "GPU" and Prepare/TrySetHardwareEffect via IRgbDevice).
                if (!row.Checked || row.Device is not IRgbDevice { Kind: "GPU" } gpu) continue;
                var kind = _nightDim ? null : EffectToHw(RowEffect(row));
                if (_hwRows.Contains(row) && kind.HasValue)
                {
                    DebugLog.Log($"re-assert firmware {kind} on {gpu.Name}");
                    Try(() => gpu.TrySetHardwareEffect(kind.Value, EffectiveColor(row)));
                }
                else if (!_hwRows.Contains(row))
                {
                    DebugLog.Log($"re-assert direct mode on {gpu.Name}");
                    Try(() => gpu.Prepare());
                }
            }
        };
        timer.Start();
    }

    // ------------------------------------------------------ lock / sleep blackout

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (!_settings.OffWhenLocked) return;
        if (e.Reason == SessionSwitchReason.SessionLock) EnterBlackout();
        else if (e.Reason == SessionSwitchReason.SessionUnlock) ExitBlackout();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            if (_settings.OffWhenSleeps) EnterBlackout();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            if (_settings.OffWhenSleeps) ExitBlackout();
            // Re-assert on wake regardless of the off-when-sleeps choice: ASUS Aura boards
            // (and some others) revert their zones to the firmware rainbow default on resume,
            // and streaming colours won't switch the mode back — only re-putting the device
            // into Direct mode (Prepare) does. Coalesce a burst of resume events into one.
            if (IsHandleCreated) { _resumeReassert.Stop(); _resumeReassert.Start(); }
        }
    }

    // ------------------------------------------------------ hotkeys + display state

    protected override void WndProc(ref Message m)
    {
        // Remove the OS title bar: claim the whole window as client area at the top, but
        // keep the native resize frame on the sides/bottom (so edge-resize, the system
        // drop shadow, Aero snap and the maximise animation all stay native). When
        // maximised the frame sits off the work area, so the top inset must stay to avoid
        // the content being clipped above the screen.
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            var p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
            int fx = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            int fy = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            // IsZoomed, not WindowState: WinForms refreshes its cached state only AFTER this message, so a
            // native maximise (double-click, snap, drag-restore) would otherwise get the old answer.
            bool maxed = IsZoomed(Handle);
            p.Proposed.Left += fx;
            p.Proposed.Right -= fx;
            p.Proposed.Bottom -= fy;
            p.Proposed.Top += maxed ? fy : 0; // drop the caption; keep the frame only when maximised
            Marshal.StructureToPtr(p, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        // The custom title strip: children answer HTTRANSPARENT over empty strip, so the hit lands here
        // as HTCLIENT; turn it into HTCAPTION (native drag / snap / double-click maximise) and give the
        // top edge back its resize band, which removing the caption took away.
        if (m.Msg == WM_NCHITTEST && _strip != null)
        {
            base.WndProc(ref m);
            if ((int)m.Result != HTCLIENT) return;
            // never m.LParam: negative multi-monitor coordinates overflow the int conversion
            m.Result = (IntPtr)ClientHitCode(PointToClient(Cursor.Position));
            return;
        }
        // Right-click on the strip: Prisma's quick menu, as the old header had (Alt+Space and Shift+F10
        // still open the Windows system menu - they arrive with lParam == -1).
        if (m.Msg == WM_CONTEXTMENU && _strip != null && m.LParam != (IntPtr)(-1))
        {
            long lp = m.LParam.ToInt64();
            var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var pt = PointToClient(screen);
            // wParam == the form: the press landed on the form surface itself (the gaps, the caption
            // band, the deck body); a strip child's message bubbles up with its own hwnd. Rail and
            // content children lie outside the strip and open their own menus - never a second one.
            if (m.WParam == Handle || _strip.Bounds.Contains(pt)) { ShowBackgroundMenu(this, pt); return; }
        }
        if (m.Msg == WM_HOTKEY)
        {
            switch ((int)m.WParam)
            {
                case HK_LIGHTS: ToggleLights(); break;
                case HK_PROFILE: CycleProfile(); break;
                case HK_RESCAN: if (!_detecting) _ = DetectDevicesAsync(); break;
            }
            return;
        }
        // The monitor going to sleep is a separate event from the session locking —
        // an idle timeout blanks the screen without ever locking the PC.
        if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_POWERSETTINGCHANGE &&
            _settings != null && _settings.OffWhenDisplaySleeps && m.LParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
            if (setting.PowerSetting == s_displayStateGuid)
            {
                if (setting.Data == 0) EnterBlackout();      // display off
                else if (setting.Data == 1) ExitBlackout();  // display on (2 = dimmed, ignored)
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>(Un)registers the system-wide hotkeys to match the setting.</summary>
    private void ApplyHotkeys()
    {
        if (!IsHandleCreated) return;
        UnregisterHotKey(Handle, HK_LIGHTS);
        UnregisterHotKey(Handle, HK_PROFILE);
        UnregisterHotKey(Handle, HK_RESCAN);
        if (!_settings.GlobalHotkeys) return;
        const uint MOD_NOREPEAT = 0x4000;   // a held hotkey fires once, never a burst of device writes
        bool ok = RegisterHotKey(Handle, HK_LIGHTS, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.L);
        ok &= RegisterHotKey(Handle, HK_PROFILE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.P);
        ok &= RegisterHotKey(Handle, HK_RESCAN, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.R);
        if (!ok) SetStatus("Some global hotkeys (Ctrl+Alt+L/P/R) are in use by another app", error: true);
    }

    /// <summary>Ctrl+Alt+L: everything off, or back to whatever ran before.</summary>
    private void ToggleLights()
    {
        if (_effect == Effect.Off)
            SelectEffect(_effectBeforeOff == Effect.Off ? Effect.Static : _effectBeforeOff);
        else
        {
            _effectBeforeOff = _effect;
            SelectEffect(Effect.Off);
        }
    }

    /// <summary>Applies the next saved profile (tray middle-click and Ctrl+Alt+P).</summary>
    private void CycleProfile()
    {
        if (_settings.Profiles.Count == 0) return;
        _profileCycle = (_profileCycle + 1) % _settings.Profiles.Count;
        ApplyProfile(_settings.Profiles[_profileCycle]);
    }

    private bool NightHoursActive()
    {
        if (!_settings.NightOffEnabled) return false;
        int h = DateTime.Now.Hour;
        int s = _settings.NightStartHour, e = _settings.NightEndHour;
        return s <= e ? h >= s && h < e : h >= s || h < e; // range may wrap midnight
    }

    private static double IdleMinutes()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime) / 60000.0;
    }

    // SystemEvents fire on their own thread; marshal device/UI work onto the form.
    private void EnterBlackout()
    {
        if (_blackedOut || !IsHandleCreated) return;
        _blackedOut = true;
        BeginInvoke(() => { _frameTimer.Stop(); _gpuReassert?.Stop(); BlackoutDevices(); });
    }

    private void ExitBlackout()
    {
        if (!_blackedOut || !IsHandleCreated) return;
        _blackedOut = false;
        BeginInvoke(() =>
        {
            if (_deviceRows.Count == 0) return;
            // If a scheduled night/idle off window is still active, stay dark — the frame loop
            // re-applies the effect itself once _autoOff clears. Re-lighting here would leave a
            // firmware effect on autonomously (the loop's `if (_autoOff) return` never turns it off).
            if (_autoOff) BlackoutDevices();
            else ApplyCurrentEffect();   // restores firmware effects on capable devices
            _frameTimer.Start();         // resumes software-rendered effects / auto-off transitions
        });
    }

    /// <summary>Re-puts every streamed device back into direct control and re-applies the
    /// current look. After wake-from-sleep an ASUS Aura board reverts its zones to the
    /// firmware rainbow default; <see cref="ApplyCurrentEffect"/> only re-Prepares a device
    /// that is LEAVING a firmware effect, so an always-streamed board (e.g. a static colour)
    /// would keep showing the rainbow. Re-Preparing it (the mode write) is what actually
    /// restores the set colour — the same thing an OpenRGB server restart achieves.</summary>
    private void ReassertLighting(string reason = "post-wake")
    {
        if (_deviceRows.Count == 0 || _blackedOut) return;
        DebugLog.Log($"reassert lighting ({reason})");
        ApplyCurrentEffect(); // re-decide firmware effects for hw-capable devices
        foreach (var row in _deviceRows)
            if (row.Checked && !_hwRows.Contains(row)) Try(() => row.Device.Prepare());
        // Prepare cleared each device's cached colour, so the running frame loop re-streams
        // it in Direct mode on the next tick.
    }

    /// <summary>Sends solid black to every enabled device once. Devices on a firmware effect
    /// are first returned to direct control so the black actually takes.</summary>
    private void BlackoutDevices()
    {
        foreach (var row in _deviceRows)
        {
            if (!row.Checked) continue;
            try
            {
                if (_hwRows.Contains(row)) Try(() => row.Device.Prepare());
                row.Device.SetColors(new uint[Math.Max(row.Device.LedCount, 1)]);
            }
            catch { }
        }
        _hwRows.Clear();
    }

    /// <summary>Maps a UI effect to a firmware effect kind, or null to render it in software.</summary>
    private static HwEffect? EffectToHw(Effect e) => e switch
    {
        Effect.RainbowWave => HwEffect.Rainbow,
        Effect.ColorCycle => HwEffect.Cycle,
        Effect.Breathing => HwEffect.Breathing,
        Effect.Strobe => HwEffect.Flashing,
        _ => null // Static, Off, Comet, Twinkle -> software
    };

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _settings.Effect = _effect.ToString();
        _settings.ColorArgb = _currentColor.ToArgb();
        _settings.Speed = _speedBar.Value;
        _settings.Brightness = _brightBar.Value;
        foreach (var row in _deviceRows)
            _settings.Devices[row.Key] = row.Checked;

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width > 0)
        {
            _settings.WinX = bounds.X;
            _settings.WinY = bounds.Y;
            _settings.WinW = bounds.Width;
            _settings.WinH = bounds.Height;
        }
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.Save();
    }

    /// <summary>The wireless mouse's reported name varies between scans; key it stably.</summary>
    private static string SettingsKey(IRgbDevice device) =>
        device is LogitechHidppDevice ? "logitech-hidpp-mouse" : device.Name;

    private void UpdateEffectButtons()
    {
        foreach (var btn in _effectButtons)
        {
            bool selected = (Effect)btn.Tag! == _effect;
            btn.Selected = selected;  // Modern: accent wash, resolved at paint time (never a baked colour)
            btn.Latched = selected;   // Classic: a held-down toolbar button
            btn.Invalidate();
        }
        UpdateHeaderButtons();
        UpdateContextualControls();
        UpdateDeviceChips();
        UpdateDeck();
    }

    /// <summary>Shows only the controls the current effect actually uses: the SPEED slider is
    /// hidden for motionless effects (Static / Off / Gradient) — where it did nothing but read
    /// a misleading value — and the Gradient-end colour appears only for the Gradient effect.
    /// The window height is fixed; brightness recentres and the COLOR action row reflows.</summary>
    private void UpdateContextualControls()
    {
        if (_speedBar == null || _actionRow == null) return;
        // SPEED is BLOCKED rather than hidden when nothing lit uses it: it stays in its row (nothing
        // reflows, brightness never jumps), reads as not-in-use, and its tooltip says why. It is read
        // per device: a row with its own effect may still animate, and Gradient sweeps A->B->A on a
        // single-LED device (the GPU) at this speed. Blocked keeps the slider enabled, so the tooltip
        // on its faded track works (a disabled child window never sees the hover).
        var lit = _deviceRows.Where(r => r.Checked).ToList();
        bool usesSpeed = lit.Count == 0 ? UsesSpeed(_effect, 2) : lit.Any(r => UsesSpeed(RowEffect(r), r.Device.LedCount));
        _speedBar.Blocked = !usesSpeed;
        _speedLabel.Blocked = !usesSpeed;
        _speedValue.Visible = usesSpeed;
        _speedLabel.Text = _effect == Effect.Music ? "Sensitivity" : "Speed";   // Music reads SPEED as sensitivity
        string why = usesSpeed
            ? _effect == Effect.Gradient ? "Sweep speed for single-LED devices" : ""
            : _effect switch
            {
                Effect.Ambient => "Screen sync follows the screen; speed is not used",
                Effect.Gradient => "Gradient is a fixed blend on multi-LED devices",
                Effect.Off => "The lights are off",
                _ => $"{EffectLabel(_effect)} has no speed",
            };
        _tips.SetToolTip(_speedBar, why); _tips.SetToolTip(_speedLabel, why);

        bool usesGradientEnd = _effect == Effect.Gradient;
        if (_gradientShown != usesGradientEnd || _gradientBtn.Visible != usesGradientEnd && Visible)
        {
            _gradientShown = usesGradientEnd;
            _gradientBtn.Visible = usesGradientEnd;
            LayoutActionRow();
        }

        // Self-colouring effects ignore the pickers — grey them out (no collapse/resize) + note.
        // The target dropdown greys too: it only scopes colour picks, which are inert here.
        // Self-colouring = the renderer generates its own hues and ignores the picked colour.
        // NOT Music: it fills the VU meter with the PICKED colour (EffectiveColor), so its pickers
        // must stay live. Surprise DOES self-colour (random cross-fade) — grey its pickers so a
        // swatch click can't silently yank the effect to Static (SetColor's self-colour list).
        bool autoColor = _effect is Effect.RainbowWave or Effect.ColorCycle or Effect.Fire or Effect.Ambient or Effect.Surprise;
        _swatchGrid.Enabled = _hueBar.Enabled = _hueValue.Enabled = _actionRow.Enabled = !autoColor;
        _targetButton.Enabled = !autoColor;
        _targetButton.Visible = !autoColor;   // the note takes the chip's place on the caption band
        // ...but it must not hide WHICH device the pickers would act on
        string? target = _colorTarget == null ? null : _deviceRows.FirstOrDefault(r => r.Key == _colorTarget)?.Device.Name;
        _autoColorNote.Text = target == null ? "Colors are automatic for this effect" : $"Automatic colors · target: {target}";
        _autoColorNote.Visible = autoColor;
        _hueLabel.Blocked = autoColor;
    }

    /// <summary>Whether an effect reads the SPEED slider on a device with <paramref name="leds"/> LEDs.
    /// Screen sync shares Static's fixed-rate fade; Gradient only moves on a single LED.</summary>
    private static bool UsesSpeed(Effect e, int leds) => e switch
    {
        Effect.Static or Effect.Off or Effect.Ambient => false,
        Effect.Gradient => leds <= 1,
        _ => true,
    };

    /// <summary>Percent columns dump the integer remainder on the LAST column (the 3rd effect
    /// tile rendered 8px wider than its siblings, the last swatch 2px). Absolute widths with the
    /// remainder spread 1px-per-column keep siblings equal.</summary>
    private static void EqualizeColumns(TableLayoutPanel t)
    {
        if (t.ColumnCount == 0 || t.Width <= 0) return;
        int cols = t.ColumnCount, baseW = t.Width / cols, rem = t.Width % cols;
        t.SuspendLayout();
        t.ColumnStyles.Clear();
        for (int c = 0; c < cols; c++)
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, baseW + (c < rem ? 1 : 0)));
        t.ResumeLayout();
    }

    /// <summary>EqualizeColumns now and again on every resize (the cards stretch with the window
    /// up to RIGHT_COL_MAX, so absolute column widths must follow).</summary>
    private static void EqualizeColumnsLive(TableLayoutPanel t)
    {
        EqualizeColumns(t);
        t.SizeChanged += (s, _) => EqualizeColumns((TableLayoutPanel)s!);
    }

    /// <summary>Tints the header quick-action glyphs to reflect live state: lights on,
    /// screen sync active, window pinned.</summary>
    private void UpdateHeaderButtons()
    {
        if (_lightsBtn == null) return;
        _lightsBtn.On = _effect != Effect.Off;
        _screenBtn.Active = _effect == Effect.Ambient;
        _pinBtn.Active = TopMost;
        // the state in words too, not only in the accent chip
        _tips.SetToolTip(_screenBtn, _screenBtn.Active ? "Screen sync: on — click to turn off" : "Screen sync: off — click to turn on");
        _tips.SetToolTip(_pinBtn, TopMost ? "Keep window on top: on" : "Keep window on top: off");
    }

    /// <summary>Swap the caption maximise/restore glyph + tooltip to match the window state.</summary>
    private void UpdateMaxGlyph()
    {
        if (_maxBtn == null) return;
        bool max = WindowState == FormWindowState.Maximized;
        _maxBtn.Maximized = max;
        _tips.SetToolTip(_maxBtn, max ? "Restore" : "Maximize");
        _strip?.Relayout();   // Classic: the 95 frame (and its inset) goes away when maximised
    }

    /// <summary>The gradient-end button is its own swatch: filled with the end color,
    /// label flipped light/dark for contrast.</summary>
    private void UpdateGradientButton()
    {
        var c = Color.FromArgb(_settings.GradientEndArgb);
        _gradientBtn.FillColor = c;
        _gradientBtn.HoverFillColor = c;
        // perceived luminance, not HSL lightness: saturated yellow/cyan need black text
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        _gradientBtn.ForeColor = lum > 0.55 ? Color.Black : Color.White;
        _gradientBtn.Invalidate();
    }

    private void SetColor(Color c)
    {
        if (_applyingProfile == 0 && _appliedProfile != null) MarkAppliedProfile(null);
        if (_colorTarget == null)
        {
            _currentColor = c;
            _settings.DeviceColors.Clear(); // "all devices": everyone follows the global color
        }
        else
        {
            _settings.DeviceColors[_colorTarget] = c.ToArgb();
        }

        if (_hueBar != null && !_syncingHue && c.GetSaturation() > 0) // greys keep the slider's hue
        {
            _syncingHue = true;
            _hueBar.Value = (int)c.GetHue();
            _hueValue.Text = $"{_hueBar.Value}°";
            _syncingHue = false;
        }
        // picking a color implies you want to see it: jump to static unless an animated color effect is active
        if (_effect is Effect.RainbowWave or Effect.ColorCycle or Effect.Off
                    or Effect.Ambient or Effect.Surprise or Effect.Fire)
            SelectEffect(Effect.Static);
        // A firmware colour effect (Breathing on the GPU) baked the old colour at apply
        // time; RenderFrame skips _hwRows so it never picks up the change. Re-assert it.
        else if (_hwRows.Any(r => EffectToHw(RowEffect(r)) == HwEffect.Breathing))
            ScheduleGpuReassert();
        UpdateSwatchSelection();
        UpdateDeviceChips();
        UpdateTrayIcon();
        SaveSoon();
    }

    /// <summary>The color currently being edited: the global color, or the targeted device's override.</summary>
    private Color ActivePickColor =>
        _colorTarget != null && _settings.DeviceColors.TryGetValue(_colorTarget, out int argb)
            ? Color.FromArgb(argb) : _currentColor;

    private Color EffectiveColor(DeviceRow row) =>
        _settings.DeviceColors.TryGetValue(row.Key, out int argb) ? Color.FromArgb(argb) : _currentColor;

    private void UpdateDeviceChips()
    {
        var end = Color.FromArgb(_settings.GradientEndArgb);
        foreach (var row in _deviceRows)
        {
            var c = EffectiveColor(row);
            var fx = RowEffect(row);
            if (row.DisplayColor.ToArgb() == c.ToArgb() && row.ArtEffect == fx && row.ArtEnd.ToArgb() == end.ToArgb())
                continue; // tile unchanged; skip the repaint
            row.DisplayColor = c;
            row.ArtEffect = fx;   // the tile shows what the device really runs, not a stale base colour
            row.ArtEnd = end;
            row.Invalidate();
        }
        foreach (var grp in _deviceGroups) grp.Invalidate();
    }

    private void SetColorTarget(string? key)
    {
        _colorTarget = key;
        string label = "All devices";
        if (key != null)
        {
            var match = _deviceRows.FirstOrDefault(r => r.Key == key);
            if (match == null) { _colorTarget = null; key = null; }
            else label = match.Device.Name;
        }
        _targetButton.Text = $"Applies to: {label}  ▾";
        _targetButton.Selected = key != null;   // a specific device: accent wash on the chip
        LayoutContent();
        foreach (var row in _deviceRows) { row.Targeted = key != null && row.Key == key; row.Invalidate(); }

        // reflect the target's current color in the hue slider + swatch selection
        var c = ActivePickColor;
        if (_hueBar != null && c.GetSaturation() > 0) // greys keep the slider's hue
        {
            _syncingHue = true;
            _hueBar.Value = (int)c.GetHue();
            _hueValue.Text = $"{_hueBar.Value}°";
            _syncingHue = false;
        }
        UpdateSwatchSelection();
    }

    /// <summary>Escapes user/device-supplied text for menu items, where a bare '&'
    /// would become a mnemonic underline and vanish.</summary>
    private static string MenuText(string s) => s.Replace("&", "&&");

    private void ShowTargetMenu()
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Header("Color applies to"));
        menu.Items.Add(Menus.Item("All devices", null, (_, _) => SetColorTarget(null), isChecked: _colorTarget == null));
        if (_deviceRows.Count > 0) menu.Items.Add(new ToolStripSeparator());
        foreach (var row in _deviceRows)
        {
            bool isTarget = _colorTarget == row.Key;
            var item = new ToolStripMenuItem(MenuText(row.Device.Name)) { Checked = isTarget };
            if (!isTarget) item.Image = Menus.Dot(EffectiveColor(row));
            item.Click += (_, _) => SetColorTarget(row.Key);
            menu.Items.Add(item);
        }
        menu.Show(_targetButton, new Point(0, _targetButton.Height));
    }

    private void SaveCurrentColor()
    {
        int argb = ActivePickColor.ToArgb();
        _settings.SavedColors.RemoveAll(a => a == argb);
        _settings.SavedColors.Add(argb);
        while (_settings.SavedColors.Count > SwatchCols) _settings.SavedColors.RemoveAt(0);
        RebuildSavedSwatches();
        SaveSoon();
    }

    private void RebuildSavedSwatches()
    {
        foreach (var b in _savedSwatchButtons) { _swatchGrid.Controls.Remove(b); b.Dispose(); }
        _savedSwatchButtons.Clear();
        for (int i = 0; i < _settings.SavedColors.Count && i < SwatchCols; i++)
        {
            var c = Color.FromArgb(_settings.SavedColors[i]);
            var sw = MakeSwatch(c);
            sw.Click += (s, _) => SetColor((Color)((RoundedButton)s!).Tag!);
            sw.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowSwatchMenu((RoundedButton)s!, (Color)((RoundedButton)s!).Tag!, saved: true); };
            _savedSwatchButtons.Add(sw);
            _swatchGrid.Controls.Add(sw);
        }
        if (LayoutSwatches()) LayoutContent();   // a first / last saved colour adds or drops the second row
        UpdateSwatchSelection();
    }

    /// <summary>Re-applies settings changed in the dialog (frame rate, accent theme) to the live UI.</summary>
    private void SyncEffectTuning()
    {
        s_ringLeds = Math.Clamp(_settings.FanRingLeds, 4, 32);
        s_waveReverse = _settings.WaveReverse;
        s_ambientBoost = _settings.AmbientVividness switch { 0 => 1.0, 2 => 1.8, _ => 1.4 };
        s_gradientEnd = Color.FromArgb(_settings.GradientEndArgb);
        OpenRgbRemoteDevice.GpuFollowExternal = _settings.GpuExternalControl;
    }

    /// <summary>Switches the Windows 95 skin on or off in place. Deliberately NOT a restart (which
    /// is how the sibling apps do it): restarting Prisma re-detects every device, and a fresh SMBus/I2C
    /// probe round is exactly what this app exists to avoid. Restyle carries the baked properties over;
    /// everything else reads Theme inside OnPaint and follows on the next Invalidate.</summary>
    private void ApplySkinChange()
    {
        if (Theme.Classic == _settings.ClassicSkin) return;
        Theme.SetSkin(_settings.ClassicSkin);
        Backdrop.Invalidate();
        foreach (Form f in Application.OpenForms.Cast<Form>().ToArray()) Restyle.Apply(f);
        _deviceList.Restyle();                     // the opaque list surfaces: white field / rail, edge, bar
        _profileList.Restyle();
        MinimumSize = OuterSizeFor(new Size(MIN_CW, MinClientH));
        ApplyWindowChrome();
        Opacity = EffectiveOpacity();              // Classic: always opaque
        Backdrop.Glass = _settings.GlassEffect && !Theme.Classic;
        _strip.Relayout();
        _blockLeft = -1;                           // force the wallpaper-slice repaint
        LayoutColumns();
        if (WindowState != FormWindowState.Minimized) LayoutLeftColumn();
        FitDeviceRows();
        UpdateEffectButtons();
        UpdateSwatchSelection();
        UpdateGradientButton();                    // Restyle remaps its literal black/white label
        UpdateContextualControls();
        UpdateTrayIcon();
        Invalidate(true);
    }

    /// <summary>The opacity slider is suspended while liquid glass is on: a layered
    /// (semi-transparent) window breaks the DWM acrylic accent rendering.</summary>
    private double EffectiveOpacity() =>
        Harness && HarnessGhost ? 0.02 :
        Theme.Classic || _settings.GlassEffect ? 1.0 : Math.Clamp(_settings.WindowOpacity, 75, 100) / 100.0;

    private void ApplySettingsChanged()
    {
        _settings.Save();
        ApplySkinChange();
        SyncEffectTuning();
        ApplyHotkeys();
        Backdrop.Intensity = Math.Clamp(_settings.BackdropGlow, 0, 100);
        Backdrop.Glass = _settings.GlassEffect;
        Backdrop.Invalidate(); // accent/glow may have changed; the gradient derives from them
        // Re-write the GPU mode only when the external-control choice actually flipped —
        // settings fire on every slider tick and the GPU MCU hates mode-write spam.
        if (_gpuExternalApplied != _settings.GpuExternalControl)
        {
            _gpuExternalApplied = _settings.GpuExternalControl;
            foreach (var row in _deviceRows)
                if (row.Device is OpenRgbRemoteDevice { Kind: "GPU" } gpu && !_hwRows.Contains(row))
                    Try(() => gpu.Prepare());
        }
        // Toggling "leave the mouse to G HUB" changes the detected device set, so re-detect
        // to add or drop the mouse row immediately rather than at the next launch/Rescan.
        // Only latch the applied state when the re-detect actually fires: if a detection is
        // already in flight it captured the old value, so keep the mismatch so the next
        // settings change re-triggers instead of leaving the mouse row silently stale.
        if (_ignoreLogitechApplied != _settings.IgnoreLogitechMouse && !_detecting)
        {
            _ignoreLogitechApplied = _settings.IgnoreLogitechMouse;
            _ = DetectDevicesAsync();
        }
        ApplyWindowChrome();                       // glass: Modern only
        Opacity = EffectiveOpacity();              // opacity: Modern only
        Backdrop.Glass = _settings.GlassEffect && !Theme.Classic;
        _frameTimer.Interval = Math.Max(20, 1000 / Math.Clamp(_settings.FrameRate, 5, 30));
        UpdateEffectButtons();
        UpdateSwatchSelection();
        UpdateDeviceChips();
        Invalidate(true);
    }

    private void UpdateSwatchSelection()
    {
        int active = ActivePickColor.ToArgb();
        foreach (var sw in _swatchButtons.Concat(_savedSwatchButtons))
        {
            bool selected = ((Color)sw.Tag!).ToArgb() == active;
            if (sw.Selected == selected) continue;
            sw.Selected = selected;
            sw.Invalidate();
        }
        // the tiles paint their art from the picked colour, and so does the deck
        foreach (var t in _effectButtons) t.Invalidate();
        UpdateDeck();
    }

    // ---------------------------------------------------------------- tray

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Prisma", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());

        _trayEffectMenu = new ToolStripMenuItem("Effect");
        foreach (Effect e in Enum.GetValues<Effect>())
        {
            var item = new ToolStripMenuItem(EffectLabel(e)) { Tag = e };
            item.Click += (s, _) => SelectEffect((Effect)((ToolStripMenuItem)s!).Tag!);
            _trayEffectMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(_trayEffectMenu);

        _trayColorMenu = new ToolStripMenuItem("Color");
        var quickColors = new (string Name, Color C)[]
        {
            ("Red", Color.FromArgb(255, 0, 0)), ("Green", Color.FromArgb(0, 255, 0)),
            ("Blue", Color.FromArgb(0, 0, 255)), ("Teal", Color.FromArgb(0, 200, 170)),
            ("White", Color.White)
        };
        foreach (var (name, c) in quickColors)
        {
            var item = new ToolStripMenuItem(name) { Tag = c };
            item.Click += (s, _) => SetColor((Color)((ToolStripMenuItem)s!).Tag!);
            _trayColorMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(_trayColorMenu);

        _trayProfileMenu = new ToolStripMenuItem("Profiles");
        menu.Items.Add(_trayProfileMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _exitRequested = true; Close(); });

        Theme.StyleMenu(menu);
        menu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in _trayEffectMenu.DropDownItems)
                item.Checked = (Effect)item.Tag! == _effect;
            foreach (ToolStripMenuItem item in _trayColorMenu.DropDownItems)
                item.Checked = ((Color)item.Tag!).ToArgb() == _currentColor.ToArgb();
            FillProfileItems(_trayProfileMenu.DropDownItems); // rebuilt each open: list changes at runtime
        };

        Icon? trayIcon = null;
        try { trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        _tray = new NotifyIcon
        {
            Icon = trayIcon ?? SystemIcons.Application,
            Text = "Prisma",
            Visible = !Harness,   // the live instance already owns the tray
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        // Middle-click on the tray icon cycles through saved profiles.
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Middle) CycleProfile();
        };
    }

    // ---------------------------------------------------------------- game-triggered profiles

    /// <summary>Profiles can name a trigger process ("cs2"): while it runs, the profile
    /// is auto-applied; when it exits, the lighting returns to what it was before.
    /// Polled from the 10s watchdog tick.</summary>
    private void CheckProfileTriggers()
    {
        if (_detecting || _settings.Profiles.Count == 0) return;
        var triggers = _settings.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.TriggerProcess)).ToList();
        if (triggers.Count == 0) return;

        LightingProfile? running = null;
        var procs = Process.GetProcesses();
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in procs) { try { names.Add(p.ProcessName); } catch { } }
            running = triggers.FirstOrDefault(p =>
                names.Contains(Path.GetFileNameWithoutExtension(p.TriggerProcess.Trim())));
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }

        if (running != null && _autoProfileActive != running.Name)
        {
            DebugLog.Log($"trigger: '{running.TriggerProcess}' running -> profile '{running.Name}'");
            if (_preAutoSnapshot == null)
            {
                _preAutoSnapshot = SnapshotCurrent("Before game");
                _preAutoApplied = _appliedProfile;   // restored with the look when the game exits
            }
            _autoProfileActive = running.Name;
            ApplyProfile(running);
        }
        else if (running == null && _autoProfileActive != null)
        {
            DebugLog.Log("trigger gone -> restoring previous lighting");
            _autoProfileActive = null;
            var snapshot = _preAutoSnapshot;
            _preAutoSnapshot = null;
            if (snapshot != null)
            {
                ApplyProfile(snapshot);
                MarkAppliedProfile(_preAutoApplied);   // the rail shows the profile that was showing before
            }
            _preAutoApplied = null;
        }
    }

    // ---------------------------------------------------------------- per-device effects

    /// <summary>Right-click menu on a device row: run a different effect on just this
    /// device, or follow the global selection again.</summary>
    private void ShowDeviceMenu(DeviceRow row, Point? at = null)
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Header(MenuText(row.Device.Name)));

        menu.Items.Add(Menus.Item(row.Checked ? "Turn off" : "Turn on", "", (_, _) => ToggleDevice(row)));
        menu.Items.Add(new ToolStripSeparator());

        var setColor = new ToolStripMenuItem("Set color…") { Image = Menus.Dot(EffectiveColor(row)) };
        setColor.Click += (_, _) =>
        {
            using var dlg = new ColorPickerDialog(EffectiveColor(row));
            if (dlg.ShowDialog(this) == DialogResult.OK) SetDeviceColor(row, dlg.SelectedColor);
        };
        menu.Items.Add(setColor);
        menu.Items.Add(Menus.Item("Use as color target", null, (_, _) => SetColorTarget(row.Key), isChecked: _colorTarget == row.Key));
        menu.Items.Add(Menus.Item("Identify (flash)", "", (_, _) => IdentifyDevice(row)));
        menu.Items.Add(new ToolStripSeparator());

        // Effect ▸ submenu: Follow global, then every effect (the active one checked)
        var fx = Menus.Submenu("Effect", "");
        bool hasOverride = _settings.DeviceEffects.ContainsKey(row.Key);
        fx.DropDownItems.Add(Menus.Item("Follow global effect", null,
            (_, _) => { _settings.DeviceEffects.Remove(row.Key); RowEffectChanged(row); }, isChecked: !hasOverride));
        fx.DropDownItems.Add(new ToolStripSeparator());
        foreach (Effect e in Enum.GetValues<Effect>())
        {
            var ef = e;
            fx.DropDownItems.Add(Menus.Item(EffectLabel(ef), null,
                (_, _) => { _settings.DeviceEffects[row.Key] = ef.ToString(); RowEffectChanged(row); },
                isChecked: hasOverride && RowEffect(row) == ef));
        }
        menu.Items.Add(fx);
        menu.Items.Add(new ToolStripSeparator());

        var reset = Menus.Item("Reset to global", "", (_, _) =>
        {
            _settings.DeviceColors.Remove(row.Key);
            _settings.DeviceEffects.Remove(row.Key);
            UpdateDeviceChips();
            RowEffectChanged(row);
        });
        reset.Enabled = _settings.DeviceColors.ContainsKey(row.Key) || hasOverride;
        menu.Items.Add(reset);

        string zones = row.Device.LedCount == 1 ? "1 zone" : $"{row.Device.LedCount} zones";
        menu.Items.Add(Menus.Header($"{zones}  ·  {row.Device.Source}"));

        menu.Show(row, at ?? row.PointToClient(Cursor.Position));
    }

    /// <summary>Toggles a device's enable state from the menu (mirrors a checkbox click).</summary>
    private void ToggleDevice(DeviceRow row)
    {
        row.SetChecked(!row.Checked);
        _settings.Devices[row.Key] = row.Checked;
        if (!row.Checked) BlackoutRow(row);
        RefreshDeviceGroups();
        LookEdited();
        SaveSoon();
        ApplyCurrentEffect();
        UpdateContextualControls();
    }

    /// <summary>Repaints every RAM-group header so its shared circle (all / mixed-dash / off)
    /// reflects member changes made via DeviceRow.SetChecked — which deliberately does NOT raise
    /// CheckedChanged, so the header's own invalidation (wired to CheckedChanged) never fires.</summary>
    private void RefreshDeviceGroups() { foreach (var grp in _deviceGroups) grp.Invalidate(); }

    private void BlackoutRow(DeviceRow row)
    {
        try
        {
            if (_hwRows.Contains(row)) Try(() => row.Device.Prepare());
            row.Device.SetColors(new uint[Math.Max(1, row.Device.LedCount)]);
        }
        catch { }
    }

    /// <summary>Sets one device's colour override (independent of the global target).</summary>
    private void SetDeviceColor(DeviceRow row, Color c)
    {
        LookEdited();
        _settings.DeviceColors[row.Key] = c.ToArgb();
        UpdateDeviceChips();
        UpdateSwatchSelection();
        if (_hwRows.Contains(row)) ScheduleGpuReassert(); // firmware colour effect: re-assert
        SaveSoon();
    }

    /// <summary>Briefly blinks one device white so the user can spot it physically.</summary>
    private void IdentifyDevice(DeviceRow row)
    {
        if (!row.Checked) { row.SetChecked(true); _settings.Devices[row.Key] = true; RefreshDeviceGroups(); }
        Try(() => row.Device.Prepare()); // take direct control for the blink
        _hwRows.Remove(row);             // so the render loop streams the blink to it
        _identifyRow = row;
        _identifyUntil = _clock.Elapsed.TotalSeconds + 3.0;
        if (!_frameTimer.Enabled) _frameTimer.Start();
    }

    private void PickColorInto(Action<Color> apply, Color seed)
    {
        using var dlg = new ColorPickerDialog(seed);
        if (dlg.ShowDialog(this) == DialogResult.OK) apply(dlg.SelectedColor);
    }

    /// <summary>Right-click an effect tile: apply it everywhere, or set the colour it uses.</summary>
    private void ShowEffectMenu(Control anchor, Effect effect)
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Header(EffectLabel(effect)));
        menu.Items.Add(Menus.Item("Apply to all devices", null, (_, _) =>
        {
            _settings.DeviceEffects.Clear();
            foreach (var r in _deviceRows) r.EffectNote = null;
            SelectEffect(effect);
        }));
        var setColor = new ToolStripMenuItem("Set color…") { Image = Menus.Dot(_currentColor) };
        setColor.Click += (_, _) => PickColorInto(SetColor, ActivePickColor);
        menu.Items.Add(setColor);
        menu.Show(anchor, anchor.PointToClient(Cursor.Position));
    }

    /// <summary>Right-click a colour swatch: apply, set as gradient end, save or remove.</summary>
    private void ShowSwatchMenu(Control anchor, Color color, bool saved)
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Header($"#{color.R:X2}{color.G:X2}{color.B:X2}"));
        var apply = new ToolStripMenuItem("Apply") { Image = Menus.Dot(color) };
        apply.Click += (_, _) => SetColor(color);
        menu.Items.Add(apply);
        menu.Items.Add(Menus.Item("Use as gradient end", null, (_, _) =>
        {
            _settings.GradientEndArgb = color.ToArgb();
            SyncEffectTuning();
            UpdateGradientButton();
            UpdateSwatchSelection();
            UpdateDeviceChips();
            LookEdited();
            SaveSoon();
            if (_effect != Effect.Gradient) SelectEffect(Effect.Gradient);
        }));
        if (!saved)
            menu.Items.Add(Menus.Item("Save to my colors", "", (_, _) => { SetColor(color); SaveCurrentColor(); }));
        else
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Menus.Item("Remove", "", (_, _) =>
            {
                _settings.SavedColors.Remove(color.ToArgb());
                RebuildSavedSwatches();
                SaveSoon();
            }));
        }
        menu.Show(anchor, anchor.PointToClient(Cursor.Position));
    }

    /// <summary>Right-click the empty window background: a compact quick-control menu.</summary>
    private void ShowBackgroundMenu(Control anchor, Point loc)
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Item(_effect == Effect.Off ? "Turn lights on" : "Turn lights off", "", (_, _) => ToggleLights()));

        var fx = Menus.Submenu("Effect", "");
        foreach (Effect e in Enum.GetValues<Effect>())
        {
            var ef = e;
            fx.DropDownItems.Add(Menus.Item(EffectLabel(ef), null, (_, _) => SelectEffect(ef), isChecked: _effect == ef));
        }
        menu.Items.Add(fx);

        var color = new ToolStripMenuItem("Pick color…") { Image = Menus.Dot(_currentColor) };
        color.Click += (_, _) => PickColorInto(SetColor, ActivePickColor);
        menu.Items.Add(color);

        var profiles = Menus.Submenu("Profiles", "");
        FillProfileItems(profiles.DropDownItems);
        menu.Items.Add(profiles);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menus.Item("Settings…", "", (_, _) =>
        {
            SaveNow();
            using var dlg = new SettingsForm(_settings, ApplySettingsChanged);
            dlg.ShowDialog(this);
        }));
        menu.Show(anchor, loc);
    }

    // ------------------------------------------------------ about popup (rainbow title)

    /// <summary>Toggles the About panel under the rainbow title (re-click closes it).</summary>
    private void ShowAboutPanel()
    {
        if (_about is { IsDisposed: false }) { _about.Close(); _about = null; return; }
        // Clicking the wordmark while About is open: Deactivate already closed it on mouse-down, so
        // this click must not reopen it - that is the toggle closing it.
        if (Environment.TickCount64 - _aboutClosedAt < 300) return;
        _about = new AboutPopup(BuildAboutInfo());
        var p = _title.PointToScreen(new Point(0, _title.Height + 2));
        var wa = Screen.FromControl(this).WorkingArea; // keep it on-screen near an edge
        p.X = Math.Max(wa.Left + 8, Math.Min(p.X, wa.Right - _about.Width - 8));
        p.Y = Math.Min(p.Y, wa.Bottom - _about.Height - 8);
        _about.Location = p;
        _about.FormClosed += (_, _) => { _about = null; _aboutClosedAt = Environment.TickCount64; };
        _about.Show(this); // owned, not modal: never interrupts the lighting; Deactivate closes it
    }

    private AboutInfo BuildAboutInfo()
    {
        string version = Application.ProductVersion.Split('+')[0];
        DateTime built;
        try { built = File.GetLastWriteTime(Environment.ProcessPath!); }
        catch { built = DateTime.Now; }

        var lit = _deviceRows.Where(r => r.Checked).ToList();
        bool musicActive = _deviceRows.Any(r => r.Checked && RowEffect(r) == Effect.Music);
        var amber = Color.FromArgb(255, 180, 60);

        var info = new AboutInfo
        {
            Version = version,
            BuildLine = $"Built {built:yyyy-MM-dd}  ·  .NET {Environment.Version}  ·  Windows {Environment.OSVersion.Version.Build}",
            Effect = EffectLabel(_effect),
            CurrentColor = _currentColor,
            LightsOff = _effect == Effect.Off,
            ColorHex = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}"
                       + (_settings.DeviceEffects.Count > 0 ? $"  ·  {_settings.DeviceEffects.Count} overrides" : ""),
            DevicesSummary = $"{lit.Count} of {_deviceRows.Count} lit",
            TotalLeds = lit.Sum(r => r.Device.LedCount),
            FrameRate = _settings.FrameRate,
            RenderMode = $"{_hwRows.Count} firmware · {Math.Max(0, lit.Count - _hwRows.Count)} streamed",
            StatusText = _status.Text,
            Tuning = $"{_brightBar.Value}% bright · {_speedBar.Value}% speed",
            ProfileLine = (_autoProfileActive ?? "Custom") + $"  ·  {_settings.Profiles.Count} saved",
            Uptime = Humanize(_clock.Elapsed),
        };
        foreach (var r in _deviceRows)
            info.Devices.Add(new AboutDevice(r.Device.Name, r.Device.Kind,
                $"{(r.Device.LedCount == 1 ? "1 zone" : r.Device.LedCount + " zones")} · {r.Device.Source}",
                EffectiveColor(r), r.Checked));

        info.Backends.Add(new AboutPill(_openRgb.Connected ? "OpenRGB SDK v4" : "OpenRGB offline",
            _openRgb.Connected ? Color.FromArgb(70, 200, 120) : Theme.ErrorCol));
        if (_deviceRows.Any(r => r.Device is LogitechHidppDevice)) info.Backends.Add(new AboutPill("Logitech HID++", Theme.Accent));
        if (_deviceRows.Any(r => r.Device is GigabyteFusion2Device)) info.Backends.Add(new AboutPill("Gigabyte Fusion", Theme.Accent));
        if (musicActive) info.Backends.Add(new AboutPill("WASAPI audio", amber));

        if (_blackedOut) info.Special = new AboutPill("Blacked out (locked)", Theme.ErrorCol);
        else if (_autoOff) info.Special = new AboutPill("Auto-off", amber);
        else if (_nightDim) info.Special = new AboutPill("Night dim", Color.FromArgb(150, 130, 255));
        else if (_autoProfileActive != null) info.Special = new AboutPill($"Game profile: {_autoProfileActive}", Theme.Accent);

        return info;
    }

    private static string Humanize(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
        : $"{t.Seconds}s";

    private void RowEffectChanged(DeviceRow row)
    {
        row.EffectNote = _settings.DeviceEffects.TryGetValue(row.Key, out var name) && Enum.TryParse(name, out Effect e)
            ? EffectLabel(e) : null;
        row.Invalidate();
        LookEdited();
        SaveSoon();
        ApplyCurrentEffect();
        UpdateDeviceChips();          // the row's tile shows its new effect
        UpdateContextualControls();   // ...which may (not) use SPEED
    }

    // ---------------------------------------------------------------- profiles

    private static Bitmap ColorDot(Color c)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 2, 2, 12, 12);
        return bmp;
    }

    /// <summary>Populates a Profiles menu: saved profiles (with their color as a dot),
    /// save-current, and a delete submenu. Shared by the header button and the tray.</summary>
    private void FillProfileItems(ToolStripItemCollection items)
    {
        items.Clear();
        if (_settings.Profiles.Count == 0)
            items.Add(new ToolStripMenuItem("(no profiles saved yet)") { Enabled = false });
        foreach (var profile in _settings.Profiles)
        {
            var item = new ToolStripMenuItem(MenuText(profile.Name), ColorDot(Color.FromArgb(profile.ColorArgb))) { Tag = profile };
            item.Click += (s, _) => ApplyProfile((LightingProfile)((ToolStripMenuItem)s!).Tag!);
            items.Add(item);
        }
        items.Add(new ToolStripSeparator());
        var save = new ToolStripMenuItem("Save current as profile...");
        save.Click += (_, _) => SaveCurrentAsProfile();
        items.Add(save);
        if (_settings.Profiles.Count > 0)
        {
            var del = new ToolStripMenuItem("Delete profile");
            foreach (var profile in _settings.Profiles)
            {
                var d = new ToolStripMenuItem(MenuText(profile.Name)) { Tag = profile };
                d.Click += (s, _) =>
                {
                    _settings.Profiles.Remove((LightingProfile)((ToolStripMenuItem)s!).Tag!);
                    _profileCycle = -1;
                    _settings.Save();
                    RebuildProfileRows();
                };
                del.DropDownItems.Add(d);
            }
            items.Add(del);
        }
        // Items created after StyleMenu's one-time pass (tray rebuilds this dropdown on
        // every open) would otherwise come up in the default light colors.
        foreach (ToolStripItem item in items) Theme.StyleMenuItem(item);
    }

    private void SaveCurrentAsProfile()
    {
        var result = PromptProfileSave();
        if (result == null || string.IsNullOrWhiteSpace(result.Value.Name)) return;
        var (name, trigger) = result.Value;
        _settings.Profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); // overwrite by name
        var profile = SnapshotCurrent(name);
        profile.TriggerProcess = trigger;
        _settings.Profiles.Add(profile);
        _settings.Save();
        _appliedProfile = name;   // it IS the current look
        RebuildProfileRows();
        try { _tray.ShowBalloonTip(1500, "Prisma", $"Profile \"{name}\" saved", ToolTipIcon.Info); } catch { }
    }

    /// <summary>Captures the complete current lighting state as a profile.</summary>
    private LightingProfile SnapshotCurrent(string name) => new()
    {
        Name = name,
        Effect = _effect.ToString(),
        ColorArgb = _currentColor.ToArgb(),
        GradientEndArgb = _settings.GradientEndArgb,
        Speed = _speedBar.Value,
        Brightness = _brightBar.Value,
        DeviceColors = new Dictionary<string, int>(_settings.DeviceColors),
        DeviceEffects = new Dictionary<string, string>(_settings.DeviceEffects),
        Devices = _deviceRows.ToDictionary(r => r.Key, r => r.Checked)
    };

    private void ApplyProfile(LightingProfile p)
    {
        DebugLog.Log($"apply profile '{p.Name}'");
        _applyingProfile++;
        try { ApplyProfileCore(p); }
        finally { _applyingProfile--; }
        MarkAppliedProfile(p.Name);
    }

    private void ApplyProfileCore(LightingProfile p)
    {
        _colorTarget = null;
        SetColor(Color.FromArgb(p.ColorArgb));                          // global color (syncs hue bar + preview)
        _settings.DeviceColors = new Dictionary<string, int>(p.DeviceColors); // then restore per-device overrides
        _settings.DeviceEffects = new Dictionary<string, string>(p.DeviceEffects);
        foreach (var row in _deviceRows)
            row.EffectNote = p.DeviceEffects.TryGetValue(row.Key, out var fxName) && Enum.TryParse(fxName, out Effect fx)
                ? EffectLabel(fx) : null;
        if (p.GradientEndArgb != 0) // 0 = profile saved before gradients existed
        {
            _settings.GradientEndArgb = p.GradientEndArgb;
            SyncEffectTuning();
            UpdateGradientButton();
        }
        _speedBar.Value = Math.Clamp(p.Speed, _speedBar.Minimum, _speedBar.Maximum);
        _brightBar.Value = Math.Clamp(p.Brightness, _brightBar.Minimum, _brightBar.Maximum);
        foreach (var row in _deviceRows)
            if (p.Devices.TryGetValue(row.Key, out bool on))
                row.SetChecked(on);
        RefreshDeviceGroups(); // SetChecked skips CheckedChanged, so refresh group headers manually
        SetColorTarget(null);
        UpdateDeviceChips();
        UpdateSwatchSelection();
        SelectEffect(Enum.TryParse(p.Effect, out Effect e) ? e : Effect.Static);
        try { _tray.ShowBalloonTip(1200, "Prisma", $"Profile \"{p.Name}\" applied", ToolTipIcon.Info); } catch { }
    }

    private (string Name, string Trigger)? PromptProfileSave()
    {
        using var dlg = new Form
        {
            Text = "Save profile",
            ClientSize = new Size(330, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            BackColor = Theme.Bg, StartPosition = FormStartPosition.CenterParent,
            Font = Theme.UiFont(9.5f)
        };
        dlg.HandleCreated += (_, _) => { int dark = 1; try { DwmSetWindowAttribute(dlg.Handle, 20, ref dark, sizeof(int)); } catch { } };
        dlg.KeyPreview = true;
        dlg.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dlg.DialogResult = DialogResult.Cancel; };
        var tb = new TextBox
        {
            Left = 18, Top = 18, Width = 294, Font = Theme.UiFont(10f),
            BackColor = Theme.RowBg, ForeColor = Theme.TextCol, BorderStyle = BorderStyle.FixedSingle,
            Text = $"Profile {_settings.Profiles.Count + 1}"
        };
        tb.SelectAll();
        dlg.Controls.Add(new CrispLabel
        {
            Text = "Auto-activate while this app is running (optional):",
            Left = 18, Top = 58, AutoSize = true, ForeColor = Theme.Subtle, Font = Theme.UiFont(8.5f)
        });
        var trigTb = new TextBox
        {
            Left = 18, Top = 80, Width = 294, Font = Theme.UiFont(10f),
            BackColor = Theme.RowBg, ForeColor = Theme.TextCol, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. cs2.exe or cyberpunk2077"
        };
        var ok = new RoundedButton { Text = "Save", Left = 122, Top = 144, Width = 92, Height = 36, FillColor = Theme.AccentDim, HoverFillColor = Theme.AccentDim, BorderColor = Theme.Accent, ForeColor = Color.White };
        var cancel = new RoundedButton { Text = "Cancel", Left = 220, Top = 144, Width = 92, Height = 36 };
        ok.Click += (_, _) => dlg.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
        tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { dlg.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; } };
        trigTb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { dlg.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; } };
        dlg.Controls.Add(tb);
        dlg.Controls.Add(trigTb);
        dlg.Controls.Add(ok);
        dlg.Controls.Add(cancel);
        return dlg.ShowDialog(this) == DialogResult.OK ? (tb.Text.Trim(), trigTb.Text.Trim()) : null;
    }

    private static string EffectLabel(Effect e) => e switch
    {
        Effect.Static => "Static color",
        Effect.RainbowWave => "Rainbow wave",
        Effect.ColorCycle => "Color cycle",
        Effect.Breathing => "Breathing",
        Effect.Strobe => "Strobe",
        Effect.Gradient => "Gradient",
        Effect.Comet => "Comet",
        Effect.Twinkle => "Twinkle",
        Effect.Ambient => "Screen sync",
        Effect.Surprise => "Surprise me",
        Effect.Fire => "Fire",
        Effect.Music => "Music",
        _ => "All off"
    };

    private void UpdateTrayText()
    {
        if (_tray == null) return;
        string text = $"Prisma · {_deviceRows.Count} device{(_deviceRows.Count == 1 ? "" : "s")} · {EffectLabel(_effect)}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        UpdateTrayIcon();
    }

    // The live tray icon renders at 32px (not the tray's native 16) so the shell downsamples
    // a supersampled glyph — much crisper than a flat 16px draw on a 125-200% DPI taskbar.
    private const int TrayIconPx = 32;

    /// <summary>The tray icon is a live Prisma mark: the "P" drawn in the current color on a
    /// dark tile (grey when everything is off), so the lighting state reads at a glance while
    /// the icon stays unmistakably Prisma — see <see cref="DrawTrayGlyph"/>.</summary>
    private void UpdateTrayIcon()
    {
        if (_tray == null) return;
        Color fill = _effect == Effect.Off ? Color.FromArgb(128, 130, 138) : _currentColor;
        if (fill.ToArgb() == _trayIconArgb) return; // pixel-identical; skip the GDI + Explorer round-trip
        // A hue-bar drag calls SetColor per mouse-move; regenerating the icon and pushing
        // Shell_NotifyIcon to Explorer ~100x/sec is pure waste for a glyph nobody can read at
        // that rate. Gate to ~6/sec; the one-shot retry guarantees the final color still lands
        // (trailing edge) so the icon can never be left stale.
        var now = DateTime.UtcNow;
        if ((now - _trayIconAt).TotalMilliseconds < 150)
        {
            _trayIconRetry.Stop();
            _trayIconRetry.Start();
            return;
        }
        _trayIconAt = now;
        _trayIconArgb = fill.ToArgb();
        using var bmp = new Bitmap(TrayIconPx, TrayIconPx);
        using (var g = Graphics.FromImage(bmp))
            DrawTrayGlyph(g, TrayIconPx, fill);
        IntPtr h = bmp.GetHicon();
        try
        {
            var icon = (Icon)Icon.FromHandle(h).Clone(); // own the icon; release the GDI handle
            _tray.Icon = icon;
            _trayDynIcon?.Dispose();
            _trayDynIcon = icon;
        }
        catch { }
        finally { DestroyIcon(h); }
    }

    /// <summary>Draws the live Prisma mark — the "P" in <paramref name="fill"/> on a dark rounded
    /// tile (the app-icon structure, lit up) — filling the whole <paramref name="size"/>×size canvas.
    /// A bright glyph on a mostly-dark tile keeps the hue readable via contrast without looking like
    /// a plain colour blob.</summary>
    private static void DrawTrayGlyph(Graphics g, int size, Color fill)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        float s = size;
        using (var tile = RoundedTilePath(0.5f, 0.5f, s - 1f, s - 1f, s * 0.22f))
        {
            using (var bg = new SolidBrush(Color.FromArgb(22, 23, 28))) g.FillPath(bg, tile);
            using var rim = new Pen(Color.FromArgb(38, 255, 255, 255), Math.Max(1f, s / 32f));
            g.DrawPath(rim, tile);
        }
        using var p = new System.Drawing.Drawing2D.GraphicsPath();
        using (var ff = new FontFamily("Arial"))
            p.AddString("P", ff, (int)FontStyle.Bold, s * 0.95f,
                new PointF(s * 0.14f, s * 0.02f), StringFormat.GenericTypographic);
        using var glyph = new SolidBrush(fill);
        g.FillPath(glyph, p);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedTilePath(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void RestoreFromTray()
    {
        Show();
        // only un-minimize; a window hidden via close-to-tray keeps its state as-is
        if (WindowState == FormWindowState.Minimized) WindowState = _stateBeforeMinimize;
        Activate();
    }

    /// <summary>Called when another launch of the exe is detected: surface the window.</summary>
    public void ShowFromOtherInstance()
    {
        if (!IsDisposed) RestoreFromTray();
    }

    // ------------------------------------------------------ remote / command line

    /// <summary>Applies a command forwarded from a second launch (Stream Deck, a script,
    /// a shortcut). Runs on the UI thread. Tokens are Unit-Separator joined so quoted
    /// names survive. A profile is applied first, then any explicit --color/--brightness/
    /// --speed/--effect overlay it; --off is authoritative (it overrides toggle/cycle).
    /// --brightness/--speed accept an absolute (55) or a relative (+10 / -10) value.</summary>
    public void HandleRemoteCommand(string raw)
    {
        if (IsDisposed) return;
        DebugLog.Log("remote cmd: " + raw.Replace(RemoteControl.Sep, ' '));
        var t = raw.Split(RemoteControl.Sep);

        Color? color = null; Effect? effect = null; int? brightness = null; int? speed = null;
        string? profile = null;
        bool toggle = false, off = false, cycle = false, show = false, any = false;

        for (int i = 0; i < t.Length; i++)
        {
            switch (t[i].Trim().ToLowerInvariant())
            {
                case "--toggle": toggle = any = true; break;
                case "--off": off = any = true; break;
                case "--next-profile": case "--cycle-profile": cycle = any = true; break;
                case "--show": show = any = true; break;
                case "--effect": if (i + 1 < t.Length && TryParseEffect(t[++i], out var fx)) { effect = fx; any = true; } break;
                case "--color": if (i + 1 < t.Length && TryParseColor(t[++i], out var c)) { color = c; any = true; } break;
                case "--brightness": if (i + 1 < t.Length && TryResolveLevel(t[++i], _brightBar.Value, 1, 100, out int b)) { brightness = b; any = true; } break;
                case "--speed": if (i + 1 < t.Length && TryResolveLevel(t[++i], _speedBar.Value, _speedBar.Minimum, _speedBar.Maximum, out int s)) { speed = s; any = true; } break;
                case "--profile": if (i + 1 < t.Length) { profile = t[++i]; any = true; } break;
                case "--gpu-bright": // calibration: write a raw 0..255 to the GPU brightness reg 0x3E
                    if (i + 1 < t.Length && byte.TryParse(t[++i], out byte gv))
                    {
                        var gpu = _deviceRows.Select(r => r.Device).OfType<SapphireNitroGlowDevice>().FirstOrDefault();
                        gpu?.WriteBrightnessRaw(gv);
                        any = true;
                    }
                    break;
                case "--skin": // modern | classic | toggle - the Settings toggle from a shortcut
                    if (i + 1 < t.Length)
                    {
                        string want = t[++i].Trim().ToLowerInvariant();
                        bool classic = want switch
                        {
                            "classic" or "95" or "win95" => true,
                            "modern" => false,
                            _ => !_settings.ClassicSkin,
                        };
                        if (classic != Theme.Classic)   // the skin actually showing, not just the saved one
                        {
                            _settings.ClassicSkin = classic;
                            ApplySettingsChanged();
                        }
                        any = true;
                    }
                    break;
                case "--hitmap" when Harness: // design harness: paint what a press does everywhere
                    if (i + 1 < t.Length) { WriteHitMap(t[++i]); any = true; }
                    break;
                case "--expand-groups": // test harness: toggle every device group (screenshot the expanded list)
                    foreach (var grp in _deviceGroups)
                    {
                        grp.Expanded = !grp.Expanded;
                        foreach (var m in grp.Members) m.Visible = grp.Expanded;
                        grp.Invalidate();
                    }
                    LayoutLeftColumn();
                    FitDeviceRows();
                    any = true;
                    break;
            }
        }

        // A profile sets effect+color+sliders; explicit tokens then overlay it.
        if (profile != null) ApplyProfileByName(profile);
        if (color.HasValue) SetColor(color.Value);
        if (brightness.HasValue) _brightBar.Value = brightness.Value;
        if (speed.HasValue) _speedBar.Value = speed.Value;
        if (effect.HasValue) SelectEffect(effect.Value);
        if (off) SelectEffect(Effect.Off);              // --off wins; never undone by --toggle
        else { if (toggle) ToggleLights(); if (cycle) CycleProfile(); }
        if (show || !any) ShowFromOtherInstance();
    }

    /// <summary>Resolves a level token: absolute ("55") or relative ("+10" / "-10")
    /// against <paramref name="current"/>, clamped to [min,max].</summary>
    private static bool TryResolveLevel(string s, int current, int min, int max, out int result)
    {
        result = current;
        s = s.Trim();
        if (!int.TryParse(s, out int n)) return false;
        bool relative = s.StartsWith("+") || s.StartsWith("-");
        result = Math.Clamp(relative ? current + n : n, min, max);
        return true;
    }

    private void ApplyProfileByName(string name)
    {
        var p = _settings.Profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (p != null) ApplyProfile(p);
        else SetStatus($"No saved profile named \"{name}\"", error: true);
    }

    private static bool TryParseColor(string s, out Color color)
    {
        color = Color.Empty;
        s = s.Trim().TrimStart('#');
        if (s.Length == 6 && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v))
        {
            color = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }
        return false;
    }

    private static bool TryParseEffect(string s, out Effect effect)
    {
        effect = Effect.Static;
        switch (s.Trim().ToLowerInvariant())
        {
            case "static": case "solid": effect = Effect.Static; return true;
            case "rainbow": case "wave": case "rainbowwave": effect = Effect.RainbowWave; return true;
            case "cycle": case "colorcycle": effect = Effect.ColorCycle; return true;
            case "breathing": case "breathe": effect = Effect.Breathing; return true;
            case "strobe": case "flash": effect = Effect.Strobe; return true;
            case "gradient": effect = Effect.Gradient; return true;
            case "comet": effect = Effect.Comet; return true;
            case "twinkle": case "sparkle": effect = Effect.Twinkle; return true;
            case "ambient": case "screen": case "screensync": effect = Effect.Ambient; return true;
            case "surprise": case "random": effect = Effect.Surprise; return true;
            case "fire": effect = Effect.Fire; return true;
            case "music": case "audio": effect = Effect.Music; return true;
            case "off": effect = Effect.Off; return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- devices

    /// <summary>Tray-balloons the devices that appeared or vanished since the last detection.
    /// Silent on the first detection (everything is "new" then) and when the set is unchanged.</summary>
    private void NotifyDeviceChanges()
    {
        var current = _deviceRows.ToDictionary(r => r.Key, r => r.Device.Name);
        if (_settings.NotifyDeviceChanges && _knownDevices != null)
        {
            var added = current.Where(kv => !_knownDevices.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
            var removed = _knownDevices.Where(kv => !current.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
            if (added.Count > 0 || removed.Count > 0)
            {
                var lines = new List<string>();
                if (added.Count > 0) lines.Add("Connected: " + string.Join(", ", added));
                if (removed.Count > 0) lines.Add("Disconnected: " + string.Join(", ", removed));
                try { _tray.ShowBalloonTip(4000, "Prisma", string.Join("\n", lines), ToolTipIcon.Info); } catch { }
            }
        }
        _knownDevices = current;
    }

    private async Task DetectDevicesAsync()
    {
        if (_detecting) { DebugLog.Log("detect: skipped, already running"); return; }
        _detecting = true;
        _rescanPendingSince = -1; // any detect (manual/watchdog/settled) clears a pending debounce
        try
        {
            await DetectDevicesCore();
        }
        catch (Exception ex)
        {
            // Without this guard an unexpected detection failure (HID enumeration, a
            // protocol parse error...) left _detecting stuck true forever: render loop
            // idle, watchdog a no-op, Rescan button disabled — a zombie app.
            DebugLog.Log("detect FAILED: " + ex.Message);
            SetStatus("Device detection failed: " + ex.Message, error: true);
        }
        finally
        {
            _detecting = false;
            _rescanButton.Enabled = true;
        }
    }

    private async Task DetectDevicesCore()
    {
        DebugLog.Log("detect start");
        _frameTimer.Stop();
        SetStatus("Detecting RGB devices...", error: false);
        _rescanButton.Enabled = false;
        ClearDeviceRows();

        var progress = new Progress<string>(s => SetStatus(s, error: false));
        DeviceManager.DetectionResult result = null!;
        if (Harness) result = HarnessRig.Detect();   // never touch HID / ADL / SMBus / OpenRGB
        else await Task.Run(() =>
        {
            var report = (IProgress<string>)progress;
            bool skipMouse = _settings.IgnoreLogitechMouse;
            // First pass; at boot the OpenRGB server task may still be scanning,
            // so give it time before resorting to launching our own copy.
            result = DeviceManager.DetectAll(_openRgb, tryConnectOpenRgb: true, skipLogitech: skipMouse);
            for (int wait = 0; wait < 5 && !result.OpenRgbConnected; wait++)
            {
                report.Report($"Waiting for OpenRGB server... ({wait + 1}/5)");
                Thread.Sleep(2000);
                DisposeDevices(result.Devices);
                result = DeviceManager.DetectAll(_openRgb, tryConnectOpenRgb: true, skipLogitech: skipMouse);
            }
            if (!result.OpenRgbConnected && TryLaunchOpenRgb())
            {
                for (int attempt = 0; attempt < 4 && !result.OpenRgbConnected; attempt++)
                {
                    report.Report($"Starting OpenRGB... ({attempt + 1}/4)");
                    Thread.Sleep(1500);
                    DisposeDevices(result.Devices);
                    result = DeviceManager.DetectAll(_openRgb, tryConnectOpenRgb: true, skipLogitech: skipMouse);
                }
            }
        });

        var keyCounts = new Dictionary<string, int>();
        foreach (var device in result.Devices)
        {
            // Stable per-row key; disambiguate identical names (e.g. four "ENE DRAM")
            // so each can hold its own enable state and color.
            string baseKey = SettingsKey(device);
            int n = keyCounts.TryGetValue(baseKey, out int cc) ? cc : 0;
            keyCounts[baseKey] = n + 1;
            string rowKey = n == 0 ? baseKey : $"{baseKey}#{n + 1}";

            // The mouse is opt-in: leave its own lighting alone unless enabled.
            bool defaultChecked = device is not LogitechHidppDevice;
            bool isChecked = _settings.Devices.TryGetValue(rowKey, out bool saved) ? saved : defaultChecked;
            var row = new DeviceRow(device, isChecked)
            {
                Key = rowKey,
                DisplayColor = _settings.DeviceColors.TryGetValue(rowKey, out int argb) ? Color.FromArgb(argb) : _currentColor
            };
            row.CheckedChanged += (_, _) => { LookEdited(); SaveSoon(); ApplyCurrentEffect(); UpdateContextualControls(); };
            row.EffectNote = _settings.DeviceEffects.TryGetValue(rowKey, out var fxName) && Enum.TryParse(fxName, out Effect fx)
                ? EffectLabel(fx) : null;
            row.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowDeviceMenu((DeviceRow)s!); };
            row.MenuKeyRequested += (s, _) => { var r = (DeviceRow)s!; ShowDeviceMenu(r, new Point(DeviceRowArt.NameX, r.Height)); };
            _tips.SetToolTip(row, $"{device.Name}\n{device.LedCount} zone{(device.LedCount == 1 ? "" : "s")} via {device.Source}\nRight-click: choose an effect for just this device");
            _deviceRows.Add(row);
        }
        PopulateDevicePanel(); // groups runs of identical devices (e.g. the 4 RAM sticks)
        _deviceList.ScrollToTop();
        FitDeviceRows();
        // a targeted device may be gone after a rescan; re-apply target (resets to All if missing)
        if (_colorTarget != null && !_deviceRows.Any(r => r.Key == _colorTarget)) _colorTarget = null;
        SetColorTarget(_colorTarget);
        _ambientArtShown = 0;         // fresh rows have no sampled colour yet: the next frame pushes it
        UpdateDeviceChips();          // fresh rows: their tiles show the effect each one really runs
        UpdateContextualControls();   // ...and SPEED follows the new device set
        UpdateTrayText();
        NotifyDeviceChanges();

        _openRgb.ClearPendingEvents(); // swallow the server's echo of our own reconnect
        _rescanButton.Enabled = true;
        _detecting = false;
        if (result.OpenRgbConnected) _openRgbEverConnected = true;

        if (_deviceRows.Count == 0)
        {
            SetStatus("No controllable RGB devices found." +
                      (result.Notes.Count > 0 ? " " + string.Join("; ", result.Notes) : ""), error: true);
            return;
        }

        string summary = $"Controlling {_deviceRows.Count} device{(_deviceRows.Count == 1 ? "" : "s")}";
        if (_deviceRows.Any(r => r.Device is LogitechHidppDevice) && Process.GetProcessesByName("lghub").Length > 0)
            summary += "  ·  close G HUB if the mouse ignores changes";
        // a healthy-looking count would hide that the GPU/RAM/motherboard backend is down
        bool error = !result.OpenRgbConnected;
        if (!result.OpenRgbConnected) summary += "  ·  OpenRGB not connected - motherboard/GPU lighting unavailable";
        // Other RGB software contending for the SMBus is the usual cause of flicker / a wedged
        // GPU; flag it (and balloon once a session) so it isn't an invisible cause of trouble.
        if (_settings.WarnOnRgbConflicts)
        {
            var conflicts = RgbConflicts.Scan();
            if (conflicts.Count > 0)
            {
                string names = string.Join(", ", conflicts.Select(c => c.Contender.Name));
                summary += $"  ·  ⚠ {names} running — may cause flicker (Settings ▸ Reliability)";
                error = true;
                if (!_conflictWarned)
                {
                    _conflictWarned = true;
                    try
                    {
                        _tray.ShowBalloonTip(6000, "Prisma — RGB conflict",
                            $"{names} is running and fights Prisma for the lighting bus. " +
                            "Open Settings ▸ Reliability to disable it.", ToolTipIcon.Warning);
                    }
                    catch { }
                }
            }
        }
        SetStatus(summary, error);

        DebugLog.Log($"detect done: {_deviceRows.Count} devices");
        if (_blackedOut)
        {
            // Detected during a lock/sleep/display-off blackout (e.g. a watchdog
            // reconnect): bring devices up dark and leave the render loop stopped, or
            // the fresh detection would defeat OffWhenLocked / OffWhenDisplaySleeps.
            BlackoutDevices();
        }
        else
        {
            // Order matters for the fragile GPU MCU: decide firmware effects FIRST (a single
            // mode write on the freshly queried device), then Prepare only the rows that
            // will be streamed. The old Prepare-everything-then-apply order sent the GPU two
            // mode writes milliseconds apart and it routinely dropped the second one.
            ApplyCurrentEffect(); // hand animated effects to capable devices' firmware (e.g. GPU)
            foreach (var row in _deviceRows)
                if (!_hwRows.Contains(row)) Try(() => row.Device.Prepare());
            _frameTimer.Start();
            // During the boot window, a device can appear late/half-initialised on the SMBus.
            // Re-assert direct mode a couple more times as the bus settles so it doesn't stay
            // stuck on its firmware default until a manual OpenRGB restart (see _bootSettle).
            if (result.OpenRgbConnected && (DateTime.UtcNow - _launchedUtc) < TimeSpan.FromSeconds(60))
            {
                _bootSettlePasses = 2;
                _bootSettle.Stop();
                _bootSettle.Start();
            }
        }
        SaveNow();
    }

    private static bool TryLaunchOpenRgb()
    {
        // Prefer the elevated "OpenRGB Server" scheduled task: it starts OpenRGB as
        // admin (required for RAM/motherboard SMBus access) without a UAC prompt.
        // Launching the exe directly from here would win the single-instance race
        // with a non-admin copy and silently lose those devices.
        try
        {
            var psi = new ProcessStartInfo("schtasks", "/run /tn \"OpenRGB Server\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p != null && p.WaitForExit(5000) && p.ExitCode == 0) return true;
        }
        catch { }

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRGB-Experimental"),
            @"C:\Program Files\OpenRGB",
            @"C:\Program Files (x86)\OpenRGB"
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var exe = Directory.EnumerateFiles(root, "OpenRGB*.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null) continue;
            try
            {
                Process.Start(new ProcessStartInfo(exe, "--server --startminimized") { UseShellExecute = true });
                return true;
            }
            catch { }
        }
        return false;
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.ForeColor = error ? Theme.ErrorCol : Theme.Subtle;
        _statusDot.ForeColor = error ? Theme.ErrorCol : Theme.Accent;
        PlaceStatusDot();
        _tips.SetToolTip(_status, text); // full text on hover when ellipsized
        _deck?.SetStatus(text, error);   // the deck's second line mirrors it (ellipsised; full text in the rail)
    }

    /// <summary>Sizes device rows to the flow panel's client width (minus the default
    /// 3px flow margins), so the right inset equals the left one with or without a
    /// vertical scrollbar.</summary>
    private void FitDeviceRows()
    {
        // The list's row width (inside its edge, less its bar when it shows): rows have zero side
        // margins, so this puts the same 12px inset on both sides of the card.
        int w = _deviceList.RowWidth;
        if (w < 100) return;
        int gap = Theme.Classic ? 0 : 4;                 // 95 lists were flush; modern rows breathe
        foreach (var row in _deviceRows)
        {
            int rw = row.InGroup ? w - 16 : w;           // grouped members sit indented under their header
            if (row.Width != rw) row.Width = rw;
            var m = row.InGroup ? new Padding(16, 0, 0, gap) : new Padding(0, 0, 0, gap);
            if (row.Margin != m) row.Margin = m;
        }
        foreach (var grp in _deviceGroups)
        {
            var m = new Padding(0, 0, 0, gap);
            if (grp.Margin != m) grp.Margin = m;
        }
        foreach (var grp in _deviceGroups)
            if (grp.Width != w) grp.Width = w;
    }

    /// <summary>Fills the device panel from <see cref="_deviceRows"/>, collapsing a run of ≥3
    /// identical devices (same name+kind — e.g. the four ENE DRAM sticks) under one
    /// <see cref="DeviceGroupRow"/> header that toggles them together and hides them until
    /// expanded. The member rows stay in <see cref="_deviceRows"/>, so every effect/apply loop
    /// is untouched — only the display changes.</summary>
    private void PopulateDevicePanel()
    {
        _devicePanel.SuspendLayout();
        _devicePanel.Controls.Clear();
        foreach (var g in _deviceGroups) g.Dispose();
        _deviceGroups.Clear();

        int i = 0;
        while (i < _deviceRows.Count)
        {
            int j = i + 1;
            while (j < _deviceRows.Count &&
                   _deviceRows[j].Device.Name == _deviceRows[i].Device.Name &&
                   _deviceRows[j].Device.Kind == _deviceRows[i].Device.Kind) j++;
            int run = j - i;
            if (run >= 3)
            {
                var members = _deviceRows.GetRange(i, run);
                var grp = new DeviceGroupRow(members);
                grp.ToggleAllRequested += (_, _) =>
                {
                    bool turnOn = !members.All(m => m.Checked);
                    foreach (var m in members) m.SetChecked(turnOn);
                    grp.Invalidate();
                    LookEdited();
                    SaveSoon();
                    ApplyCurrentEffect();
                    UpdateContextualControls();
                };
                grp.ExpandToggled += (_, _) =>
                {
                    foreach (var m in members) m.Visible = grp.Expanded;
                    LayoutLeftColumn();  // device card grows/shrinks to the visible rows; preview fills the rest
                    FitDeviceRows();
                };
                foreach (var m in members)
                {
                    m.InGroup = true;
                    m.Visible = grp.Expanded;                // collapsed by default
                    m.CheckedChanged += (_, _) => grp.Invalidate(); // keep the header's all/mixed/off state fresh
                }
                _deviceGroups.Add(grp);
                _devicePanel.Controls.Add(grp);
                foreach (var m in members) _devicePanel.Controls.Add(m);
            }
            else
            {
                for (int k = i; k < j; k++)
                {
                    _deviceRows[k].InGroup = false;
                    _deviceRows[k].Visible = true;
                    _devicePanel.Controls.Add(_deviceRows[k]);
                }
            }
            i = j;
        }
        _devicePanel.ResumeLayout();
        LayoutLeftColumn();
    }

    /// <summary>Stacks the rail: DEVICES sized to its visible rows (tight when a group is collapsed,
    /// taller when expanded), PROFILES under it, the status footer at the bottom. When everything
    /// cannot fit, both lists scroll on their own bars: PROFILES gives way first (to two rows), then
    /// the device list (to one row), then PROFILES to its last row - it never runs into the footer.</summary>
    private void LayoutLeftColumn()
    {
        if (_devicesCard == null || _profilesCard == null) return;
        // Count visible rows from the MODEL, not Control.Visible: the latter walks the parent
        // chain and returns false for every row while the form is hidden, so on autostart-to-tray
        // (Shown Hide()s before the first detect) it counted 0 and clamped the card to one row.
        int visRows = _deviceGroups.Count;                                  // one header per group
        foreach (var grp in _deviceGroups) if (grp.Expanded) visRows += grp.Members.Count;
        foreach (var row in _deviceRows) if (!row.InGroup) visRows++;       // ungrouped rows
        if (visRows == 0) visRows = 1;
        int pitch = Theme.Classic ? 48 : 52;                                // DeviceRow 48 + its bottom margin
        int H = _leftCol.Height;

        // footer, from the bottom up: Rescan, the rule, the status block
        _rescanButton.Top = H - 14 - _rescanButton.Height;
        int ruleY = _rescanButton.Top - 12;
        _leftCol.RuleY = ruleY;
        _status.Top = ruleY - 8 - _status.Height;
        PlaceStatusDot();
        int footerTop = _status.Top - 10;

        // Modern: the DEVICES caption shares its baseline with EFFECTS beside it (the content stack
        // starts at PAD - 6); Classic: both columns' group boxes start at 12.
        int devTop = Theme.Classic ? 12 : 16;
        _devicesCard.Top = devTop;
        int devEdge = 2 * _deviceList.Edge, profEdge = 2 * _profileList.Edge;
        int nProf = _profileRows.Count;
        int ProfH(int rows) => CardPanel.SectionTop + (nProf == 0 ? 22 : rows * ProfileRow.RowH + profEdge) + 6;
        int room = footerTop - devTop - 10;                                 // both cards + the gap between
        int devFixed = CardPanel.SectionTop + devEdge + 6;
        int devFull = visRows * pitch;
        int pv = Math.Clamp(nProf, 1, 5);
        while (pv > Math.Max(1, Math.Min(2, nProf)) && devFixed + devFull + ProfH(pv) > room) pv--;
        int listH = Math.Max(Math.Min(pitch, devFull), Math.Min(devFull, room - ProfH(pv) - devFixed));
        while (pv > 1 && devFixed + listH + ProfH(pv) > room) pv--;

        _deviceList.Height = listH + devEdge;
        _devicesCard.Height = devFixed + listH;
        _devicesCard.TitleNote = _deviceRows.Count > 0 ? _deviceRows.Count.ToString() : null;
        _devicesCard.Invalidate();
        _profilesCard.Top = _devicesCard.Bottom + 10;
        _profilesCard.Height = ProfH(pv);
        _profileList.Visible = nProf > 0;
        _profileList.SetBounds(CardPanel.SectionPad, CardPanel.SectionTop, _profilesCard.Width - 2 * CardPanel.SectionPad,
            Math.Max(1, pv) * ProfileRow.RowH + profEdge);
        // "+": on the caption band (its glyph's right ink on the DEVICES count's edge) in the modern
        // look; inside the 95 group box, under its frame line, in Classic
        if (Theme.Classic) _profileAdd.SetBounds(_profilesCard.Width - 8 - 26, 9, 26, 18);
        else _profileAdd.SetBounds(_profilesCard.Width - CardPanel.SectionPad - 26 + 10, 0, 26, 22);
    }

    /// <summary>The status is bottom-aligned in its three-line block (it sits on the footer rule); the
    /// dot follows its FIRST line, wherever that lands.</summary>
    private void PlaceStatusDot()
    {
        if (_status == null || _statusDot == null) return;
        int h = TextRenderer.MeasureText(_status.Text, _status.Font, new Size(Math.Max(1, _status.Width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        h = Math.Clamp(h, 1, _status.Height);
        _statusDot.Top = _status.Bottom - h + 2;
    }

    /// <summary>Re-creates the PROFILES rows from settings: each shows its own effect art, its name and
    /// its trigger process. Click / Enter / Space applies; right-click or the menu key offers Apply and
    /// Delete…; Delete asks to delete.</summary>
    private void RebuildProfileRows()
    {
        if (_profilesCard == null) return;
        var content = _profileList.Content;
        content.SuspendLayout();
        foreach (var r in _profileRows) { content.Controls.Remove(r); r.Dispose(); }
        _profileRows.Clear();
        int w = Math.Max(100, _profileList.RowWidth);
        foreach (var p in _settings.Profiles)
        {
            var row = new ProfileRow(p) { Width = w, Applied = p.Name == _appliedProfile };
            row.ApplyRequested += (s, _) => ApplyProfile(((ProfileRow)s!).Profile);
            row.MenuRequested += (s, at) => ShowProfileMenu((ProfileRow)s!, at);
            row.DeleteRequested += (s, _) => DeleteProfile(((ProfileRow)s!).Profile);
            string tip = string.IsNullOrWhiteSpace(p.TriggerProcess)
                ? $"Apply \"{p.Name}\"  ·  right-click for more"
                : $"Apply \"{p.Name}\" — also applied while {p.TriggerProcess} runs\nRight-click for more";
            _tips.SetToolTip(row, tip);
            content.Controls.Add(row);
            _profileRows.Add(row);
        }
        content.ResumeLayout();
        _profileHint.Visible = _profileRows.Count == 0;
        LayoutLeftColumn();
        FitProfileRows();
    }

    private void FitProfileRows()
    {
        int w = _profileList.RowWidth;
        if (w < 50) return;
        foreach (var r in _profileRows) if (r.Width != w) r.Width = w;
    }

    private void ShowProfileMenu(ProfileRow row, Point at)
    {
        var menu = Menus.Build();
        menu.Items.Add(Menus.Header(MenuText(row.Profile.Name)));
        menu.Items.Add(Menus.Item("Apply", null, (_, _) => ApplyProfile(row.Profile)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Menus.Item("Delete…", null, (_, _) => DeleteProfile(row.Profile)));
        menu.Show(row, at);
    }

    /// <summary>Deletes a profile after asking - there is no undo, and it sits one click from Apply.</summary>
    private void DeleteProfile(LightingProfile p)
    {
        if (!ConfirmDeleteProfile(p.Name)) return;
        _settings.Profiles.Remove(p);
        _profileCycle = -1;
        if (_appliedProfile == p.Name) _appliedProfile = null;
        _settings.Save();
        RebuildProfileRows();
    }

    private bool ConfirmDeleteProfile(string name)
    {
        using var dlg = new Form
        {
            Text = "Delete profile",
            ClientSize = new Size(330, 132),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            BackColor = Theme.Bg, StartPosition = FormStartPosition.CenterParent,
            Font = Theme.UiFont(9.5f), KeyPreview = true,
        };
        dlg.HandleCreated += (_, _) => { int dark = Theme.Classic ? 0 : 1; try { DwmSetWindowAttribute(dlg.Handle, 20, ref dark, sizeof(int)); } catch { } };
        dlg.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dlg.DialogResult = DialogResult.Cancel; };
        dlg.Controls.Add(new CrispLabel
        {
            Text = $"Delete the profile \"{name}\"?\nThis cannot be undone.", UseMnemonic = false,
            Left = 18, Top = 16, Width = 294, Height = 48, ForeColor = Theme.TextCol, Font = Theme.UiFont(9.5f),
        });
        var del = new RoundedButton { Text = "Delete", Left = 122, Top = 80, Width = 92, Height = 34, ForeColor = Theme.ErrorCol };
        var cancel = new RoundedButton { Text = "Cancel", Left = 220, Top = 80, Width = 92, Height = 34 };
        del.Click += (_, _) => dlg.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
        dlg.Controls.Add(del);
        dlg.Controls.Add(cancel);
        dlg.Shown += (_, _) => cancel.Focus();   // the safe answer is the default one
        return dlg.ShowDialog(this) == DialogResult.OK;
    }

    private void MarkAppliedProfile(string? name)
    {
        _appliedProfile = name;
        foreach (var r in _profileRows) r.Applied = r.Profile.Name == name;
    }

    /// <summary>A manual edit of the look (speed, brightness, a device toggle, a per-device effect, the
    /// gradient end): the rail no longer claims a profile is showing.</summary>
    private void LookEdited()
    {
        if (_applyingProfile == 0 && _appliedProfile != null) MarkAppliedProfile(null);
    }

    private void ClearDeviceRows()
    {
        _devicePanel.Controls.Clear();
        _hwRows.Clear();
        foreach (var grp in _deviceGroups) grp.Dispose();
        _deviceGroups.Clear();
        foreach (var row in _deviceRows)
        {
            // Direct drivers (Logitech/Gigabyte) own OS HID handles; dispose the device
            // too, not just the row control, or every rescan leaks the previous handles.
            if (row.Device is IDisposable disp) Try(disp.Dispose);
            row.Dispose();
        }
        _deviceRows.Clear();
    }

    // ---------------------------------------------------------------- effects engine

    private void RenderFrame()
    {
        if (_detecting || _deviceRows.Count == 0) return;
        _openRgb.PollEvents(); // drain server pushes (device added/removed) so the dirty flag works
        // OpenRGB fires DeviceListUpdated in BURSTS while the SMBus settles after a display
        // wake or a controller hot-plug. Re-detecting on each one rebuilds the whole device
        // list over and over and yanks the ASUS RAM/board back to their blue Aura default
        // 5-6 times. So coalesce the burst: consume the flag now and keep streaming to the
        // live devices, then re-detect ONCE after the events go quiet (or a hard cap), when
        // the bus is stable — one clean rebuild instead of a strobe of failed ones.
        double now = _clock.Elapsed.TotalSeconds;
        if (_openRgb.Connected && _openRgb.DeviceListDirty)
        {
            _openRgb.ClearPendingEvents();            // consume so we don't spin every frame
            if (_rescanPendingSince < 0) _rescanPendingSince = now;
            _lastDirtyAt = now;
        }
        if (_rescanPendingSince >= 0 &&
            (now - _lastDirtyAt >= 1.2 || now - _rescanPendingSince >= 5.0))
        {
            _rescanPendingSince = -1;
            // The burst settled. Before the disruptive full rebuild (which re-Prepares every
            // device and flashes the ASUS RAM/board blue), check whether the OpenRGB list
            // ACTUALLY changed. A non-RGB hot-plug (e.g. an Xbox controller) or a display wake
            // makes the server re-enumerate and fire the event, but the RGB set comes back
            // identical — in that case skip the rebuild entirely and keep streaming, so the
            // lights never flinch. Only a real add/remove falls through to a full re-detect.
            bool changed = true;
            try { changed = !_openRgb.Connected || _openRgb.RefreshIfChanged(); }
            catch { changed = true; } // socket trouble -> let the full detect handle reconnect
            if (changed)
            {
                DebugLog.Log("device list changed -> rescan");
                _ = DetectDevicesAsync();
                return;
            }
            DebugLog.Log("device list settled but unchanged -> skip rescan (no flash)");
            _openRgb.ClearPendingEvents();
        }

        // Scheduled/idle auto-off rides the render tick (cheap checks); the lock/sleep
        // blackout has its own event path and wins while active.
        if (!_blackedOut)
        {
            bool night = NightHoursActive();
            bool wantOff = (night && !_settings.NightDimInstead)
                        || (_settings.IdleOffEnabled && IdleMinutes() >= _settings.IdleOffMinutes);
            if (wantOff != _autoOff)
            {
                _autoOff = wantOff;
                DebugLog.Log(wantOff ? "auto-off: lights out" : "auto-off: waking");
                if (wantOff) BlackoutDevices();
                else ApplyCurrentEffect();
            }
            if (_autoOff) return;
            bool dim = night && _settings.NightDimInstead;
            if (dim != _nightDim)
            {
                _nightDim = dim;
                DebugLog.Log(dim ? "night dim: on" : "night dim: off");
                // Firmware effects can't be dimmed from here — route everything through
                // the software renderer while dim is active, restore afterwards.
                ApplyCurrentEffect();
            }
        }

        double t = _clock.Elapsed.TotalSeconds;
        double speed = _speedBar.Value / 25.0;          // 0.04 .. 4.0
        double master = _brightBar.Value / 100.0;
        if (_nightDim) master *= 0.15;                  // night "dim" mode: faint glow instead of off

        // Identify: blink one device white for a few seconds, then restore its effect.
        if (_identifyRow != null && (t >= _identifyUntil || !_deviceRows.Contains(_identifyRow)))
        {
            _identifyRow = null;
            ApplyCurrentEffect();
        }

        // Shared inputs the per-row effects may need: one screen sample for every
        // ambient row, one audio level for every music row.
        bool anyAmbient = _effect == Effect.Ambient;
        foreach (var row in _deviceRows)
            if (row.Checked && RowEffect(row) == Effect.Ambient) { anyAmbient = true; break; }
        Color ambient = anyAmbient ? SampleScreenColor() : Color.Empty;
        s_audioLevel = _audio.Level;
        s_audioBass = _audio.Bass;

        List<DeviceRow>? failed = null;
        foreach (var row in _deviceRows)
        {
            if (!row.Checked || row.Device.LedCount == 0) continue;
            if (row == _identifyRow)
            {
                bool on = t * 3 % 1 < 0.5; // ~3 Hz white blink (GPU is rate-capped, so slower there)
                var blink = new uint[row.Device.LedCount];
                if (on) Array.Fill(blink, 0xFFFFFFu);
                try { row.Device.SetColors(blink); } catch { (failed ??= new List<DeviceRow>()).Add(row); }
                continue;
            }
            if (_hwRows.Contains(row)) continue; // running its own firmware effect; don't stream
            var rowEffect = RowEffect(row);
            Color baseColor = rowEffect == Effect.Ambient ? ambient : EffectiveColor(row);
            try
            {
                row.Device.SetColors(
                    BuildColorsCore(rowEffect, baseColor, row.Device.LedCount, t, speed, master, row.Fade));
            }
            catch
            {
                (failed ??= new List<DeviceRow>()).Add(row);
            }
        }

        // live stage + deck line: the active target's colour through the current effect, at FULL value -
        // the brightness slider says how bright, the preview says what colour (at 6% the dimmed frame
        // was a near-black smear). The stage gets an LED per ~12 px so a wide stage blends smoothly.
        Color previewBase = _effect == Effect.Ambient ? ambient : ActivePickColor;
        int leds = Math.Clamp(_preview.Width / 12, 22, 96);
        var frame = BuildColorsCore(_effect, previewBase, leds, t, speed, 1.0, _pvFade);
        bool off = _effect == Effect.Off;
        _preview.Off = off;
        _preview.SetColors(frame);
        if (anyAmbient) _ambientNow = ambient;
        _deck.SetLive(frame, off, _ambientNow);
        if (anyAmbient && _ambientNow.ToArgb() != _ambientArtShown)
        {
            // the same sampled colour on every Screen-sync picture: device tiles, group tiles, the effect tile
            _ambientArtShown = _ambientNow.ToArgb();
            foreach (var row in _deviceRows) row.SetLive(_ambientNow);
            foreach (var grp in _deviceGroups)
                if (grp.Members.Count > 0 && grp.Members[0].ArtEffect == Effect.Ambient) grp.InvalidateTile();
            foreach (var b in _effectButtons)
                if (b.Tag is Effect.Ambient) b.Invalidate(new Rectangle(9, (b.Height - 22) / 2, 22, 22));
        }

        if (failed != null)
        {
            bool openRgbFailure = failed.Any(r => r.Device is OpenRgbRemoteDevice);
            if (openRgbFailure)
            {
                // Server connection is gone; drop all its devices at once and let the
                // watchdog restart OpenRGB and re-detect.
                _openRgb.Disconnect();
                failed = failed.Concat(_deviceRows.Where(r => r.Device is OpenRgbRemoteDevice))
                               .Distinct().ToList();
            }
            foreach (var row in failed)
            {
                _deviceRows.Remove(row);
                _hwRows.Remove(row);
                _devicePanel.Controls.Remove(row);
                if (row.Device is IDisposable disp) Try(disp.Dispose);
                row.Dispose();
            }
            // A removed device may have been a RAM-group member (the OpenRGB drop takes all 4
            // sticks at once); drop any group header whose members are gone so it doesn't linger
            // as a zombie — stale "×N · Σ zones" over disposed member refs — until the re-detect.
            for (int gi = _deviceGroups.Count - 1; gi >= 0; gi--)
                if (_deviceGroups[gi].Members.Any(m => !_deviceRows.Contains(m)))
                {
                    var grp = _deviceGroups[gi];
                    _devicePanel.Controls.Remove(grp);
                    _deviceGroups.RemoveAt(gi);
                    grp.Dispose();
                }
            LayoutLeftColumn();
            SetStatus(openRgbFailure
                ? "OpenRGB closed or crashed - restarting it automatically..."
                : "A device stopped responding and was removed. Rescan to re-detect.", error: true);
            if (_deviceRows.Count == 0) _frameTimer.Stop();
        }

        SurfaceDeviceHealth();
    }

    /// <summary>Surfaces a device that has wedged (e.g. the GPU's I2C MCU failing every write)
    /// once — sticky status plus a tray balloon with the recovery hint — instead of the render
    /// loop retrying it forever in silence. Clears with a brief notice when it recovers.</summary>
    private void SurfaceDeviceHealth()
    {
        string? fault = null;
        foreach (var row in _deviceRows)
            if (row.Checked && row.Device.FaultNote is { } note) { fault = note; break; }
        if (fault == _deviceFault)
        {
            // Still the same fault: re-assert the sticky line if another SetStatus clobbered it,
            // but don't re-fire the balloon (that only happens on the null->fault transition).
            if (fault != null && _status.Text != fault) SetStatus(fault, error: true);
            return;
        }
        bool recovered = fault == null && _deviceFault != null;
        _deviceFault = fault;
        if (fault != null)
        {
            SetStatus(fault, error: true);
            try { _tray.ShowBalloonTip(7000, "Prisma — lighting fault", fault, ToolTipIcon.Warning); } catch { }
        }
        else if (recovered)
        {
            SetStatus("GPU lighting recovered.", error: false);
        }
    }

    private uint[] BuildColorsCore(Effect effect, Color baseColor, int ledCount, double t, double speed, double master, FadeState fade)
    {
        var colors = new uint[ledCount];
        switch (effect)
        {
            case Effect.Ambient: // baseColor is the sampled screen color (supplied by RenderFrame)
            case Effect.Static:
            {
                // smooth toward the target color so changes fade instead of snapping
                uint target = Scaled(baseColor, master);
                double tr = target & 0xFF, tg = (target >> 8) & 0xFF, tb = (target >> 16) & 0xFF;
                if (fade.R < 0) { fade.R = tr; fade.G = tg; fade.B = tb; }
                const double k = 0.30;
                fade.R += (tr - fade.R) * k;
                fade.G += (tg - fade.G) * k;
                fade.B += (tb - fade.B) * k;
                Array.Fill(colors, OpenRgbClient.Color((int)Math.Round(fade.R), (int)Math.Round(fade.G), (int)Math.Round(fade.B)));
                break;
            }
            case Effect.RainbowWave:
            {
                // Full colour wheel per fan ring (LED count configurable in Settings),
                // wrapping along longer chains — every ring shows all colours chasing
                // in a circle. Short devices (DRAM, mouse) keep the one-wheel spread.
                double step = 360.0 / Math.Min(Math.Max(ledCount, 8), s_ringLeds);
                double dir = s_waveReverse ? -1 : 1;
                for (int i = 0; i < ledCount; i++)
                {
                    double hue = ((t * speed * 60 * dir + i * step) % 360 + 360) % 360;
                    colors[i] = HsvColor(hue, 1, master);
                }
                break;
            }
            case Effect.ColorCycle:
            {
                uint c = HsvColor((t * speed * 30) % 360, 1, master);
                Array.Fill(colors, c);
                break;
            }
            case Effect.Breathing:
            {
                double factor = 0.04 + 0.96 * (Math.Sin(t * speed * 2.2) + 1) / 2;
                Array.Fill(colors, Scaled(baseColor, master * factor));
                break;
            }
            case Effect.Strobe:
            {
                bool on = t * speed * 4 % 1 < 0.5;
                Array.Fill(colors, on ? Scaled(baseColor, master) : 0u);
                break;
            }
            case Effect.Gradient:
            {
                // Armoury Crate-style two-color scheme: the picked color blends into the
                // gradient end color along the chain. A single LED (the GPU) can't show
                // a spatial blend, so it sweeps through the gradient over time instead
                // (A -> B -> A); its 500ms rate cap still gives a readable slow fade.
                double sweep = (1 - Math.Cos(t * speed * 0.9)) / 2;
                for (int i = 0; i < ledCount; i++)
                {
                    double f = ledCount <= 1 ? sweep : (double)i / (ledCount - 1);
                    if (s_waveReverse) f = 1 - f;
                    int r = (int)Math.Clamp((baseColor.R + (s_gradientEnd.R - baseColor.R) * f) * master, 0, 255);
                    int g = (int)Math.Clamp((baseColor.G + (s_gradientEnd.G - baseColor.G) * f) * master, 0, 255);
                    int b = (int)Math.Clamp((baseColor.B + (s_gradientEnd.B - baseColor.B) * f) * master, 0, 255);
                    colors[i] = OpenRgbClient.Color(r, g, b);
                }
                break;
            }
            case Effect.Comet:
            {
                // a lit head sweeping across the strip with a fading tail
                double head = (t * speed * 0.6 * ledCount) % ledCount;
                double tail = Math.Max(2.0, ledCount * 0.35);
                for (int i = 0; i < ledCount; i++)
                {
                    double d = head - i;
                    if (d < 0) d += ledCount;
                    double b = Math.Max(0, 1 - d / tail);
                    colors[i] = Scaled(baseColor, master * b * b);
                }
                break;
            }
            case Effect.Twinkle:
            {
                // each LED sparkles on its own pseudo-random phase
                for (int i = 0; i < ledCount; i++)
                {
                    double seed = Frac(Math.Sin((i + 1) * 12.9898) * 43758.5453);
                    double tw = Math.Sin(t * speed * 2.5 + seed * Math.PI * 2);
                    double b = Math.Pow(Math.Max(0, tw), 6);
                    colors[i] = Scaled(baseColor, master * (0.05 + 0.95 * b));
                }
                break;
            }
            case Effect.Surprise:
            {
                // a new pseudo-random color every few seconds, slowly cross-fading between them.
                // Driven by floor(time) so every device picks the same color in sync.
                double seg = Math.Floor(t * speed * 0.33);
                double hue = Frac(Math.Sin(seg * 78.233) * 43758.5453) * 360;
                uint target = HsvColor(hue, 0.9, master);
                double tr = target & 0xFF, tg = (target >> 8) & 0xFF, tb = (target >> 16) & 0xFF;
                if (fade.R < 0) { fade.R = tr; fade.G = tg; fade.B = tb; }
                const double k = 0.05; // slow, dreamy crossfade
                fade.R += (tr - fade.R) * k;
                fade.G += (tg - fade.G) * k;
                fade.B += (tb - fade.B) * k;
                Array.Fill(colors, OpenRgbClient.Color((int)Math.Round(fade.R), (int)Math.Round(fade.G), (int)Math.Round(fade.B)));
                break;
            }
            case Effect.Fire:
            {
                // warm flicker: hue drifts red->orange->yellow, brightness gutters like a flame
                for (int i = 0; i < ledCount; i++)
                {
                    double n1 = Math.Sin(t * speed * 7.0 + i * 1.7);
                    double n2 = Math.Sin(t * speed * 13.0 + i * 0.9 + 2.1);
                    double flick = Math.Clamp(0.55 + 0.45 * (n1 * 0.6 + n2 * 0.4), 0.08, 1.0);
                    double hue = 14 + 26 * Frac(Math.Sin((i + 1) * 91.7) * 1234.5 + t * 0.07); // ~14..40°
                    colors[i] = HsvColor(hue, 1, master * flick);
                }
                break;
            }
            case Effect.Music:
            {
                // Audio visualizer: bass-weighted VU fill in the base color whose top
                // heats toward white, a falling peak-hold dot above it, and a faint
                // breathing floor when silent. SPEED doubles as sensitivity.
                double blended = 0.55 * Math.Min(s_audioBass * 2.2, 1) + 0.45 * s_audioLevel;
                double level = Math.Clamp(blended * 1.6 * (0.5 + speed * 0.5), 0, 1);

                // frame-rate independent peak: hold ~0.5s, then fall
                double dt = fade.LastT > 0 ? Math.Clamp(t - fade.LastT, 0, 0.2) : 0.04;
                fade.LastT = t;
                if (level >= fade.Peak) { fade.Peak = level; fade.HoldUntil = t + 0.5; }
                else if (t > fade.HoldUntil) fade.Peak = Math.Max(level, fade.Peak - dt * 0.9);

                if (ledCount == 1)
                {
                    // single LED (GPU): pulse, blending toward white on hits
                    double k = 0.08 + 0.92 * level;
                    int r1 = (int)Math.Clamp((baseColor.R + (255 - baseColor.R) * level * 0.45) * master * k, 0, 255);
                    int g1 = (int)Math.Clamp((baseColor.G + (255 - baseColor.G) * level * 0.45) * master * k, 0, 255);
                    int b1 = (int)Math.Clamp((baseColor.B + (255 - baseColor.B) * level * 0.45) * master * k, 0, 255);
                    colors[0] = OpenRgbClient.Color(r1, g1, b1);
                    break;
                }

                double fill = level * ledCount;                  // fractional fill
                double floor = 0.03 + 0.02 * Math.Sin(t * 1.6);  // idle breathing floor
                for (int i = 0; i < ledCount; i++)
                {
                    double over = i + 1 - fill;
                    double bright = over <= 0 ? 1 : over < 1 ? 1 - over : floor; // anti-aliased tip
                    double heat = 0;
                    if (bright > floor + 0.01 && fill > 0.001)
                    {
                        // the top ~30% of the lit section glows toward white, scaled by loudness
                        double withinFill = Math.Clamp((i + 1) / fill, 0, 1);
                        heat = Math.Clamp((withinFill - 0.7) / 0.3, 0, 1) * level;
                    }
                    int pos = s_waveReverse ? ledCount - 1 - i : i;
                    int r = (int)Math.Clamp((baseColor.R + (255 - baseColor.R) * heat) * master * bright, 0, 255);
                    int g = (int)Math.Clamp((baseColor.G + (255 - baseColor.G) * heat) * master * bright, 0, 255);
                    int b = (int)Math.Clamp((baseColor.B + (255 - baseColor.B) * heat) * master * bright, 0, 255);
                    colors[pos] = OpenRgbClient.Color(r, g, b);
                }
                if (fade.Peak > 0.03)
                {
                    // white peak-hold dot riding above the fill
                    int peakLed = Math.Min(ledCount - 1, (int)(fade.Peak * ledCount));
                    int pos = s_waveReverse ? ledCount - 1 - peakLed : peakLed;
                    int w = (int)(255 * master);
                    colors[pos] = OpenRgbClient.Color(w, w, w);
                }
                break;
            }
            case Effect.Off:
                break; // all zeros
        }
        return colors;
    }

    /// <summary>Average color of the primary screen for the ambient (screen-sync) effect.
    /// Throttled to ~10 Hz and downscaled to a tiny bitmap; the 25 fps render loop keeps
    /// fading toward the latest sample so the lighting stays smooth between grabs.</summary>
    private Color SampleScreenColor()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastAmbientSample).TotalMilliseconds < 100) return _ambientColor;
        _lastAmbientSample = now;
        try
        {
            var b = Screen.PrimaryScreen!.Bounds;
            if (_grabFull == null || _grabFull.Width != b.Width || _grabFull.Height != b.Height)
            {
                _grabFull?.Dispose();
                _grabFull = new Bitmap(b.Width, b.Height);
            }
            const int sw = 48, sh = 27;
            _grabSmall ??= new Bitmap(sw, sh);

            using (var g = Graphics.FromImage(_grabFull))
                g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            using (var g2 = Graphics.FromImage(_grabSmall))
            {
                // Plain Bilinear, not HighQuality: the result is averaged over every texel
                // below, so the cheap filter yields the same color (±1-2/255) while the HQ
                // filter alone cost 10-40ms per sample ON THE UI THREAD — enough to blow the
                // 40ms frame budget and jitter every device's pacing at 4K.
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g2.DrawImage(_grabFull, 0, 0, sw, sh);
            }

            // One LockBits + bulk copy instead of 1,296 GetPixel P/Invoke round-trips.
            var data = _grabSmall.LockBits(new Rectangle(0, 0, sw, sh),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            long r = 0, gg = 0, bb = 0;
            try
            {
                int bytes = data.Stride * sh;
                if (_grabBuf == null || _grabBuf.Length < bytes) _grabBuf = new byte[bytes];
                Marshal.Copy(data.Scan0, _grabBuf, 0, bytes);
                for (int yy = 0; yy < sh; yy++)
                {
                    int row = yy * data.Stride;
                    for (int xx = 0; xx < sw; xx++)
                    {
                        int i = row + xx * 4;                       // 32bpp BGRA
                        bb += _grabBuf[i]; gg += _grabBuf[i + 1]; r += _grabBuf[i + 2];
                    }
                }
            }
            finally { _grabSmall.UnlockBits(data); }
            int n = sw * sh;
            _ambientColor = Boost(Color.FromArgb((int)(r / n), (int)(gg / n), (int)(bb / n)));
        }
        catch { /* screen capture can fail on a secure desktop / lock screen */ }
        return _ambientColor;
    }

    /// <summary>Deepens a color's saturation so the averaged screen tint reads as a color
    /// rather than a muddy grey, without changing its hue.</summary>
    private static Color Boost(Color c)
    {
        int mx = Math.Max(c.R, Math.Max(c.G, c.B));
        double boost = s_ambientBoost; // saturation push, "Colour boost" in Settings
        if (mx == 0 || boost <= 1.001) return c;
        int R = (int)Math.Clamp(mx - (mx - c.R) * boost, 0, 255);
        int G = (int)Math.Clamp(mx - (mx - c.G) * boost, 0, 255);
        int B = (int)Math.Clamp(mx - (mx - c.B) * boost, 0, 255);
        return Color.FromArgb(R, G, B);
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private static uint Scaled(Color c, double factor) =>
        OpenRgbClient.Color((int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));

    private static uint HsvColor(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return OpenRgbClient.Color((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    // ---------------------------------------------------------------- helpers

    private void DisposeDevices()
    {
        foreach (var row in _deviceRows)
            if (row.Device is IDisposable disp) Try(disp.Dispose);
        _deviceRows.Clear();
    }

    private static void DisposeDevices(List<IRgbDevice> devices)
    {
        foreach (var d in devices)
            if (d is IDisposable disp) Try(disp.Dispose);
        devices.Clear();
    }

    private static void Try(Action action)
    {
        try { action(); } catch { }
    }
}
