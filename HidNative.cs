using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RGBCommander;

public class HidDeviceInfo
{
    public string Path = "";
    public ushort Vid, Pid, UsagePage, Usage;
    public int InputLen, OutputLen, FeatureLen;
}

/// <summary>Raw Win32 HID enumeration and report I/O (no external dependencies).</summary>
public static class HidNative
{
    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
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

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] internal static extern bool HidD_SetFeature(SafeFileHandle h, byte[] data, int len);
    [DllImport("hid.dll")] internal static extern bool HidD_GetFeature(SafeFileHandle h, byte[] data, int len);

    [DllImport("setupapi.dll")]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devs, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devs, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int required, IntPtr devInfoData);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devs);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    public static List<HidDeviceInfo> Enumerate()
    {
        var results = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out var hidGuid);
        IntPtr devs = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devs == new IntPtr(-1)) return results;
        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref hidGuid, i, ref ifaceData); i++)
            {
                SetupDiGetDeviceInterfaceDetail(devs, ref ifaceData, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devs, ref ifaceData, detail, required, out _, IntPtr.Zero)) continue;
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path == null) continue;
                    var info = Query(path);
                    if (info != null) results.Add(info);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devs); }
        return results;
    }

    private static HidDeviceInfo? Query(string path)
    {
        using var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;
        if (!HidD_GetPreparsedData(h, out IntPtr ppd)) return null;
        try
        {
            var caps = new HIDP_CAPS();
            HidP_GetCaps(ppd, ref caps);
            return new HidDeviceInfo
            {
                Path = path,
                Vid = attr.VendorID,
                Pid = attr.ProductID,
                UsagePage = caps.UsagePage,
                Usage = caps.Usage,
                InputLen = caps.InputReportByteLength,
                OutputLen = caps.OutputReportByteLength,
                FeatureLen = caps.FeatureReportByteLength
            };
        }
        finally { HidD_FreePreparsedData(ppd); }
    }

    internal static SafeFileHandle OpenForIO(string path)
    {
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"Cannot open HID device: {path}");
        return h;
    }
}

/// <summary>An open HID device handle supporting overlapped reads/writes and feature reports.</summary>
public sealed class HidStream : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly ManualResetEvent _readEvent = new(false);
    private readonly ManualResetEvent _writeEvent = new(false);

    public HidDeviceInfo Info { get; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal, InternalHigh;
        public uint OffsetLow, OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle h, byte[] buf, int n, out int written, ref OVERLAPPED o);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, out int read, ref OVERLAPPED o);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle h, ref OVERLAPPED o, out int transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle h, ref OVERLAPPED o);

    private const int ERROR_IO_PENDING = 997;

    public HidStream(HidDeviceInfo info)
    {
        Info = info;
        _handle = HidNative.OpenForIO(info.Path);
    }

    /// <summary>Writes an output report. Data is padded to the device's output report length.</summary>
    public void Write(byte[] report)
    {
        var buf = new byte[Info.OutputLen];
        Array.Copy(report, buf, Math.Min(report.Length, buf.Length));
        var o = new OVERLAPPED { hEvent = _writeEvent.SafeWaitHandle.DangerousGetHandle() };
        _writeEvent.Reset();
        if (!WriteFile(_handle, buf, buf.Length, out _, ref o))
        {
            if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                throw new IOException("HID write failed");
            if (!_writeEvent.WaitOne(1000))
            {
                CancelIoEx(_handle, ref o);
                GetOverlappedResult(_handle, ref o, out _, true);
                throw new TimeoutException("HID write timed out");
            }
            GetOverlappedResult(_handle, ref o, out _, false);
        }
    }

    /// <summary>Reads an input report. Returns null on timeout.</summary>
    public byte[]? Read(int timeoutMs)
    {
        var buf = new byte[Info.InputLen];
        var o = new OVERLAPPED { hEvent = _readEvent.SafeWaitHandle.DangerousGetHandle() };
        _readEvent.Reset();
        if (ReadFile(_handle, buf, buf.Length, out _, ref o)) return buf;
        if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
            throw new IOException("HID read failed");
        if (!_readEvent.WaitOne(timeoutMs))
        {
            CancelIoEx(_handle, ref o);
            GetOverlappedResult(_handle, ref o, out _, true);
            return null;
        }
        return GetOverlappedResult(_handle, ref o, out _, false) ? buf : null;
    }

    public void DrainInput()
    {
        while (Read(40) != null) { }
    }

    public bool SetFeature(byte[] report)
    {
        var buf = new byte[Info.FeatureLen];
        Array.Copy(report, buf, Math.Min(report.Length, buf.Length));
        return HidNative.HidD_SetFeature(_handle, buf, buf.Length);
    }

    public byte[]? GetFeature(byte reportId)
    {
        var buf = new byte[Info.FeatureLen];
        buf[0] = reportId;
        return HidNative.HidD_GetFeature(_handle, buf, buf.Length) ? buf : null;
    }

    public void Dispose()
    {
        _handle.Dispose();
        _readEvent.Dispose();
        _writeEvent.Dispose();
    }
}
