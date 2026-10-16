namespace RGBCommander;

/// <summary>
/// The pretend hardware behind the design harnesses (<c>--test-main</c>, <c>--test-skin</c>): a rig shaped
/// like Erik's (fan hub, GPU, four RAM sticks, board) whose devices accept every write and touch nothing.
/// Lets the REAL main window be built, laid out and screenshotted while the live instance keeps the
/// single-instance mutex and drives the actual SMBus / I2C / USB hardware undisturbed.
/// </summary>
internal static class HarnessRig
{
    /// <summary>A device that exists only on screen: every write is accepted and dropped.</summary>
    internal sealed class Device : IRgbDevice
    {
        public Device(string name, int leds, string source, string kind)
        { Name = name; LedCount = leds; Source = source; Kind = kind; }
        public string Name { get; }
        public int LedCount { get; }
        public string Source { get; }
        public string Kind { get; }
        public void SetColors(uint[] colors) { }
    }

    /// <summary>What a detect pass returns in harness mode. Reported as "OpenRGB connected" so the window
    /// shows its normal state; nothing listens, so no reconnect path ever fires.</summary>
    public static DeviceManager.DetectionResult Detect()
    {
        var r = new DeviceManager.DetectionResult { OpenRgbConnected = true };
        r.Devices.Add(new Device("Gigabyte IT5702-GIGABYTE V0.0.8.0", 64, "Gigabyte USB", "USB"));
        r.Devices.Add(new Device("AMD Radeon RX 9060 XT", 1, "Native ADL", "GPU"));
        for (int i = 0; i < 4; i++) r.Devices.Add(new Device("ENE DRAM", 5, "OpenRGB", "RAM"));
        r.Devices.Add(new Device("ASUS ROG STRIX B550-F GAMING", 5, "OpenRGB", "MB"));
        // PRISMA_HARNESS_EXTRA=n: n more (distinct) devices, so the rail's lists must scroll
        if (int.TryParse(Environment.GetEnvironmentVariable("PRISMA_HARNESS_EXTRA"), out int extra))
            for (int i = 0; i < Math.Clamp(extra, 0, 40); i++)
                r.Devices.Add(new Device($"Test LED strip {i + 1}", 30, "OpenRGB", "USB"));
        return r;
    }
}
