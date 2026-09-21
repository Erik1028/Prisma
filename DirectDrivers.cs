using System.Text;

namespace RGBCommander;

/// <summary>A controllable RGB device, regardless of backend. Colors use the same
/// packing as OpenRgbClient.Color: r | g&lt;&lt;8 | b&lt;&lt;16.</summary>
public interface IRgbDevice
{
    string Name { get; }
    int LedCount { get; }
    string Source { get; }
    string Kind { get; }
    void Prepare() { }
    void SetColors(uint[] colors);

    /// <summary>Tries to run an animated effect on the device's own firmware (smooth, off its
    /// own clock) instead of streaming software frames. Returns true if applied — the caller
    /// then stops streaming to it. Default: not supported (caller renders in software).</summary>
    bool TrySetHardwareEffect(HwEffect kind, Color color) => false;

    /// <summary>Non-null when the device has stopped responding to writes — e.g. the Sapphire
    /// GPU's I2C MCU wedging (every write fails, a state only a sleep/wake or reboot clears).
    /// The render loop surfaces this string and the recovery hint once, instead of silently
    /// retrying forever. Default: healthy.</summary>
    string? FaultNote => null;
}

/// <summary>A device-agnostic animated effect that can map to a built-in firmware mode.</summary>
public enum HwEffect { Rainbow, Cycle, Breathing, Flashing }

/// <summary>Shared throttling for direct HID drivers: skip identical frames
/// (with a periodic keep-alive) and cap the update rate.</summary>
public abstract class DirectRgbDevice : IRgbDevice, IDisposable
{
    private uint[]? _lastColors;
    private DateTime _lastSend = DateTime.MinValue;

    public abstract string Name { get; }
    public abstract int LedCount { get; }
    public abstract string Source { get; }
    public abstract string Kind { get; }

    public void SetColors(uint[] colors)
    {
        var now = DateTime.UtcNow;
        bool changed = _lastColors == null || !colors.AsSpan().SequenceEqual(_lastColors);
        double sinceLast = (now - _lastSend).TotalMilliseconds;
        if (!changed && sinceLast < 1500) return;   // identical frame, keep-alive not yet due
        if (changed && sinceLast < 50) return;      // rate cap; effect timer will deliver the next frame
        SendColors(colors);
        _lastColors = (uint[])colors.Clone();
        _lastSend = now;
    }

    protected abstract void SendColors(uint[] colors);

    /// <summary>Direct drivers stream by default; override to run a built-in firmware effect.</summary>
    public virtual bool TrySetHardwareEffect(HwEffect kind, Color color) => false;

    public abstract void Dispose();

