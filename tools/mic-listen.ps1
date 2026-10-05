# Passive listener: capture input reports from all 4 HID collections of the UGREEN mic.
# Run while operating the mic's physical controls (mode key, knob, mute touch).
# Usage: mic-listen.ps1 [-Seconds 35]
param([int]$Seconds = 35)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidListener
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    static void Listen(string label, string path, int reportLen, int seconds)
    {
        // GENERIC_READ, share rw, OPEN_EXISTING, FILE_FLAG_OVERLAPPED
        SafeFileHandle h = CreateFile(path, 0x80000000u, 3, IntPtr.Zero, 3, 0x40000000u, IntPtr.Zero);
        if (h.IsInvalid) { Console.WriteLine(label + ": open failed err=" + Marshal.GetLastWin32Error()); return; }
        FileStream fs = new FileStream(h, FileAccess.Read, reportLen, true);
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        byte[] buf = new byte[reportLen];
        while (DateTime.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var task = fs.ReadAsync(buf, 0, reportLen);
            try { if (!task.Wait(remaining)) break; } catch (AggregateException) { break; }
            int n = task.Result;
            if (n > 0) Console.WriteLine(string.Format("{0:HH:mm:ss.fff} {1}: {2}", DateTime.Now, label, BitConverter.ToString(buf, 0, n)));
        }
    }

    public static void ListenMany(string[] labels, string[] paths, int[] lens, int seconds)
    {
        Thread[] threads = new Thread[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            threads[i] = new Thread(delegate () { Listen(labels[idx], paths[idx], lens[idx], seconds); });
            threads[i].IsBackground = true;
            threads[i].Start();
        }
        foreach (Thread t in threads) t.Join();
    }
}
"@

$base = '\\?\hid#vid_2b89&pid_005d&mi_03&'
$suffix = '#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$labels = @('COL01-consumer', 'COL02-telephony', 'COL03-ff99', 'COL04-ff01')
$paths = @(
    ($base + 'col01#8&50c213e&0&0000' + $suffix),
    ($base + 'col02#8&50c213e&0&0001' + $suffix),
    ($base + 'col03#8&50c213e&0&0002' + $suffix),
    ($base + 'col04#8&50c213e&0&0003' + $suffix)
)
$lens = @(2, 16, 2, 64)

Write-Output ("Listening on 4 collections for {0} seconds - operate the mic controls NOW..." -f $Seconds)
[HidListener]::ListenMany($labels, $paths, $lens, $Seconds)
Write-Output "Capture window closed."