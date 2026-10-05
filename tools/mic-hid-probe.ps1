# Probe HID capabilities of the UGREEN MIC-CM581 (VID 2B89 PID 005D)
# Dumps usage page/usage + report lengths + value/button caps (with report IDs) per collection.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    // Shared 72-byte layout for HIDP_VALUE_CAPS / HIDP_BUTTON_CAPS (header fields match)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HIDP_GENERIC_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved1;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)] public byte[] Mid;
        public ushort UsageMin;
        public ushort UsageMax;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public byte[] Tail;
    }

    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll")] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA did);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA did, ref SP_DEVICE_INTERFACE_DETAIL_DATA detail, int detailSize, out int required, IntPtr devInfoData);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr ppd);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr ppd);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr ppd, out HIDP_CAPS caps);
    [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, [In, Out] HIDP_GENERIC_CAPS[] caps, ref ushort len, IntPtr ppd);
    [DllImport("hid.dll")] public static extern int HidP_GetButtonCaps(int reportType, [In, Out] HIDP_GENERIC_CAPS[] caps, ref ushort len, IntPtr ppd);
}
"@

function Dump-GenericCaps {
    param($ppd, [int]$reportType, [int]$count, [bool]$isButton, [string]$label)
    if ($count -le 0) { return }
    $arr = New-Object 'HidProbe+HIDP_GENERIC_CAPS[]' $count
    $len = [uint16]$count
    if ($isButton) { $status = [HidProbe]::HidP_GetButtonCaps($reportType, $arr, [ref]$len, $ppd) }
    else           { $status = [HidProbe]::HidP_GetValueCaps($reportType, $arr, [ref]$len, $ppd) }
    if ($status -ne 0x00110000) { Write-Output ("    {0}: HidP status 0x{1:X8}" -f $label, $status); return }
    for ($k = 0; $k -lt $len; $k++) {
        $c = $arr[$k]
        if ($c.IsRange) { $usage = ("usage 0x{0:X2}..0x{1:X2}" -f $c.UsageMin, $c.UsageMax) }
        else            { $usage = ("usage 0x{0:X2}" -f $c.UsageMin) }
        $extra = ""
        if (-not $isButton) { $extra = (" bitSize={0} reportCount={1}" -f $c.BitSize, $c.ReportCount) }
        Write-Output ("    {0}[{1}]: reportId=0x{2:X2} usagePage=0x{3:X4} {4}{5}" -f $label, $k, $c.ReportID, $c.UsagePage, $usage, $extra)
    }
}

$hidGuid = [Guid]::Empty
[HidProbe]::HidD_GetHidGuid([ref]$hidGuid)
$h = [HidProbe]::SetupDiGetClassDevs([ref]$hidGuid, [IntPtr]::Zero, [IntPtr]::Zero, 0x12) # PRESENT | DEVICEINTERFACE
if ($h -eq [IntPtr]::Zero -or $h -eq [IntPtr](-1)) { throw "SetupDiGetClassDevs failed" }

$i = 0
while ($true) {
    $did = New-Object 'HidProbe+SP_DEVICE_INTERFACE_DATA'
    $did.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'HidProbe+SP_DEVICE_INTERFACE_DATA')
    if (-not [HidProbe]::SetupDiEnumDeviceInterfaces($h, [IntPtr]::Zero, [ref]$hidGuid, $i, [ref]$did)) { break }
    $i++

    $detail = New-Object 'HidProbe+SP_DEVICE_INTERFACE_DETAIL_DATA'
    $detail.cbSize = 8  # x64
    $req = 0
    $detailSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'HidProbe+SP_DEVICE_INTERFACE_DETAIL_DATA')
    if (-not [HidProbe]::SetupDiGetDeviceInterfaceDetail($h, [ref]$did, [ref]$detail, $detailSize, [ref]$req, [IntPtr]::Zero)) { continue }
    $path = $detail.DevicePath
    if ($path -notmatch 'vid_2b89') { continue }

    Write-Output ""
    Write-Output ("PATH: {0}" -f $path)
    $fh = [HidProbe]::CreateFile($path, 0, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
    if ($fh.IsInvalid) { Write-Output "  (cannot open)"; continue }

    $attr = New-Object 'HidProbe+HIDD_ATTRIBUTES'
    $attr.Size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'HidProbe+HIDD_ATTRIBUTES')
    [void][HidProbe]::HidD_GetAttributes($fh, [ref]$attr)
    Write-Output ("  VID=0x{0:X4} PID=0x{1:X4} ver=0x{2:X4}" -f $attr.VendorID, $attr.ProductID, $attr.VersionNumber)

    $ppd = [IntPtr]::Zero
    if (-not [HidProbe]::HidD_GetPreparsedData($fh, [ref]$ppd)) { Write-Output "  (no preparsed data)"; $fh.Close(); continue }
    $caps = New-Object 'HidProbe+HIDP_CAPS'
    $status = [HidProbe]::HidP_GetCaps($ppd, [ref]$caps)
    if ($status -eq 0x00110000) {
        Write-Output ("  TLC: usagePage=0x{0:X4} usage=0x{1:X4}" -f $caps.UsagePage, $caps.Usage)
        Write-Output ("  ReportByteLen: input={0} output={1} feature={2}" -f $caps.InputReportByteLength, $caps.OutputReportByteLength, $caps.FeatureReportByteLength)
        Write-Output ("  Caps counts: inBtn={0} inVal={1} outBtn={2} outVal={3} featBtn={4} featVal={5}" -f $caps.NumberInputButtonCaps, $caps.NumberInputValueCaps, $caps.NumberOutputButtonCaps, $caps.NumberOutputValueCaps, $caps.NumberFeatureButtonCaps, $caps.NumberFeatureValueCaps)
        Dump-GenericCaps $ppd 0 $caps.NumberInputButtonCaps   $true  "inputButton"
        Dump-GenericCaps $ppd 0 $caps.NumberInputValueCaps    $false "inputValue"
        Dump-GenericCaps $ppd 1 $caps.NumberOutputButtonCaps  $true  "outputButton"
        Dump-GenericCaps $ppd 1 $caps.NumberOutputValueCaps   $false "outputValue"
        Dump-GenericCaps $ppd 2 $caps.NumberFeatureButtonCaps $true  "featureButton"
        Dump-GenericCaps $ppd 2 $caps.NumberFeatureValueCaps  $false "featureValue"
    } else {
        Write-Output ("  HidP_GetCaps failed: 0x{0:X8}" -f $status)
    }
    [void][HidProbe]::HidD_FreePreparsedData($ppd)
    $fh.Close()
}
[void][HidProbe]::SetupDiDestroyDeviceInfoList($h)
Write-Output ""
Write-Output "Done."