    protected static (byte R, byte G, byte B) Split(uint c) => ((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
}

/// <summary>
/// Direct driver for the Logitech G502 X PLUS connected via a LIGHTSPEED receiver
/// (USB 046D:C547). Speaks HID++ 2.0 long reports (0x11) on the vendor collection
/// (usage page 0xFF00, usage 2) and drives the RGB_EFFECTS feature (0x8071).
/// </summary>
public sealed class LogitechHidppDevice : DirectRgbDevice
{
    public const ushort LogitechVid = 0x046D;
    public static readonly ushort[] ReceiverPids = { 0xC547, 0xC545, 0xC541, 0xC539 };

    private readonly HidStream _stream;
    private byte _deviceIndex;
    private byte _rgbFeatureIndex;
    private ushort _rgbFeaturePage;
    private byte[] _fixedEffectIndex = Array.Empty<byte>();
    private string _name = "Logitech wireless device";
    private bool _swControlEnabled;
    private DateTime _probeDeadline = DateTime.MaxValue; // hard cap on the HID++ discovery sweep

    public override string Name => _name;
    public override int LedCount => _fixedEffectIndex.Length;
    public override string Source => "Logitech HID++";
    public override string Kind => "MOUSE";

    private LogitechHidppDevice(HidStream stream) => _stream = stream;

    public static LogitechHidppDevice? TryCreate(List<HidDeviceInfo> hidDevices)
    {
        // Hard time-box the whole discovery. An absent or sleeping wireless mouse answers
        // NONE of the HID++ probes, so the full sweep (2 pages x 6 slots x 2 attempts x 2
        // passes) used to burn ~15-20s of 400ms timeouts — and it blocks the ENTIRE device
        // detection (GPU/RAM/board) behind an opt-in device. An awake mouse answers the first
        // probe in <20ms, so this cap never affects a present one; a silent receiver just gets
        // the "move the mouse and Rescan" note instead of a multi-second freeze at every launch.
        var deadline = DateTime.UtcNow.AddMilliseconds(2200);
        // Two full passes: other software (G HUB) sharing the receiver can flood the
        // channel with notifications and make a single init attempt time out.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (var info in hidDevices)
            {
                if (info.Vid != LogitechVid || !ReceiverPids.Contains(info.Pid)) continue;
                if (info.UsagePage != 0xFF00 || info.Usage != 2) continue;
                if (DateTime.UtcNow > deadline) { DebugLog.Log("logitech probe timed out (silent receiver)"); return null; }
                LogitechHidppDevice? drv = null;
                try
                {
                    drv = new LogitechHidppDevice(new HidStream(info)) { _probeDeadline = deadline };
                    if (drv.Init()) return drv;
                    drv.Dispose();
                }
                catch
                {
                    drv?.Dispose();
                }
            }
        }
        return null;
    }

    private bool Init()
    {
        _stream.DrainInput();

        // Probe paired device slots 1..6 for one exposing the RGB_EFFECTS (0x8071)
        // or COLOR_LED_EFFECTS (0x8070) feature. Two attempts per slot since a
        // sleeping wireless device can miss the first packet.
        foreach (ushort page in new ushort[] { 0x8071, 0x8070 })
        {
            for (byte idx = 1; idx <= 6 && _rgbFeatureIndex == 0; idx++)
            {
                if (DateTime.UtcNow > _probeDeadline) return false; // time-box: don't stall detection on a silent receiver
                for (int attempt = 0; attempt < 2 && _rgbFeatureIndex == 0; attempt++)
                {
                    // Short probe timeout: an awake device answers in <20ms, so a longer wait
                    // only lengthens the sweep for a silent one. More short attempts also fit
                    // in the deadline budget, giving a waking mouse more chances to respond.
                    var resp = Request(idx, 0x00, 0x01, new byte[] { (byte)(page >> 8), (byte)(page & 0xFF) }, 150);
                    if (resp != null && resp[0] != 0)
                    {
                        _deviceIndex = idx;
                        _rgbFeatureIndex = resp[0];
                        _rgbFeaturePage = page;
                    }
                }
            }
            if (_rgbFeatureIndex != 0) break;
        }
        if (_rgbFeatureIndex == 0) return false;

        ReadDeviceName();
        return DiscoverZones();
    }

    /// <summary>Takes RGB control away from the device firmware. Deferred until the
    /// first color write so a merely-detected (unchecked) mouse keeps its own lighting.</summary>
    private void EnsureSoftwareControl()
    {
        if (_swControlEnabled) return;
        if (_rgbFeaturePage == 0x8071)
        {
            // MANAGE_SW_CONTROL: take RGB + power control away from device firmware
            Request(_deviceIndex, _rgbFeatureIndex, 0x50, new byte[] { 1, 3, 5 });
        }
        else
        {
            // FP8070 SET_SW_CTL
            Request(_deviceIndex, _rgbFeatureIndex, 0x80, new byte[] { 1, 1 });
        }
        _swControlEnabled = true;
    }

    private void ReadDeviceName()
    {
        var idxResp = Request(_deviceIndex, 0x00, 0x01, new byte[] { 0x00, 0x05 }); // DEVICE_NAME_TYPE feature
        if (idxResp == null || idxResp[0] == 0) return;
        byte nameFeature = idxResp[0];

        var lenResp = Request(_deviceIndex, nameFeature, 0x01, Array.Empty<byte>());
        if (lenResp == null) return;
        int nameLength = lenResp[0];

        var sb = new StringBuilder();
        while (sb.Length < nameLength && sb.Length < 64)
        {
            var chunk = Request(_deviceIndex, nameFeature, 0x11, new byte[] { (byte)sb.Length });
            if (chunk == null) break;
            foreach (byte b in chunk)
            {
                if (b == 0 || sb.Length >= nameLength) break;
                sb.Append((char)b);
            }
        }
        if (sb.Length > 3) _name = sb.ToString().Trim() + " (wireless)";
    }

    private bool DiscoverZones()
    {
        int ledCount;
        if (_rgbFeaturePage == 0x8071)
        {
            var info = Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { 0xFF, 0xFF });
            if (info == null) return false;
            ledCount = (info[1] << 8) | info[2];
        }
        else
        {
            var info = Request(_deviceIndex, _rgbFeatureIndex, 0x00, Array.Empty<byte>());
            if (info == null) return false;
            ledCount = info[0];
        }
        if (ledCount <= 0 || ledCount > 8) ledCount = Math.Clamp(ledCount, 1, 8);

        _fixedEffectIndex = new byte[ledCount];
        for (byte led = 0; led < ledCount; led++)
        {
            // Same time-box as Init: a mouse that answered the probe but then goes silent on
            // per-zone/per-effect reads would otherwise burn ledCount x 16 x 400ms of timeouts
            // here (tens of seconds) with no deadline, freezing the whole detection.
            if (DateTime.UtcNow > _probeDeadline) return false;
            byte fxCount;
            if (_rgbFeaturePage == 0x8071)
            {
                var zone = Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { led, 0xFF });
                if (zone == null) return false;
                fxCount = zone[4];
            }
            else
            {
                var zone = Request(_deviceIndex, _rgbFeatureIndex, 0x10, new byte[] { led });
                if (zone == null) return false;
                fxCount = zone[3];
            }

            _fixedEffectIndex[led] = 0;
            for (byte fx = 0; fx < fxCount && fx < 16; fx++)
            {
                if (DateTime.UtcNow > _probeDeadline) return false;
                byte[]? effect = _rgbFeaturePage == 0x8071
                    ? Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { led, fx })
                    : Request(_deviceIndex, _rgbFeatureIndex, 0x20, new byte[] { led, fx });
                if (effect == null) continue;
                int mode = (effect[2] << 8) | effect[3];
                if (mode == 0x0001) // fixed color ("LED on")
                {
                    _fixedEffectIndex[led] = fx;
                    break;
                }
            }
        }
        return true;
    }

    /// <summary>Dumps raw discovery data and per-command responses for debugging.</summary>
    public string DumpDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name={_name} deviceIndex={_deviceIndex} featureIndex=0x{_rgbFeatureIndex:X2} page=0x{_rgbFeaturePage:X4}");

        string Hex(byte[]? d) => d == null ? "<timeout>" : BitConverter.ToString(d);

        var info = Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { 0xFF, 0xFF });
        sb.AppendLine($"GET_INFO(FF,FF): {Hex(info)}");

        for (byte led = 0; led < _fixedEffectIndex.Length; led++)
        {
            var zone = Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { led, 0xFF });
            sb.AppendLine($"zone {led}: {Hex(zone)}  chosenFixedFx={_fixedEffectIndex[led]}");
            byte fxCount = zone?[4] ?? 0;
            for (byte fx = 0; fx < fxCount && fx < 16; fx++)
            {
                var effect = Request(_deviceIndex, _rgbFeatureIndex, 0x00, new byte[] { led, fx });
                sb.AppendLine($"  effect {fx}: {Hex(effect)}");
            }
        }
        return sb.ToString();
    }

    public byte FixedEffectIndexOf(int led) => _fixedEffectIndex[led];

    /// <summary>Sets one color and returns the raw device response for debugging.</summary>
    public string SetColorVerbose(byte led, byte effectIndex, byte r, byte g, byte b)
    {
        var args = new byte[13];
        args[0] = led;
        args[1] = effectIndex;
        args[2] = r;
        args[3] = g;
        args[4] = b;
        args[12] = 0x01;
        var resp = Request(_deviceIndex, _rgbFeatureIndex, _rgbFeaturePage == 0x8071 ? (byte)0x10 : (byte)0x30, args);
        return resp == null ? "<timeout>" : BitConverter.ToString(resp);
    }

    protected override void SendColors(uint[] colors)
    {
        EnsureSoftwareControl();
        byte setEffectCmd = _rgbFeaturePage == 0x8071 ? (byte)0x10 : (byte)0x30;
        for (byte led = 0; led < _fixedEffectIndex.Length && led < colors.Length; led++)
        {
            var (r, g, b) = Split(colors[led]);
            var args = new byte[13];
            args[0] = led;
            args[1] = _fixedEffectIndex[led];
            args[2] = r;
            args[3] = g;
            args[4] = b;
            args[12] = 0x01; // persistence: RAM only (no flash writes)
            Request(_deviceIndex, _rgbFeatureIndex, setEffectCmd, args, 150);
        }
    }

    /// <summary>Sends a HID++ long report and waits for the matching response.
    /// Returns the 16 data bytes, or null on timeout/error.</summary>
    private byte[]? Request(byte deviceIndex, byte featureIndex, byte command, byte[] args, int timeoutMs = 400)
    {
        var report = new byte[20];
        report[0] = 0x11;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = command;
        Array.Copy(args, 0, report, 4, Math.Min(args.Length, 16));
        _stream.Write(report);

        for (int i = 0; i < 14; i++)
        {
            var resp = _stream.Read(timeoutMs);
            if (resp == null) return null;
            if (resp[0] != 0x11 || resp[1] != deviceIndex) continue;       // notification/other traffic
            if (resp[2] == 0xFF || resp[2] == 0x8F)                        // HID++ error reply
            {
                if (resp[3] == featureIndex) return null;
                continue;
            }
            if (resp[2] == featureIndex && resp[3] == command)
                return resp.Skip(4).Take(16).ToArray();
        }
        return null;
    }

    public override void Dispose() => _stream.Dispose();
}

