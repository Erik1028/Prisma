# Read the 8-byte feature report from the G413 TKL SE vendor collection (MI_02, page 0xFF01).
# Read-only: HidD_GetFeature only, no writes.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class KbFeat
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
}
"@

$path = '\\?\hid#vid_046d&pid_c34a&mi_02#8&31b164ca&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
# GENERIC_READ|GENERIC_WRITE, share rw, OPEN_EXISTING
$fh = [KbFeat]::CreateFile($path, [uint32]3221225472, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)  # GENERIC_READ|GENERIC_WRITE
if ($fh.IsInvalid) { throw "Cannot open $path (err $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))" }

$buf = New-Object byte[] 9   # report ID byte + 8 data bytes
$buf[0] = 0x00
if ([KbFeat]::HidD_GetFeature($fh, $buf, $buf.Length)) {
    Write-Output ("Feature report: " + (($buf | ForEach-Object { $_.ToString('X2') }) -join ' '))
} else {
    Write-Output ("HidD_GetFeature failed (err $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))")
}
$fh.Close()
