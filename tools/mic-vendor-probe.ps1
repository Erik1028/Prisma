# Vendor command-channel probe for UGREEN mic COL04 (usagePage 0xFF01).
# Opens a read handle, sends benign query commands on output report 0x50,
# and listens for any input report 0x10/0x11 response. Request/response only -
# no color writes, no bulk (0x51) channel. Goal: learn if the device answers.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class Vendor
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);

    public static List<string> Probe(string path, List<byte[]> commands)
    {
        var log = new List<string>();
        // read handle (overlapped) + write handle (sync)
        SafeFileHandle rh = CreateFile(path, 0x80000000u, 3, IntPtr.Zero, 3, 0x40000000u, IntPtr.Zero);
        SafeFileHandle wh = CreateFile(path, 0x40000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (rh.IsInvalid || wh.IsInvalid) { log.Add("open failed err=" + Marshal.GetLastWin32Error()); return log; }
        FileStream fs = new FileStream(rh, FileAccess.Read, 64, true);

        var responses = new List<string>();
        object gate = new object();
        bool stop = false;
        Thread reader = new Thread(delegate () {
            byte[] buf = new byte[64];
            while (!stop) {
                var task = fs.ReadAsync(buf, 0, 64);
                try { if (!task.Wait(400)) continue; } catch { break; }
                int n = task.Result;
                if (n > 0) lock (gate) { responses.Add(BitConverter.ToString(buf, 0, n)); }
            }
        });
        reader.IsBackground = true;
        reader.Start();

        foreach (byte[] cmd in commands) {
            lock (gate) { responses.Clear(); }
            byte[] rep = new byte[64];
            Array.Copy(cmd, rep, Math.Min(cmd.Length, 64));
            bool ok = HidD_SetOutputReport(wh, rep, rep.Length);
            Thread.Sleep(350);
            string got;
            lock (gate) { got = responses.Count == 0 ? "(no response)" : string.Join(" || ", responses); }
            log.Add(string.Format("TX {0} -> setOut={1} | RX {2}",
                BitConverter.ToString(cmd, 0, Math.Min(cmd.Length, 8)), ok, got));
        }
        stop = true;
        Thread.Sleep(500);
        try { fs.Dispose(); } catch {}
        try { wh.Close(); } catch {}
        return log;
    }
}
"@

$path = '\\?\hid#vid_2b89&pid_005d&mi_03&col04#8&50c213e&0&0003#{4d1e55b2-f16f-11cf-88cb-001111000030}'

# Benign query candidates: report 0x50, then a command byte. These are guesses at
# "get status / get version / ping" - all harmless reads, nothing that sets state.
$cmds = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($c in 0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x10,0x20,0x80,0x81,0x82,0xA0,0xF0,0xFF) {
    $b = New-Object byte[] 64
    $b[0] = 0x50    # report ID
    $b[1] = [byte]$c
    $cmds.Add($b)
}

Write-Output "Probing COL04 command channel (report 0x50) for query responses..."
$result = [Vendor]::Probe($path, $cmds)
$result | ForEach-Object { Write-Output $_ }
Write-Output "Done."