/// <summary>
/// Direct driver for Gigabyte ITE RGB MCUs speaking the RGB Fusion 2 USB protocol:
/// 64-byte HID feature reports with report id 0xCC. Covers the newer Gigabyte-VID
/// module (0414:A100) that OpenRGB does not yet detect, plus the classic ITE PIDs.
/// </summary>
public sealed class GigabyteFusion2Device : DirectRgbDevice
{
    private const byte ReportId = 0xCC;
    private const int ZoneCount = 8;           // effect headers 0x20..0x27
    private const int StripLeds = 64;          // LEDs streamed per ARGB header (0x34 range enum 1)
    private const byte HdrDled1Argb = 0x58;    // IT8297/IT5702 addressable D_LED headers
    private const byte HdrDled2Argb = 0x59;

    private readonly HidStream _stream;
    private string _name = "Gigabyte RGB controller";
    private bool _stripsDirect;                // builtin strip effects disabled via 0x32 (= addressable mode)
    private uint[]? _zoneCache;                // last colours sent to the 8 effect zones
    private (int R, int G, int B) _cal1 = (0, 1, 2); // triplet byte positions per strip (calibration)
    private (int R, int G, int B) _cal2 = (0, 1, 2);

    public override string Name => _name;
    public override int LedCount => StripLeds;
    public override string Source => "Gigabyte USB";
    public override string Kind => "USB";

