using System.Runtime.InteropServices;
using System.Text;

namespace RGBCommander;

/// <summary>P/Invoke surface for AMD ADL sensor + Overdrive8 entry points, the ones a
/// GPU monitor/control tool needs (temps, fan, clocks, power, load, and fan-curve
/// control). Separate from AdlNative so the RGB/I2C path stays untouched. All calls are
/// user-mode, no admin, and ship with every Radeon driver via atiadlxx.dll.</summary>
public static class AdlOd
{
    public delegate IntPtr MainMemoryAlloc(int size);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Main_Control_Create(MainMemoryAlloc callback, int enumConnectedAdapters, out IntPtr context);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Main_Control_Destroy(IntPtr context);

    // iVersion: 5/6/7/8 -> Overdrive generation the adapter speaks.
    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Overdrive_Caps(IntPtr context, int adapterIndex, out int supported, out int enabled, out int version);

    // Instant sensor snapshot; does NOT require a PMLog session to be started.
    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapterIndex, IntPtr lpDataOutput);

    // Overdrive8: read the capability table (per-setting min/max/default) and current values.
    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Overdrive8_Init_Setting_Get(IntPtr context, int adapterIndex, IntPtr lpInitSetting);

    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Overdrive8_Current_Setting_Get(IntPtr context, int adapterIndex, IntPtr lpCurrentSetting);

    // Applies a set of OD8 changes. lpCurrentSetting receives the post-write state.
    [DllImport("atiadlxx.dll")]
    public static extern int ADL2_Overdrive8_Setting_Set(IntPtr context, int adapterIndex, IntPtr lpSetSetting, IntPtr lpCurrentSetting);

    public const int Ok = 0;

    // ADLPMLogDataOutput = { int size; ADLSingleSensorData sensors[MAX]; } with
    // ADLSingleSensorData = { int supported; int value; }. Header caps it at 256.
    public const int PmLogMaxSensors = 256;

    // Generous cap for the OD8 setting tables; the structs carry their own `count`.
    public const int Od8MaxSettings = 80;
}

/// <summary>The subset of PMLog sensor IDs we care about. Numeric values follow the
/// ADL SDK's ADL_PMLOG_SENSORS enum, but drivers have shifted these historically — the
/// probe dumps ALL slots so we can confirm the real mapping on this card before trusting
/// any single ID.</summary>
public enum PmSensor
{
    ClkGfx = 1,
    ClkMem = 2,
    ClkSoc = 3,
    TempEdge = 8,
    TempMem = 9,
    FanRpm = 14,
    FanPercent = 15,
    SocPower = 17,
    ActivityGfx = 19,
    ActivityMem = 20,
    GfxVoltage = 21,
    AsicPower = 23,
    TempHotspot = 27,
    TempGfx = 28,
    BoardPower = 32,
}

/// <summary>One row of the Overdrive8 capability table.</summary>
public readonly record struct Od8Setting(int FeatureId, int Min, int Max, int Default);

/// <summary>Owns an ADL2 context aimed at the first AMD adapter, and exposes the read-only
/// sensor + Overdrive8 capability queries. Write support (fan control) is added on top of
/// this once the probe confirms the setting IDs. All entry points are locked so Dispose
/// can't race an in-flight ADL call.</summary>
public sealed class AdlGpuSensors : IDisposable
{
    private static readonly AdlOd.MainMemoryAlloc AllocCb = size =>
    {
        try { return Marshal.AllocCoTaskMem(size); }
        catch { return IntPtr.Zero; }
    };

    private readonly object _sync = new();
    private IntPtr _ctx;
    public int AdapterIndex { get; }
    public string AdapterName { get; }

    private AdlGpuSensors(IntPtr ctx, int adapterIndex, string adapterName)
    {
        _ctx = ctx;
        AdapterIndex = adapterIndex;
        AdapterName = adapterName;
    }

