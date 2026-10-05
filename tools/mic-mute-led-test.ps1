# Safe test: blink the standard HID telephony Mute LED (LED page 0x08, usage 0x09)
# on the UGREEN mic's COL02 output report 0x04. Standard, reversible host control.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class HidLed
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr ppd);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr ppd);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] public static extern int HidP_SetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort[] usageList, ref uint usageLength, IntPtr ppd, byte[] report, uint reportLength);
}
"@

$path = '\\?\hid#vid_2b89&pid_005d&mi_03&col02#8&50c213e&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$fh = [HidLed]::CreateFile($path, [uint32]3221225472, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
if ($fh.IsInvalid) { throw ("open failed: {0}" -f [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
$ppd = [IntPtr]::Zero
if (-not [HidLed]::HidD_GetPreparsedData($fh, [ref]$ppd)) { throw "no preparsed data" }

# ON report: report ID 0x04 with LED-page Mute (0x09) usage set via HidP_SetUsages
$on = New-Object byte[] 16
$on[0] = 0x04
$usages = [uint16[]]@(0x09)
$ulen = [uint32]1
$status = [HidLed]::HidP_SetUsages(1, 0x08, 0, $usages, [ref]$ulen, $ppd, $on, [uint32]$on.Length)
if ($status -ne 0x00110000) { throw ("HidP_SetUsages failed: 0x{0:X8}" -f $status) }
Write-Output ("ON  report bytes: " + (($on | ForEach-Object { $_.ToString('X2') }) -join ' '))

# OFF report: report ID 0x04, all LED bits clear
$off = New-Object byte[] 16
$off[0] = 0x04

for ($i = 1; $i -le 3; $i++) {
    $okOn = [HidLed]::HidD_SetOutputReport($fh, $on, $on.Length)
    Write-Output ("blink {0}: mute LED ON  -> {1}" -f $i, $okOn)
    Start-Sleep -Milliseconds 1500
    $okOff = [HidLed]::HidD_SetOutputReport($fh, $off, $off.Length)
    Write-Output ("blink {0}: mute LED OFF -> {1}" -f $i, $okOff)
    Start-Sleep -Milliseconds 1500
}

[void][HidLed]::HidD_FreePreparsedData($ppd)
$fh.Close()
Write-Output "Done - ended with LED cleared."