    private GigabyteFusion2Device(HidStream stream) => _stream = stream;

    public static GigabyteFusion2Device? TryCreate(List<HidDeviceInfo> hidDevices)
    {
        foreach (var info in hidDevices)
        {
            bool match = (info.Vid == 0x0414 && info.Pid == 0xA100) ||
                         (info.Vid == 0x048D && info.Pid is 0x8297 or 0x8950 or 0x5702 or 0x5711);
            if (!match || info.FeatureLen < 64) continue;
            GigabyteFusion2Device? drv = null;
            try
            {
                drv = new GigabyteFusion2Device(new HidStream(info));
                if (drv.Init()) return drv;
                drv.Dispose();
            }
            catch
            {
                drv?.Dispose();
            }
        }
        return null;
    }

    private bool Init()
    {
        SendCommand(0x60, 0x00); // request hardware info refresh
        var report = _stream.GetFeature(ReportId);
        if (report == null) return false;

        // IT8297Report: product string at offset 12, 28 chars; support flag at 11
        string product = Encoding.ASCII.GetString(report, 12, 28).TrimEnd('\0', ' ');
        if (product.Length < 3 || product.Any(c => c < 32 || c > 126)) return false;
        _name = $"Gigabyte {product}";

        byte supportFlag = report[11];
        if (supportFlag >= 0x02) SendCommand(0x48, 0x00); // lamp array (MSDL) off
        SendCommand(0x31, 0x00);                          // beat mode off

        // RGB byte order per strip, as calibrated on the chip (cal_strip0/1 in the
        // info report). 0 / garbage = uncalibrated, keep plain RGB.
        if (report.Length >= 52)
        {
            _cal1 = ParseCalibration(BitConverter.ToUInt32(report, 44)) ?? _cal1;
            _cal2 = ParseCalibration(BitConverter.ToUInt32(report, 48)) ?? _cal2;
        }
        SendCommand(0x34, 0x11, 0x00); // both D_LED strips: 64-LED range
        DebugLog.Log($"gigabyte cal: strip1={_cal1} strip2={_cal2}  (name '{_name}')");
        return true;
    }

