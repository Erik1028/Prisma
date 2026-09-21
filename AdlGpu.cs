using System.Runtime.InteropServices;
using System.Text;

namespace RGBCommander;

/// <summary>P/Invoke surface for AMD's atiadlxx.dll (ships with every Radeon driver,
/// user-mode, no admin). Only the entry points needed for GPU I2C.</summary>
public static class AdlNative
{
    public delegate IntPtr MainMemoryAlloc(int size);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Main_Control_Create(MainMemoryAlloc callback, int enumConnectedAdapters, out IntPtr context);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Adapter_AdapterInfoX4_Get(IntPtr context, int adapterIndex, out int count, out IntPtr infos);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Display_WriteAndReadI2C(IntPtr context, int adapterIndex, ref ADLI2C data);

    // ADL SDK adl_defines.h — CONFIRMED on live hardware by AdlDiag (2026-06-12):
    // reads succeed with 1 and 3, fail with 0.
    public const int ActionRead = 1;            // ADL_DL_I2C_ACTIONREAD
    public const int ActionWrite = 2;           // ADL_DL_I2C_ACTIONWRITE
    public const int ActionReadRepeated = 3;    // ADL_DL_I2C_ACTIONREAD_REPEATEDSTART
    public const int Ok = 0;                    // ADL_OK

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLI2C
    {
        public int Size;
        public int Line;       // I2C line/bus on the card; the Nitro Glow MCU sits on line 1
        public int Address;    // 7-bit address shifted left (0x28 << 1)
        public int Offset;     // register offset for reads
        public int Action;
        public int Speed;      // kHz
        public int DataSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct AdapterInfoX2
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PNPString;
        public int OSDisplayIndex;
        public int InfoMask;
        public int InfoValue;
    }
}

/// <summary>SMBus-style byte register access to a device on a Radeon card's own I2C
/// bus, via ADL. One instance owns the ADL context. All entry points are locked, so
/// a Dispose can never race an in-flight transaction.</summary>
public sealed class AdlI2cBus : IDisposable
{
    // The allocator delegate must outlive every ADL call that may allocate, and must
    // honor malloc semantics: a managed exception may not unwind native ADL frames.
    private static readonly AdlNative.MainMemoryAlloc AllocCb = size =>
    {
        try { return Marshal.AllocCoTaskMem(size); }
        catch { return IntPtr.Zero; }
    };

    private readonly object _sync = new();
    private IntPtr _ctx;
    public int AdapterIndex { get; }
    public string AdapterName { get; }
    /// <summary>True when the picked adapter's PNP string carries the Sapphire
    /// sub-vendor (1DA2). Writes are only safe on a board whose 0x28 device is known.</summary>
    public bool IsSapphire { get; }

    private AdlI2cBus(IntPtr ctx, int adapterIndex, string adapterName, bool isSapphire)
    {
        _ctx = ctx;
        AdapterIndex = adapterIndex;
        AdapterName = adapterName;
        IsSapphire = isSapphire;
    }

