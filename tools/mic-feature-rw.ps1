# Low-risk test: SetFeature a recognizable pattern on COL03 report 0x9A, then GetFeature
# to see whether it is a readable/writable register (persistence => settable parameter).
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class FRW {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern SafeFileHandle CreateFile(string p, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
}
"@
$path = '\\?\hid#vid_2b89&pid_005d&mi_03&col03#8&50c213e&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$fh = [FRW]::CreateFile($path, [uint32]3221225472, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
if ($fh.IsInvalid) { throw ("open failed {0}" -f [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()) }

function ReadBack { $b = New-Object byte[] 16; $b[0]=0x9A; $ok=[FRW]::HidD_GetFeature($fh,$b,16); if($ok){(($b|%{$_.ToString('X2')}) -join ' ')}else{"GetFeature err "+[System.Runtime.InteropServices.Marshal]::GetLastWin32Error()} }

Write-Output ("baseline:   " + (ReadBack))
$set = New-Object byte[] 16
$set[0]=0x9A; $set[1]=0xA5; $set[2]=0x5A; $set[3]=0x12; $set[4]=0x34
$ok=[FRW]::HidD_SetFeature($fh,$set,16)
Write-Output ("SetFeature A5 5A 12 34 -> {0} (err {1})" -f $ok, [System.Runtime.InteropServices.Marshal]::GetLastWin32Error())
Start-Sleep -Milliseconds 200
Write-Output ("after write: " + (ReadBack))
$fh.Close()