    private static (int R, int G, int B)? ParseCalibration(uint value)
    {
        int r = (int)(value >> 16) & 0xFF, g = (int)(value >> 8) & 0xFF, b = (int)value & 0xFF;
        bool valid = r < 3 && g < 3 && b < 3 && r != g && r != b && g != b;
        return valid ? (r, g, b) : null;
    }

    protected override void SendColors(uint[] colors)
    {
        // Addressable path: the fans daisy-chain on the D_LED headers, so spatial
        // effects (the rainbow wave) only exist as per-LED strip data — zone packets
        // can only paint a whole chain one colour.
        EnsureStripsDirect();
        SendStrip(HdrDled1Argb, colors, _cal1);
        SendStrip(HdrDled2Argb, colors, _cal2);

        // The 8 effect zones (plain RGB headers) follow by sampling the strip, but
        // only when their colour actually changed — keeps the per-frame USB traffic low.
        _zoneCache ??= Enumerable.Repeat(0xFFFFFFFFu, ZoneCount).ToArray();
        bool changed = false;
        for (int zone = 0; zone < ZoneCount; zone++)
        {
            uint c = colors[Math.Min(zone * (StripLeds / ZoneCount), colors.Length - 1)];
            if (_zoneCache[zone] == c) continue;
            _zoneCache[zone] = c;
            changed = true;
            var (r, g, b) = Split(c);
            var pkt = new byte[64];
            pkt[0] = ReportId;
            pkt[1] = (byte)(0x20 + zone);
            BitConverter.GetBytes(1u << zone).CopyTo(pkt, 2);  // zone select mask
            pkt[11] = 0x01;                                    // EFFECT_STATIC
            pkt[12] = 0xFF;                                    // max brightness
            pkt[14] = b;                                       // color0 = 0x00RRGGBB little-endian
            pkt[15] = g;
            pkt[16] = r;
            // all periods 0 = immediate ("direct") application
            if (!_stream.SetFeature(pkt)) throw new IOException("Gigabyte controller rejected zone packet");
        }
        if (changed) SendCommand(0x28, 0xFF, 0x00); // apply to all zones
    }

    /// <summary>Disable the builtin effect engine on both D_LED headers so they show
    /// streamed per-LED data (RGB Fusion "direct" mode). Re-enabled for firmware effects.</summary>
    private void EnsureStripsDirect()
    {
        if (_stripsDirect) return;
        SendCommand(0x32, 0x03); // bit0 = D_LED1, bit1 = D_LED2
        _stripsDirect = true;
        _zoneCache = null;
    }

    private void SendStrip(byte header, uint[] colors, (int R, int G, int B) cal)
    {
        const int ledsPerPkt = 19; // 19 RGB triplets fit a 64-byte report
        for (int start = 0; start < colors.Length; start += ledsPerPkt)
        {
            int count = Math.Min(ledsPerPkt, colors.Length - start);
            var pkt = new byte[64];
            pkt[0] = ReportId;
            pkt[1] = header;
            BitConverter.GetBytes((ushort)(start * 3)).CopyTo(pkt, 2); // byte offset into the strip
            pkt[4] = (byte)(count * 3);
            for (int i = 0; i < count; i++)
            {
                var (r, g, b) = Split(colors[start + i]);
                int off = 5 + i * 3;
                pkt[off + cal.R] = r;
                pkt[off + cal.G] = g;
                pkt[off + cal.B] = b;
            }
            if (!_stream.SetFeature(pkt)) throw new IOException("Gigabyte controller rejected strip packet");
        }
    }