    /// <summary>Opens ADL and locates the Radeon adapter, preferring a Sapphire
    /// (sub-vendor 1DA2) match. Returns null when no AMD adapter is reachable.</summary>
    public static AdlI2cBus? TryOpen(out string note)
    {
        note = "";
        IntPtr ctx = IntPtr.Zero;
        try
        {
            if (AdlNative.ADL2_Main_Control_Create(AllocCb, 1, out ctx) != AdlNative.Ok || ctx == IntPtr.Zero)
            {
                note = "ADL init failed (is a Radeon driver installed?)";
                return null;
            }
            var adapters = ListAdapters(ctx);
            // One physical card shows up once per display output; bus number identifies
            // the physical device, so consider the first entry per bus.
            var seen = new HashSet<int>();
            AdlNative.AdapterInfoX2? sapphire = null, anyAmd = null;
            foreach (var a in adapters)
            {
                if (a.Present == 0 || !seen.Add(a.BusNumber)) continue;
                if (!a.PNPString.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)) continue;
                anyAmd ??= a;
                if (a.PNPString.Contains("1DA2", StringComparison.OrdinalIgnoreCase)) { sapphire = a; break; }
            }
            var pick = sapphire ?? anyAmd;
            if (pick == null)
            {
                note = "no AMD adapter found via ADL";
                return null; // the finally below destroys ctx exactly once
            }
            var bus = new AdlI2cBus(ctx, pick.Value.AdapterIndex, pick.Value.AdapterName.Trim(), sapphire != null);
            ctx = IntPtr.Zero; // ownership moved to the bus
            return bus;
        }
        catch (DllNotFoundException)
        {
            note = "atiadlxx.dll not found (no Radeon driver)";
            return null;
        }
        catch (Exception ex)
        {
            note = "ADL: " + ex.Message;
            return null;
        }
        finally
        {
            if (ctx != IntPtr.Zero) AdlNative.ADL2_Main_Control_Destroy(ctx);
        }
    }

    public static List<AdlNative.AdapterInfoX2> ListAdapters(IntPtr ctx)
    {
        var list = new List<AdlNative.AdapterInfoX2>();
        int ret = AdlNative.ADL2_Adapter_AdapterInfoX4_Get(ctx, -1, out int count, out IntPtr buf);
        try
        {
            if (ret != AdlNative.Ok || buf == IntPtr.Zero) return list;
            int size = Marshal.SizeOf<AdlNative.AdapterInfoX2>();
            for (int i = 0; i < count; i++)
                list.Add(Marshal.PtrToStructure<AdlNative.AdapterInfoX2>(buf + i * size));
        }
        finally
        {
            // allocated by ADL through our callback — free even when ret != Ok
            if (buf != IntPtr.Zero) Marshal.FreeCoTaskMem(buf);
        }
        return list;
    }

    /// <summary>SMBus "read byte data": register offset in Offset, one data byte back.
    /// The buffer is pre-filled with a sentinel so an OK-without-data driver quirk
    /// surfaces as an implausible value instead of a fake 0.</summary>
    public bool ReadByte(int deviceAddr, byte reg, out byte value, int action = AdlNative.ActionRead)
    {
        const byte Sentinel = 0xA5;
        value = Sentinel;
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            IntPtr buf = Marshal.AllocHGlobal(2);
            try
            {
                Marshal.WriteByte(buf, Sentinel);
                var i2c = new AdlNative.ADLI2C
                {
                    Size = Marshal.SizeOf<AdlNative.ADLI2C>(),
                    Line = 1,
                    Address = deviceAddr << 1,
                    Offset = reg,
                    Action = action,
                    Speed = 100,
                    DataSize = 1,
                    Data = buf
                };
                if (AdlNative.ADL2_Display_WriteAndReadI2C(_ctx, AdapterIndex, ref i2c) != AdlNative.Ok) return false;
                value = Marshal.ReadByte(buf);
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>SMBus "write byte data": the buffer carries [register, value].</summary>
    public bool WriteByte(int deviceAddr, byte reg, byte value)
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            IntPtr buf = Marshal.AllocHGlobal(2);
            try
            {
                Marshal.WriteByte(buf, 0, reg);
                Marshal.WriteByte(buf, 1, value);
                var i2c = new AdlNative.ADLI2C
                {
                    Size = Marshal.SizeOf<AdlNative.ADLI2C>(),
                    Line = 1,
                    Address = deviceAddr << 1,
                    Offset = 0,
                    Action = AdlNative.ActionWrite,
                    Speed = 100,
                    DataSize = 2,
                    Data = buf
                };
                return AdlNative.ADL2_Display_WriteAndReadI2C(_ctx, AdapterIndex, ref i2c) == AdlNative.Ok;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            try { AdlNative.ADL2_Main_Control_Destroy(_ctx); } catch { }
            _ctx = IntPtr.Zero;
        }
    }
}

/// <summary>Native driver for the Sapphire Nitro Glow V3 RGB controller (the Nitro+
/// light bar) at I2C 0x28 on the card's own bus — no OpenRGB server involved.
/// The bar is multi-LED but single-zone: one RGB register triple drives all of it.</summary>
public sealed class SapphireNitroGlowDevice : IRgbDevice, IDisposable
{
    private const int Addr = 0x28;
    private const byte RegExternal = 0x0F, RegMode = 0x10, RegR = 0x1A, RegG = 0x1B, RegB = 0x1C, RegBrightness = 0x3E;
    private const byte ModeRainbow = 0, ModeColorCycle = 2, ModeCustom = 6;

    private readonly AdlI2cBus _bus;
    private uint[]? _lastColors;
    private DateTime _lastSend = DateTime.MinValue;
    private int _consecutiveFails;

