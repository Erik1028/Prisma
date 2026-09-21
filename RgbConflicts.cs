using System.Diagnostics;

namespace RGBCommander;

/// <summary>Detects other RGB-control software that fights Prisma/OpenRGB for the same
/// SMBus/I2C bus — the usual cause of RAM/motherboard/GPU flicker, a fluctuating device
/// count, and a wedged GPU MCU. Detection is by running process; the optional one-click
/// fix stops and disables the offenders' services (a single elevation prompt) and drops a
/// Restore-Prisma-RGB.cmd on the Desktop to undo it.</summary>
public static class RgbConflicts
{
    /// <summary>A known RGB stack that contends for the lighting bus. <see cref="Processes"/>
    /// are matched (without ".exe") against running processes to detect it; <see cref="Services"/>
    /// are stopped + disabled by the fix.</summary>
    public sealed record Contender(string Name, string[] Processes, string[] Services, string Why);

    /// <summary>The contenders we know about. OpenRGB is deliberately absent — it is Prisma's
    /// own backend, not a rival. Service names vary across versions; the fix best-efforts each
    /// (an unknown name just returns nonzero and is ignored).</summary>
    public static readonly IReadOnlyList<Contender> Known = new[]
    {
        new Contender("ASUS Aura / Armoury Crate",
            new[] { "LightingService", "AuraServiceUWP" },
            new[] { "LightingService" },
            "drives the RAM & motherboard RGB over the SMBus"),
        new Contender("SignalRGB",
            new[] { "SignalRgb", "SignalRgbLauncher" },
            new[] { "SignalRgb.Service" },
            "probes the GPU/SMBus for its own sync engine"),
        new Contender("Corsair iCUE",
            new[] { "iCUE", "Corsair.Service" },
            new[] { "Corsair Service" },
            "scans the SMBus for Corsair RGB"),
        new Contender("MSI Center / Mystic Light",
            new[] { "Mystic_Light", "MSI Center", "MSI.CentralServer" },
            new[] { "MSI_Center_Service", "Mystic_Light_Service" },
            "drives motherboard RGB over the SMBus"),
        new Contender("Razer Synapse / Chroma",
            new[] { "Razer Synapse 3", "RzSDKService", "Razer Central" },
            new[] { "Razer Synapse Service", "RzActionSvc" },
            "runs a Chroma SMBus broker"),
        new Contender("Gigabyte RGB Fusion",
            new[] { "RGBFusion", "GBT_RGBFusion", "GLEDInstaller" },
            new[] { "GBT_RGBFusion" },
            "controls the same Gigabyte chip Prisma drives over USB"),
        new Contender("ASRock Polychrome",
            new[] { "Polychrome", "ASRPolychromeRGB" },
            new[] { "ASRSVCService" },
            "drives motherboard RGB over the SMBus"),
    };

    /// <summary>A contender found running, with the process names actually seen.</summary>
    public sealed record Active(Contender Contender, string[] RunningProcesses);

    /// <summary>Returns the known contenders currently running (any of their processes
    /// present). Cheap: one process snapshot, name lookups against it.</summary>
    public static List<Active> Scan()
    {
        var found = new List<Active>();
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try { running.Add(p.ProcessName); } catch { } finally { p.Dispose(); }
            }
        }
        catch { return found; } // never let a health check crash the caller

        foreach (var c in Known)
        {
            var hit = c.Processes.Where(running.Contains).ToArray();
            if (hit.Length > 0) found.Add(new Active(c, hit));
        }
        return found;
    }

    /// <summary>Stops + disables the services of the given contenders and kills their
    /// processes, in a single elevated shell (one UAC prompt), then writes a
    /// Restore-Prisma-RGB.cmd to the Desktop that re-enables everything touched. Returns false
    /// (with a reason) if the user cancels elevation or there is nothing to do.</summary>
    public static bool Disable(IEnumerable<Active> targets, out string message)
    {
        var list = targets.ToList();
        if (list.Count == 0) { message = "Nothing to disable."; return false; }

        var doCmds = new List<string> { "@echo off" };
        var undoCmds = new List<string>();
        foreach (var a in list)
        {
            foreach (var svc in a.Contender.Services)
            {
                doCmds.Add($"sc stop \"{svc}\"");
                doCmds.Add($"sc config \"{svc}\" start= disabled");   // the space after start= is required syntax
                undoCmds.Add($"sc config \"{svc}\" start= auto");
                undoCmds.Add($"sc start \"{svc}\"");
            }
            foreach (var proc in a.Contender.Processes)
                doCmds.Add($"taskkill /f /im \"{proc}.exe\"");
        }
        doCmds.Add("exit /b 0");

        // Run the batch from a temp .cmd file rather than a long /c string: it sidesteps all of
        // cmd's nested-quote rules around the "service name" arguments.
        string tmp = Path.Combine(Path.GetTempPath(), "prisma-rgb-fix.cmd");
        bool exited;
        try
        {
            File.WriteAllText(tmp, string.Join("\r\n", doCmds) + "\r\n");
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{tmp}\"")
            {
                UseShellExecute = true,
                Verb = "runas",                       // one UAC prompt for the whole batch
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            exited = proc != null && proc.WaitForExit(20000);
        }
        catch (Exception ex)
        {
            try { File.Delete(tmp); } catch { }
            message = "Could not run the fix (elevation cancelled?): " + ex.Message;
            return false;
        }

        if (!exited)
        {
            // Timed out (a service stuck in STOP_PENDING) or never launched. Don't claim
            // success — the offenders may still be running or half-configured — and leave the
            // temp .cmd, which the elevated process may still be reading.
            message = "The fix didn't finish (still running, or elevation was declined). " +
                      "Re-scan to see what's still active.";
            return false;
        }
        try { File.Delete(tmp); } catch { }

        TryWriteRestoreScript(undoCmds);
        message = $"Disabled {list.Count} app{(list.Count == 1 ? "" : "s")}. " +
                  "A \"Restore-Prisma-RGB.cmd\" is on your Desktop to undo this.";
        return true;
    }

    private static void TryWriteRestoreScript(List<string> undoCmds)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                       "Restore-Prisma-RGB.cmd");
            var lines = new List<string>
            {
                "@echo off",
                "REM Re-enables the RGB software Prisma disabled to stop bus contention.",
                "REM Right-click this file -> Run as administrator.",
                ""
            };
            lines.AddRange(undoCmds);
            lines.Add("");
            lines.Add("echo Done. A reboot lets the services start cleanly.");
            lines.Add("pause");
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");
        }
        catch { /* Desktop write is a convenience, not required */ }
    }
}