    public override bool TrySetHardwareEffect(HwEffect kind, Color color)
    {
        // Run the effect on the IT8297 firmware (smooth, off its own clock). USB feature
        // reports are robust here — unlike the GPU's I2C bus, this won't wedge.
        // PktEffect layout: periods (u16 LE rise/fall/hold) live at 22/24/26, effect
        // params at 30+. effect_param0 is the colour count for the cycle — leaving it
        // 0 freezes the cycle on its first colour (solid red).
        byte effect, brightness = 0xFF, param0 = 0;
        bool useColor;
        ushort p0, p1, p2;
        switch (kind)
        {
            case HwEffect.Rainbow:
                // The IT5702 firmware has no working wave: type 6 (OpenRGB "Wave",
                // p0=55/param0=7/param1=1) just goes dark, and colour-cycle is not a
                // wave. Decline so the app streams the wave along the LED chain itself.
                return false;
            case HwEffect.Cycle:                  // colour cycle through 7 hues in sync, mid speed
                effect = 0x04; useColor = false; p0 = 700; p1 = 500; p2 = 0; param0 = 7; break;
            case HwEffect.Breathing:              // pulse; firmware quirk: brightness above 100 misbehaves
                effect = 0x02; useColor = true; brightness = 100; p0 = 800; p1 = 800; p2 = 200; break;
            case HwEffect.Flashing:               // blink: 100ms rise/fall, hold between flashes
                effect = 0x03; useColor = true; p0 = 100; p1 = 100; p2 = 1100; break;
            default: return false;
        }
        // Firmware effects run on the builtin engine — re-enable it on the strips
        // (streamed/direct mode disables it via 0x32).
        if (_stripsDirect)
        {
            SendCommand(0x32, 0x00);
            _stripsDirect = false;
        }
        _zoneCache = null;
        for (int zone = 0; zone < ZoneCount; zone++)
        {
            var pkt = new byte[64];
            pkt[0] = ReportId;
            pkt[1] = (byte)(0x20 + zone);
            BitConverter.GetBytes(1u << zone).CopyTo(pkt, 2);   // zone select mask
            pkt[11] = effect;
            pkt[12] = brightness;                               // max brightness (min at [13] stays 0)
            if (useColor) { pkt[14] = color.B; pkt[15] = color.G; pkt[16] = color.R; }
            BitConverter.GetBytes(p0).CopyTo(pkt, 22);
            BitConverter.GetBytes(p1).CopyTo(pkt, 24);
            BitConverter.GetBytes(p2).CopyTo(pkt, 26);
            pkt[30] = param0;
            if (!_stream.SetFeature(pkt)) return false;
        }
        SendCommand(0x28, 0xFF, 0x00); // apply to all zones
        return true;
    }

    private void SendCommand(byte a, byte b, byte c = 0)
    {
        var pkt = new byte[64];
        pkt[0] = ReportId;
        pkt[1] = a;
        pkt[2] = b;
        pkt[3] = c;
        if (!_stream.SetFeature(pkt)) throw new IOException("Gigabyte controller rejected command");
    }

    public override void Dispose() => _stream.Dispose();
}

/// <summary>Wraps a device exposed by the OpenRGB SDK server.</summary>
public sealed class OpenRgbRemoteDevice : IRgbDevice
{
    internal const int DeviceTypeGpu = 2;

    private readonly OpenRgbClient _client;
    private readonly OpenRgbDevice _device;
    private uint[]? _lastColors;
    private DateTime _lastSend = DateTime.MinValue;

    public OpenRgbRemoteDevice(OpenRgbClient client, OpenRgbDevice device)
    {
        _client = client;
        _device = device;
    }

    public string Name => _device.Name;
    public int LedCount => _device.LedCount;
    public string Source => "OpenRGB";

    public string Kind => _device.Type switch
    {
        0 => "MB",
        1 => "RAM",
        2 => "GPU",
        3 => "COOLER",
        4 => "STRIP",
        5 => "KB",
        6 => "MOUSE",
        8 => "HEADSET",
        _ => "DEV"
    };

    /// <summary>When true, a GPU's light bar follows the ARGB sync cable (Sapphire
    /// "External Control" mode): Prepare selects that mode, and streaming / firmware
    /// effects leave the card alone so nothing yanks it back to I2C control.</summary>
    public static bool GpuFollowExternal;

