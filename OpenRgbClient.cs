using System.Net.Sockets;
using System.Text;

namespace RGBCommander;

public class OpenRgbDevice
{
    public int Index;
    public int Type;
    public string Name = "";
    public int LedCount;
    public int ActiveMode;
    public List<OpenRgbMode> Modes = new();
}

public class OpenRgbMode
{
    public int Index;
    public string Name = "";
    public uint Flags;
    public byte[] Raw = Array.Empty<byte>(); // serialized exactly as received; reusable for UpdateMode

    public const uint FlagPerLedColor = 1u << 5;
    public const uint FlagHasBrightness = 1u << 4;
}

/// <summary>
/// Minimal client for the OpenRGB SDK network protocol (TCP, default port 6742).
/// Implements protocol versions 0-4. All calls must come from a single thread.
/// </summary>
public class OpenRgbClient : IDisposable
{
    private const int HeaderSize = 16;
    private const uint PktRequestControllerCount = 0;
    private const uint PktRequestControllerData = 1;
    private const uint PktRequestProtocolVersion = 40;
    private const uint PktSetClientName = 50;
    private const uint PktDeviceListUpdated = 100;
    private const uint PktUpdateLeds = 1050;
    private const uint PktSetCustomMode = 1100;
    private const uint PktUpdateMode = 1101;

    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public uint ProtocolVersion { get; private set; } = 4;
    public bool Connected => _tcp is { Connected: true };
    public List<OpenRgbDevice> Devices { get; } = new();
    public bool DeviceListDirty { get; private set; }