    // Brightness reg 0x3E: writing it does NOT dim the onboard firmware effects on this card
    // (verified 2026-07-14 on live hardware — no change across 1..255; matches OpenRGB reporting
    // brightness_max=0). So we do NOT write it automatically. Kept only as a manual calibration
    // hook (`--gpu-bright <n>`) in case a future firmware/scale turns out to respond. To dim the
    // GPU under a firmware effect, the real route is software-rendering it (rate-capped) instead.
    /// <summary>Diagnostic only: write a raw 0x3E value to probe the brightness register against
    /// the physical bar. Not used in normal operation.</summary>
    public bool WriteBrightnessRaw(byte v)
    {
        DebugLog.Log($"  nitro glow raw brightness 0x3E = {v}");
        return _bus.WriteByte(Addr, RegBrightness, v);
    }

    public string Name { get; }
    public int LedCount => 1;
    public string Source => "AMD ADL I2C";
    public string Kind => "GPU";

    /// <summary>The MCU occasionally wedges under heavy multi-master SMBus contention: ADL
    /// still enumerates the adapter but every I2C write fails until a sleep/wake or cold boot
    /// re-inits it (an OpenRGB restart or Rescan does NOT clear it). After a run of failed
    /// writes (~3s at the 500ms pacing) report it so the UI can guide recovery instead of the
    /// render loop retrying in silence. The first write that lands resets it.</summary>
    public string? FaultNote => _consecutiveFails >= 6
        ? "GPU lighting stopped responding (Sapphire I2C bus wedged). Sleep the PC and wake it (or reboot) to recover."
        : null;

    private SapphireNitroGlowDevice(AdlI2cBus bus, string name)
    {
        _bus = bus;
        Name = name;
    }

    /// <summary>Claims the device only when (a) the adapter is verifiably a Sapphire
    /// board and (b) the mode register answers twice with the same plausible value —
    /// writes to an unverified chip at 0x28 could hit a power/monitoring controller.</summary>
    public static SapphireNitroGlowDevice? TryCreate(out string note)
    {
        var bus = AdlI2cBus.TryOpen(out note);
        if (bus == null) return null;
        if (!bus.IsSapphire)
        {
            note = $"'{bus.AdapterName}' is not a Sapphire board (no 1DA2 sub-vendor) - not touching its I2C";
            bus.Dispose();
            return null;
        }
        // ADL I2C reads are known flaky — demand two consistent, plausible answers
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (ProbeMode(bus, out byte m1) && ProbeMode(bus, out byte m2) && m1 == m2)
            {
                DebugLog.Log($"nitro glow probe ok: mode={m1}");
                return new SapphireNitroGlowDevice(bus, bus.AdapterName.Length > 0 ? bus.AdapterName : "Sapphire Radeon GPU");
            }
            Thread.Sleep(150);
        }
        note = "no Nitro Glow controller answered at 0x28";
        bus.Dispose();
        return null;
    }

    private static bool ProbeMode(AdlI2cBus bus, out byte mode)
    {
        if (!bus.ReadByte(Addr, RegMode, out mode)) return false;
        if (mode <= 7) return true;
        // 0xFF is both the External Control mode and an empty-bus all-ones read;
        // only believe it when the external-control flag corroborates.
        return mode == 0xFF && bus.ReadByte(Addr, RegExternal, out byte ext) && ext == 1;
    }

    public void Prepare()
    {
        // Diagnostic read FIRST: the MCU drops mode writes that land close before
        // other traffic, so the mode write must be the last transaction before the
        // settle window. Register 0x3E stays read-only on purpose: it read 6 on lit
        // hardware (2026-06-12), so its scale is NOT linear 0-255 and a blind write
        // could darken the bar. App-level brightness scales the RGB instead.
        if (_bus.ReadByte(Addr, RegBrightness, out byte bright))
            DebugLog.Log($"  nitro glow prepare, brightness reg reads {bright}");
        _bus.WriteByte(Addr, RegMode, ModeCustom);
        _lastColors = null;
        _lastSend = DateTime.UtcNow; // settle time before the first color write
    }

    public bool TrySetHardwareEffect(HwEffect kind, Color color)
    {
        byte mode = kind switch
        {
            HwEffect.Rainbow => ModeRainbow,
            HwEffect.Cycle => ModeColorCycle,
            _ => 0xFE // no breathing/flashing mode on this MCU: render in software
        };
        if (mode == 0xFE) return false;
        DebugLog.Log($"  nitro glow hwfx {kind} -> mode {mode}");
        if (!_bus.WriteByte(Addr, RegMode, mode)) return false;
        // A landed mode write proves the bus recovered. Clear the fault counter here too:
        // once the GPU runs a firmware effect it leaves the streamed SetColors path (the only
        // other reset site), so without this a stale wedge fault would stick forever.
        _consecutiveFails = 0;
        _lastColors = null;
        _lastSend = DateTime.UtcNow;
        return true;
    }

    public void SetColors(uint[] colors)
    {
        if (colors.Length == 0) return;
        // The MCU is fragile: forward only actual changes, at most ~2 per second
        // (the same pacing the OpenRGB path used). A paced-out or failed write is
        // retried naturally: _lastColors only records colors that fully landed.
        var now = DateTime.UtcNow;
        bool changed = _lastColors == null || !colors.AsSpan().SequenceEqual(_lastColors);
        if (!changed) return;
        if ((now - _lastSend).TotalMilliseconds < 500) return;
        _lastSend = now;

        byte r = (byte)(colors[0] & 0xFF), g = (byte)((colors[0] >> 8) & 0xFF), b = (byte)((colors[0] >> 16) & 0xFF);
        bool ok = _bus.WriteByte(Addr, RegR, r);
        ok &= _bus.WriteByte(Addr, RegG, g);
        ok &= _bus.WriteByte(Addr, RegB, b);
        if (ok)
        {
            _lastColors = (uint[])colors.Clone();
            _consecutiveFails = 0;
        }
        else
        {
            _lastColors = null;          // not recorded -> retried on the next frame
            _consecutiveFails++;
            DebugLog.Log($"  nitro glow color write failed ({_consecutiveFails}), will retry");
        }
    }

    public void Dispose() => _bus.Dispose();
}