    public void Prepare()
    {
        if (_device.Type == DeviceTypeGpu && GpuFollowExternal)
        {
            var external = _device.Modes.FirstOrDefault(m => m.Name.Contains("External", StringComparison.OrdinalIgnoreCase));
            if (external != null)
            {
                DebugLog.Log($"  prepare {_device.Name}: mode '{external.Name}' (follow ARGB cable)");
                _client.UpdateMode(_device.Index, external);
                _lastSend = DateTime.UtcNow;
                _lastColors = null;
                return;
            }
        }
        _client.SetCustomMode(_device.Index);
        // Write the direct mode to the hardware too — some controllers (Sapphire
        // Nitro Glow GPUs) otherwise keep running their onboard effect.
        var direct = _device.Modes.FirstOrDefault(m => (m.Flags & OpenRgbMode.FlagPerLedColor) != 0)
                  ?? _device.Modes.FirstOrDefault(m => m.Name is "Direct" or "Custom" or "Static");
        if (_device.Type == DeviceTypeGpu) DebugLog.Log($"  prepare {_device.Name}: mode '{direct?.Name ?? "none"}'");
        if (direct != null) { _client.MaximizeBrightness(direct); _client.UpdateMode(_device.Index, direct); }
        _lastColors = null;            // force the next streamed frame through (don't dedupe across a mode change)
        _lastSend = DateTime.UtcNow;   // give the fragile GPU MCU 500ms between the mode write and the first frame
    }

    public bool TrySetHardwareEffect(HwEffect kind, Color color)
    {
        // Following the ARGB cable: the bar mirrors whatever the cable source shows.
        if (_device.Type == DeviceTypeGpu && GpuFollowExternal) return false;
        // Only single-colour controllers benefit (e.g. the GPU): they can't show a spatial
        // pattern and are rate-capped. Multi-zone strips keep the synced software rendering.
        if (_device.LedCount > 2) return false;

        // Onboard switching wedges some GPUs ("stuck in rainbow"); the caller exits cleanly by
        // forcing a re-detect (Prepare on a freshly-queried device returns it to direct control).
        string[] keywords = kind switch
        {
            HwEffect.Rainbow => new[] { "Rainbow Wave", "Rainbow", "Spectrum Cycle", "Spectrum" },
            HwEffect.Cycle => new[] { "Spectrum Cycle", "Spectrum", "Rainbow", "Cycle" },
            HwEffect.Breathing => new[] { "Breathing", "Breath" },
            HwEffect.Flashing => new[] { "Flashing", "Blinking", "Blink", "Strobe" },
            _ => Array.Empty<string>()
        };
        OpenRgbMode? mode = null;
        foreach (var kw in keywords)
        {
            mode = _device.Modes.FirstOrDefault(m => m.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (mode != null) break;
        }
        if (mode == null) return false;
        DebugLog.Log($"  hwfx {_device.Name}: {kind} -> '{mode.Name}'");
        _client.MaximizeBrightness(mode);
        _client.UpdateMode(_device.Index, mode);
        _lastColors = null;
        return true;
    }

    public void SetColors(uint[] colors)
    {
        if (_device.Type == DeviceTypeGpu && GpuFollowExternal) return; // bar is on the cable
        var now = DateTime.UtcNow;
        bool changed = _lastColors == null || !colors.AsSpan().SequenceEqual(_lastColors);
        if (_device.Type == DeviceTypeGpu)
        {
            // GPU LED controllers are fragile little I2C MCUs; streaming colors at the
            // full frame rate can lock up their firmware until the next cold boot.
            // Only forward actual changes, at most ~2 per second.
            if (!changed) return;
            if ((now - _lastSend).TotalMilliseconds < 500) return;
        }
        else
        {
            // MB / RAM controllers sit on the slow SMBus: re-sending an IDENTICAL frame every
            // tick makes the OpenRGB server busy-write the bus ~25x/sec for a colour that never
            // changes — a static effect alone pegged its CPU. Skip unchanged frames; a 1.5s
            // keep-alive still re-asserts the colour periodically so a controller that reverted
            // to its firmware default (ASUS Aura boards do this) recovers on its own.
            if (!changed && (now - _lastSend).TotalMilliseconds < 1500) return;
        }
        _lastColors = (uint[])colors.Clone();
        _lastSend = now;
        _client.UpdateLeds(_device.Index, colors);
    }

}

/// <summary>Builds the combined device list from direct HID drivers and OpenRGB.</summary>
public static class DeviceManager
{
    public class DetectionResult
    {
        public List<IRgbDevice> Devices = new();
        public List<string> Notes = new();
        public bool OpenRgbConnected;
    }