    public static uint Color(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    public void Connect(string host = "127.0.0.1", int port = 6742, string clientName = "Prisma")
    {
        Disconnect();
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _tcp.NoDelay = true;
        // Reads happen on the UI thread (PollEvents, count probe) — a stalled server
        // must throw instead of hanging the app on a blocking Read forever.
        _tcp.ReceiveTimeout = 4000;
        _stream = _tcp.GetStream();

        SendPacket(0, PktRequestProtocolVersion, BitConverter.GetBytes((uint)4));
        var versionReply = ReadExpected(PktRequestProtocolVersion);
        ProtocolVersion = Math.Min(4u, BitConverter.ToUInt32(versionReply, 0));

        SendPacket(0, PktSetClientName, Encoding.ASCII.GetBytes(clientName + "\0"));
        RefreshDevices();
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
        Devices.Clear();
    }

    public void Dispose() => Disconnect();

    public void RefreshDevices()
    {
        Devices.Clear();
        SendPacket(0, PktRequestControllerCount);
        uint count = BitConverter.ToUInt32(ReadExpected(PktRequestControllerCount), 0);
        for (uint i = 0; i < count; i++)
        {
            byte[]? request = ProtocolVersion >= 1 ? BitConverter.GetBytes(ProtocolVersion) : null;
            SendPacket(i, PktRequestControllerData, request);
            Devices.Add(ParseDevice((int)i, ReadExpected(PktRequestControllerData)));
        }
        DeviceListDirty = false;
    }

    /// <summary>Re-fetches the controller list and compares it to the cached one. Only swaps
    /// the new list in (and returns true) if the set ACTUALLY changed — same count and same
    /// name/type/LED-count in order counts as unchanged, so a transient drop-and-re-add (a
    /// display wake or a non-RGB USB hot-plug that makes the server re-enumerate) leaves the
    /// cached list — and every existing device wrapper — untouched, and the caller can skip
    /// the disruptive rebuild that would re-Prepare every device and flash them. Clears the
    /// dirty flag either way.</summary>
    public bool RefreshIfChanged()
    {
        var fresh = new List<OpenRgbDevice>();
        SendPacket(0, PktRequestControllerCount);
        uint count = BitConverter.ToUInt32(ReadExpected(PktRequestControllerCount), 0);
        for (uint i = 0; i < count; i++)
        {
            byte[]? request = ProtocolVersion >= 1 ? BitConverter.GetBytes(ProtocolVersion) : null;
            SendPacket(i, PktRequestControllerData, request);
            fresh.Add(ParseDevice((int)i, ReadExpected(PktRequestControllerData)));
        }
        DeviceListDirty = false;

        bool changed = fresh.Count != Devices.Count;
        for (int i = 0; !changed && i < fresh.Count; i++)
            if (fresh[i].Name != Devices[i].Name || fresh[i].Type != Devices[i].Type ||
                fresh[i].LedCount != Devices[i].LedCount)
                changed = true;

        if (changed) { Devices.Clear(); Devices.AddRange(fresh); }
        return changed;
    }

    /// <summary>Processes unsolicited packets sitting in the receive buffer. The server
    /// pushes DeviceListUpdated when its detection adds a controller — e.g. the GPU
    /// appearing seconds after boot while OpenRGB is still scanning. Streaming only ever
    /// writes to the socket, so without this poll the notice would sit unread forever.</summary>
    public void PollEvents()
    {
        try
        {
            while (_tcp is { Connected: true } && _tcp.Available >= HeaderSize)
            {
                var header = ReadFully(HeaderSize);
                if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B')
                    throw new IOException("Bad packet magic from OpenRGB server");
                uint id = BitConverter.ToUInt32(header, 8);
                uint size = BitConverter.ToUInt32(header, 12);
                if (size > 0) ReadFully((int)size);
                if (id == PktDeviceListUpdated) DeviceListDirty = true;
            }
        }
        catch { /* connection trouble surfaces via Connected + the watchdog */ }
    }

    /// <summary>Drains pending pushes and clears the dirty flag. Call right after a full
    /// detection: the server pushes DeviceListUpdated at us for our own reconnect, and
    /// treating that echo as news caused an infinite rescan loop.</summary>
    public void ClearPendingEvents()
    {
        PollEvents();
        DeviceListDirty = false;
    }

    /// <summary>Asks the server how many controllers it currently has, without touching
    /// the cached list. Fallback for a missed DeviceListUpdated push. Null on failure.</summary>
    public int? TryGetControllerCount()
    {
        try
        {
            if (!Connected) return null;
            SendPacket(0, PktRequestControllerCount);
            return (int)BitConverter.ToUInt32(ReadExpected(PktRequestControllerCount), 0);
        }
        catch { return null; }
    }

    /// <summary>Puts the device into its custom (direct-control) mode.</summary>
    public void SetCustomMode(int deviceIndex) => SendPacket((uint)deviceIndex, PktSetCustomMode);

    /// <summary>Writes a mode to the device hardware. Required by controllers (e.g.
    /// Sapphire Nitro Glow) that otherwise stay in their onboard effect and ignore
    /// direct color updates.</summary>
    public void UpdateMode(int deviceIndex, OpenRgbMode mode)
    {
        var buf = new byte[4 + 4 + mode.Raw.Length];
        BitConverter.GetBytes((uint)buf.Length).CopyTo(buf, 0);
        BitConverter.GetBytes(mode.Index).CopyTo(buf, 4);
        mode.Raw.CopyTo(buf, 8);
        SendPacket((uint)deviceIndex, PktUpdateMode, buf);
    }

    /// <summary>Patches a mode's Raw blob so it requests full brightness. Some GPUs
    /// (Sapphire Nitro Glow) default an onboard/direct mode to a dim level, leaving the
    /// card noticeably darker than the rest. No-op when the protocol or mode lacks a
    /// brightness field. Idempotent — sets the current brightness to brightness_max.</summary>
    public void MaximizeBrightness(OpenRgbMode mode)
    {
        if (ProtocolVersion < 3) return;
        if ((mode.Flags & OpenRgbMode.FlagHasBrightness) == 0) return;
        var raw = mode.Raw;
        if (raw.Length < 2) return;
        ushort nameLen = BitConverter.ToUInt16(raw, 0);
        int afterName = 2 + nameLen;
        // layout after name: value(4) flags(4) speedMin(4) speedMax(4) brMin(4) brMax(4)
        // colMin(4) colMax(4) speed(4) brightness(4)
        int brMaxOff = afterName + 20;
        int brOff = afterName + 36;
        if (brOff + 4 > raw.Length) return;
        uint brMax = BitConverter.ToUInt32(raw, brMaxOff);
        BitConverter.GetBytes(brMax).CopyTo(raw, brOff);
    }

    public void UpdateLeds(int deviceIndex, uint[] colors)
    {
        var buf = new byte[4 + 2 + colors.Length * 4];
        BitConverter.GetBytes((uint)buf.Length).CopyTo(buf, 0);
        BitConverter.GetBytes((ushort)colors.Length).CopyTo(buf, 4);
        for (int i = 0; i < colors.Length; i++)
            BitConverter.GetBytes(colors[i]).CopyTo(buf, 6 + i * 4);
        SendPacket((uint)deviceIndex, PktUpdateLeds, buf);
    }

    private void SendPacket(uint deviceIndex, uint packetId, byte[]? data = null)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");
        data ??= Array.Empty<byte>();
        var header = new byte[HeaderSize];
        header[0] = (byte)'O'; header[1] = (byte)'R'; header[2] = (byte)'G'; header[3] = (byte)'B';
        BitConverter.GetBytes(deviceIndex).CopyTo(header, 4);
        BitConverter.GetBytes(packetId).CopyTo(header, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(header, 12);
        _stream.Write(header, 0, header.Length);
        if (data.Length > 0) _stream.Write(data, 0, data.Length);
    }

    private byte[] ReadExpected(uint packetId)
    {
        while (true)
        {
            var header = ReadFully(HeaderSize);
            if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B')
                throw new IOException("Bad packet magic from OpenRGB server");
            uint id = BitConverter.ToUInt32(header, 8);
            uint size = BitConverter.ToUInt32(header, 12);
            var payload = size > 0 ? ReadFully((int)size) : Array.Empty<byte>();
            if (id == packetId) return payload;
            if (id == PktDeviceListUpdated) DeviceListDirty = true;
            // any other unsolicited packet is ignored
        }
    }

    private byte[] ReadFully(int count)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");
        var buf = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = _stream.Read(buf, offset, count - offset);
            if (read <= 0) throw new IOException("Connection closed by OpenRGB server");
            offset += read;
        }
        return buf;
    }

    private OpenRgbDevice ParseDevice(int index, byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        string ReadStr()
        {
            ushort len = r.ReadUInt16();
            return Encoding.ASCII.GetString(r.ReadBytes(len)).TrimEnd('\0');
        }

        r.ReadUInt32(); // total data size
        var dev = new OpenRgbDevice { Index = index, Type = r.ReadInt32(), Name = ReadStr() };
        if (ProtocolVersion >= 1) ReadStr(); // vendor
        ReadStr(); // description
        ReadStr(); // fw version
        ReadStr(); // serial
        ReadStr(); // location

        ushort numModes = r.ReadUInt16();
        dev.ActiveMode = r.ReadInt32();
        for (int m = 0; m < numModes; m++)
        {
            int start = (int)r.BaseStream.Position;
            string modeName = ReadStr();                        // mode name
            r.ReadInt32();                                      // value
            uint modeFlags = r.ReadUInt32();                    // flags
            r.ReadUInt32(); r.ReadUInt32();                     // speed min/max
            if (ProtocolVersion >= 3) { r.ReadUInt32(); r.ReadUInt32(); } // brightness min/max
            r.ReadUInt32(); r.ReadUInt32();                     // colors min/max
            r.ReadUInt32();                                     // speed
            if (ProtocolVersion >= 3) r.ReadUInt32();           // brightness
            r.ReadUInt32();                                     // direction
            r.ReadUInt32();                                     // color mode
            ushort modeColors = r.ReadUInt16();
            for (int c = 0; c < modeColors; c++) r.ReadUInt32();
            int end = (int)r.BaseStream.Position;
            dev.Modes.Add(new OpenRgbMode
            {
                Index = m,
                Name = modeName,
                Flags = modeFlags,
                Raw = data[start..end]
            });
        }

        ushort numZones = r.ReadUInt16();
        for (int z = 0; z < numZones; z++)
        {
            ReadStr();                                          // zone name
            r.ReadInt32();                                      // zone type
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();     // leds min/max/count
            ushort matrixLen = r.ReadUInt16();
            if (matrixLen > 0) r.ReadBytes(matrixLen);
            if (ProtocolVersion >= 4)
            {
                ushort numSegments = r.ReadUInt16();
                for (int s = 0; s < numSegments; s++)
                {
                    ReadStr();                                  // segment name
                    r.ReadInt32();                              // segment type
                    r.ReadUInt32(); r.ReadUInt32();             // start index, led count
                }
            }
        }

        ushort numLeds = r.ReadUInt16();
        for (int l = 0; l < numLeds; l++) { ReadStr(); r.ReadUInt32(); }
        dev.LedCount = numLeds;
        return dev;
    }
}
