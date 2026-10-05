# Read-only probe: HidD_GetFeature on the UGREEN mic's COL03 feature report 0x9A.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class HidFeat
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
}
"@

$path = '\\?\hid#vid_2b89&pid_005d&mi_03&col03#8&50c213e&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}'
# GENERIC_READ|GENERIC_WRITE needed for feature I/O
$fh = [HidFeat]::CreateFile($path, [uint32]3221225472, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
if ($fh.IsInvalid) { throw ("open failed: {0}" -f [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()) }

$buf = New-Object byte[] 16
$buf[0] = 0x9A
$ok = [HidFeat]::HidD_GetFeature($fh, $buf, $buf.Length)
if ($ok) {
    Write-Output ("GetFeature 0x9A OK: " + (($buf | ForEach-Object { $_.ToString('X2') }) -join ' '))
} else {
    Write-Output ("GetFeature 0x9A failed: Win32 err {0}" -f [System.Runtime.InteropServices.Marshal]::GetLastWin32Error())
}
$fh.Close()