    public static DetectionResult DetectAll(OpenRgbClient openRgb, bool tryConnectOpenRgb, bool skipLogitech = false)
    {
        var result = new DetectionResult();
        var hid = HidNative.Enumerate();
        bool haveDirectLogitech = false;
        bool receiverPresent = hid.Any(h => h.Vid == LogitechHidppDevice.LogitechVid &&
                                            LogitechHidppDevice.ReceiverPids.Contains(h.Pid));

        try
        {
            var gigabyte = GigabyteFusion2Device.TryCreate(hid);
            if (gigabyte != null) result.Devices.Add(gigabyte);
        }
        catch (Exception ex) { result.Notes.Add("Gigabyte driver: " + ex.Message); }

        // When the user lets G HUB own the mouse (its RGB + macros), skip the whole HID++
        // probe: instant detect, no phantom row, and zero contention for the channel G HUB
        // holds. (The phantom-G915 filter below still runs so OpenRGB's misread stays hidden.)
        if (!skipLogitech)
        {
            try
            {
                var logitech = LogitechHidppDevice.TryCreate(hid);
                if (logitech != null)
                {
                    result.Devices.Add(logitech);
                    haveDirectLogitech = true;
                }
            }
            catch (Exception ex) { result.Notes.Add("Logitech driver: " + ex.Message); }
            if (receiverPresent && !haveDirectLogitech)
                result.Notes.Add("Logitech wireless device did not respond - move the mouse and Rescan");
        }

        // Native Sapphire GPU driver (ADL I2C, atiadlxx.dll, no admin) — talks straight
        // to the card's Nitro Glow controller, completely independent of the OpenRGB
        // server. This is what makes GPU RGB work at every boot: OpenRGB enumerates
        // hardware only at its own startup and routinely loses the I2C bus race to the
        // vendor RGB stacks (SignalRGB/AURA), so the GPU was vanishing until a manual
        // server restart. When this claims the card we exclude OpenRGB's own GPU
        // controller below, so the chip at 0x28 has exactly one master.
        bool haveNativeGpu = false;
        try
        {
            var nativeGpu = SapphireNitroGlowDevice.TryCreate(out string gpuNote);
            if (nativeGpu != null) { result.Devices.Add(nativeGpu); haveNativeGpu = true; }
            else if (!string.IsNullOrEmpty(gpuNote)) result.Notes.Add("GPU (native): " + gpuNote);
        }
        catch (Exception ex) { result.Notes.Add("Native GPU driver: " + ex.Message); }

        if (tryConnectOpenRgb)
        {
            try
            {
                // Re-enumerate every detect, not just the first connect: the OpenRGB
                // server discovers some controllers late (the Sapphire GPU's I2C scan is
                // slow and can lose a race with TRIXX at boot). Without this, an already-
                // connected client keeps reusing the stale device list captured at the
                // first Connect(), so "Rescan devices" never brings a late GPU back.
                if (!openRgb.Connected)
                {
                    openRgb.Connect();
                }
                else
                {
                    try { openRgb.RefreshDevices(); }
                    catch { openRgb.Connect(); } // socket went stale: reconnect from scratch
                }
                result.OpenRgbConnected = true;
                foreach (var d in openRgb.Devices)
                {
                    // OpenRGB misidentifies the multi-device LIGHTSPEED receiver (046D:C547)
                    // as a G915 keyboard; while such a receiver is present, the "G915" entry
                    // is a phantom regardless of whether our direct driver came up.
                    if (receiverPresent && d.Name.Contains("G915")) continue;
                    // The native ADL driver already owns the GPU; skip OpenRGB's GPU
                    // controller so two I2C masters don't both drive 0x28 (which can
                    // wedge the Sapphire MCU until a cold boot).
                    if (haveNativeGpu && d.Type == OpenRgbRemoteDevice.DeviceTypeGpu) continue;
                    result.Devices.Add(new OpenRgbRemoteDevice(openRgb, d));
                }
            }
            catch (Exception ex)
            {
                result.Notes.Add("OpenRGB not reachable: " + ex.Message);
            }
        }
        return result;
    }
}