    /// <summary>Opens ADL and locks onto the first present AMD adapter (one entry per bus).
    /// Returns null with a note when no Radeon adapter is reachable.</summary>
    public static AdlGpuSensors? TryOpen(out string note)
    {
        note = "";
        IntPtr ctx = IntPtr.Zero;
        try
        {
            if (AdlOd.ADL2_Main_Control_Create(AllocCb, 1, out ctx) != AdlOd.Ok || ctx == IntPtr.Zero)
            {
                note = "ADL init failed (is a Radeon driver installed?)";
                return null;
            }
            AdlNative.AdapterInfoX2? amd = null;
            var seen = new HashSet<int>();
            foreach (var a in AdlI2cBus.ListAdapters(ctx))
            {
                if (a.Present == 0 || !seen.Add(a.BusNumber)) continue;
                if (!a.PNPString.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)) continue;
                amd = a;
                break;
            }
            if (amd == null)
            {
                note = "no AMD adapter found via ADL";
                return null;
            }
            var s = new AdlGpuSensors(ctx, amd.Value.AdapterIndex, amd.Value.AdapterName.Trim());
            ctx = IntPtr.Zero; // ownership moved
            return s;
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
            if (ctx != IntPtr.Zero) AdlOd.ADL2_Main_Control_Destroy(ctx);
        }
    }

    public bool TryGetOverdriveCaps(out int supported, out int enabled, out int version)
    {
        supported = enabled = version = 0;
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            return AdlOd.ADL2_Overdrive_Caps(_ctx, AdapterIndex, out supported, out enabled, out version) == AdlOd.Ok;
        }
    }

    /// <summary>Snapshots every PMLog sensor slot. Returns supported flag + raw value for
    /// each of the 256 slots so callers can map IDs empirically.</summary>
    public bool TryReadPmLog(out (int Supported, int Value)[] slots)
    {
        slots = Array.Empty<(int, int)>();
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            int bytes = 4 + AdlOd.PmLogMaxSensors * 8;
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                // zero it so unsupported slots read as {0,0} rather than stack garbage
                for (int i = 0; i < bytes; i += 4) Marshal.WriteInt32(buf, i, 0);
                if (AdlOd.ADL2_New_QueryPMLogData_Get(_ctx, AdapterIndex, buf) != AdlOd.Ok) return false;
                var outp = new (int, int)[AdlOd.PmLogMaxSensors];
                for (int i = 0; i < AdlOd.PmLogMaxSensors; i++)
                {
                    int off = 4 + i * 8;
                    outp[i] = (Marshal.ReadInt32(buf, off), Marshal.ReadInt32(buf, off + 4));
                }
                slots = outp;
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>Reads one PMLog sensor by ID (only trust after the probe confirms the map).</summary>
    public bool TryReadSensor(PmSensor id, out int value)
    {
        value = 0;
        if (!TryReadPmLog(out var slots)) return false;
        int i = (int)id;
        if (i < 0 || i >= slots.Length || slots[i].Supported == 0) return false;
        value = slots[i].Value;
        return true;
    }

    /// <summary>Reads the OD8 capability table: bitmask of supported features plus a
    /// (featureID, min, max, default) row per setting.</summary>
    public bool TryGetOd8Init(out int capabilities, out Od8Setting[] table)
    {
        capabilities = 0;
        table = Array.Empty<Od8Setting>();
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            // ADLOD8InitSetting = { int count; int overdrive8Capabilities; ADLOD8SingleInitSetting[count]; }
            // ADLOD8SingleInitSetting = { int featureID; int minValue; int maxValue; int defaultValue; }
            int bytes = 8 + AdlOd.Od8MaxSettings * 16;
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                for (int i = 0; i < bytes; i += 4) Marshal.WriteInt32(buf, i, 0);
                if (AdlOd.ADL2_Overdrive8_Init_Setting_Get(_ctx, AdapterIndex, buf) != AdlOd.Ok) return false;
                int count = Marshal.ReadInt32(buf, 0);
                capabilities = Marshal.ReadInt32(buf, 4);
                if (count < 0 || count > AdlOd.Od8MaxSettings) count = AdlOd.Od8MaxSettings;
                var rows = new Od8Setting[count];
                for (int i = 0; i < count; i++)
                {
                    int off = 8 + i * 16;
                    rows[i] = new Od8Setting(
                        Marshal.ReadInt32(buf, off),
                        Marshal.ReadInt32(buf, off + 4),
                        Marshal.ReadInt32(buf, off + 8),
                        Marshal.ReadInt32(buf, off + 12));
                }
                table = rows;
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>Raw int dump of the OD8 init buffer so the probe can reveal the true
    /// struct layout when `count` comes back implausible.</summary>
    public bool TryGetOd8InitRaw(int intCount, out int[] raw)
    {
        raw = Array.Empty<int>();
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            int bytes = 8 + AdlOd.Od8MaxSettings * 16;
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                for (int i = 0; i < bytes; i += 4) Marshal.WriteInt32(buf, i, unchecked((int)0xDEADBEEF));
                if (AdlOd.ADL2_Overdrive8_Init_Setting_Get(_ctx, AdapterIndex, buf) != AdlOd.Ok) return false;
                int n = Math.Min(intCount, bytes / 4);
                var outp = new int[n];
                for (int i = 0; i < n; i++) outp[i] = Marshal.ReadInt32(buf, i * 4);
                raw = outp;
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    /// <summary>Reads the current OD8 setting values (parallel to the init table's rows).</summary>
    public bool TryGetOd8Current(out int[] values)
    {
        values = Array.Empty<int>();
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return false;
            // ADLOD8CurrentSetting = { int count; int Od8SettingTable[count]; }
            int bytes = 4 + AdlOd.Od8MaxSettings * 4;
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                for (int i = 0; i < bytes; i += 4) Marshal.WriteInt32(buf, i, 0);
                if (AdlOd.ADL2_Overdrive8_Current_Setting_Get(_ctx, AdapterIndex, buf) != AdlOd.Ok) return false;
                int count = Marshal.ReadInt32(buf, 0);
                // some drivers leave `count` at 0 and expect the fixed OD8_COUNT (~47)
                if (count <= 0 || count > AdlOd.Od8MaxSettings) count = 47;
                var vals = new int[count];
                for (int i = 0; i < count; i++) vals[i] = Marshal.ReadInt32(buf, 4 + i * 4);
                values = vals;
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            try { AdlOd.ADL2_Main_Control_Destroy(_ctx); } catch { }
            _ctx = IntPtr.Zero;
        }
    }
}

/// <summary>Read-only probe (--test-gpu-sensors): dumps Overdrive caps, every PMLog sensor
/// slot, and the full Overdrive8 setting table (min/max/default + current) to
/// gpu-sensors.txt next to the exe. Writes nothing to the card. This is what we read to
/// map sensor/fan IDs before building the real GPU Guardian.</summary>
public static class GpuSensorDiag
{
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine($"GPU Guardian sensor probe  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        try
        {
            var s = AdlGpuSensors.TryOpen(out string note);
            if (s == null)
            {
                log.AppendLine("OPEN FAILED: " + note);
            }
            else
            {
                using (s)
                {
                    log.AppendLine($"adapter [{s.AdapterIndex}] {s.AdapterName}");
                    log.AppendLine();

                    if (s.TryGetOverdriveCaps(out int sup, out int en, out int ver))
                        log.AppendLine($"Overdrive caps: supported={sup} enabled={en} version={ver}");
                    else
                        log.AppendLine("Overdrive caps: query FAILED");
                    log.AppendLine();

                    log.AppendLine("-- PMLog sensors (supported slots only) --");
                    if (s.TryReadPmLog(out var slots))
                    {
                        int shown = 0;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if (slots[i].Supported == 0 && slots[i].Value == 0) continue;
                            string guess = i switch
                            {
                                (int)PmSensor.ClkGfx => "GFX clock MHz?",
                                (int)PmSensor.ClkMem => "MEM clock MHz?",
                                (int)PmSensor.ClkSoc => "SOC clock MHz?",
                                (int)PmSensor.TempEdge => "edge temp C?",
                                (int)PmSensor.TempMem => "mem temp C?",
                                (int)PmSensor.FanRpm => "fan RPM?",
                                (int)PmSensor.FanPercent => "fan %?",
                                (int)PmSensor.SocPower => "SOC power W?",
                                (int)PmSensor.ActivityGfx => "GPU load %?",
                                (int)PmSensor.ActivityMem => "MEM load %?",
                                (int)PmSensor.GfxVoltage => "GFX mV?",
                                (int)PmSensor.AsicPower => "ASIC power W?",
                                (int)PmSensor.TempHotspot => "hotspot temp C?",
                                (int)PmSensor.TempGfx => "gfx temp C?",
                                (int)PmSensor.BoardPower => "board power W?",
                                _ => ""
                            };
                            log.AppendLine($"  [{i,3}] supported={slots[i].Supported}  value={slots[i].Value,-8} {guess}");
                            shown++;
                        }
                        log.AppendLine($"  ({shown} non-empty slots)");
                    }
                    else log.AppendLine("  PMLog query FAILED");
                    log.AppendLine();

                    log.AppendLine("-- Overdrive8 init buffer, raw ints (offset0=count?, offset1=caps) --");
                    if (s.TryGetOd8InitRaw(2 + 47 * 4, out int[] raw))
                    {
                        for (int i = 0; i < raw.Length; i += 4)
                        {
                            var parts = new List<string>();
                            for (int j = i; j < i + 4 && j < raw.Length; j++) parts.Add($"[{j,3}]={raw[j],-8}");
                            log.AppendLine("  " + string.Join(" ", parts));
                        }
                    }
                    else log.AppendLine("  OD8 init raw query FAILED");
                    log.AppendLine();

                    log.AppendLine("-- Overdrive8 current settings, raw ints --");
                    if (s.TryGetOd8Current(out int[] curRaw))
                    {
                        for (int i = 0; i < curRaw.Length; i += 8)
                        {
                            var parts = new List<string>();
                            for (int j = i; j < i + 8 && j < curRaw.Length; j++) parts.Add($"[{j,2}]={curRaw[j],-6}");
                            log.AppendLine("  " + string.Join(" ", parts));
                        }
                    }
                    else log.AppendLine("  OD8 current raw query FAILED");
                }
            }
            log.AppendLine();
            log.AppendLine("RESULT: OK");
        }
        catch (Exception ex)
        {
            log.AppendLine("PROBE FAILED: " + ex);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gpu-sensors.txt"), log.ToString());
    }
}