/// <summary>Read-only hardware probe (--test-gpu-native): lists ADL adapters (with PNP
/// strings) and dumps the Nitro Glow registers with the candidate action constants,
/// without writing anything. Output: adl-diag.txt next to the exe.</summary>
public static class AdlDiag
{
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine($"Prisma ADL diagnostic  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        try
        {
            var bus = AdlI2cBus.TryOpen(out string note);
            if (bus == null)
            {
                log.AppendLine("BUS OPEN FAILED: " + note);
            }
            else
            {
                using (bus)
                {
                    log.AppendLine($"picked adapter index {bus.AdapterIndex}: {bus.AdapterName}  (Sapphire match: {bus.IsSapphire})");
                    log.AppendLine();
                    log.AppendLine("-- adapters (first entry per bus) --");
                    if (AdlNative.ADL2_Main_Control_Create(size => Marshal.AllocCoTaskMem(size), 1, out IntPtr listCtx) == AdlNative.Ok)
                    {
                        try
                        {
                            var seen = new HashSet<int>();
                            foreach (var a in AdlI2cBus.ListAdapters(listCtx))
                            {
                                if (a.Present == 0 || !seen.Add(a.BusNumber)) continue;
                                string pnp = a.PNPString.Length > 96 ? a.PNPString[..96] : a.PNPString;
                                log.AppendLine($"  [{a.AdapterIndex}] bus {a.BusNumber}  {a.AdapterName.Trim()}");
                                log.AppendLine($"      PNP: {pnp}");
                            }
                        }
                        finally { AdlNative.ADL2_Main_Control_Destroy(listCtx); }
                    }
                    log.AppendLine();
                    var regs = new (byte Reg, string Name)[]
                    {
                        (0x10, "mode"), (0x1A, "R"), (0x1B, "G"), (0x1C, "B"),
                        (0x3E, "brightness"), (0x0F, "external"),
                    };
                    foreach (int action in new[] { AdlNative.ActionRead, AdlNative.ActionReadRepeated, 0 })
                    {
                        log.AppendLine($"-- reads with iAction={action} --");
                        foreach (var (reg, name) in regs)
                        {
                            bool ok = bus.ReadByte(0x28, reg, out byte v, action);
                            log.AppendLine($"  reg 0x{reg:X2} ({name}): {(ok ? $"0x{v:X2} ({v})" : "FAILED")}");
                            Thread.Sleep(60);
                        }
                        log.AppendLine();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("DIAG FAILED: " + ex);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "adl-diag.txt"), log.ToString());
    }
}
