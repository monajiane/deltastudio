using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Modbus;
using DeltaStudio.Protocols.Transport;

namespace DeltaStudio.Protocols.Mock;

/// <summary>Mock configuration.</summary>
public sealed record MockPlcOptions
{
    /// <summary>Address mapping used to translate Modbus locations to devices (required for device-level semantics).</summary>
    public IModbusDeviceMap? Map { get; init; }

    /// <summary>Station number answered.</summary>
    public byte Station { get; init; } = 1;

    /// <summary>Model code returned by FC07 flow-control identify.</summary>
    public ushort ModelCode { get; init; } = 0x0C32;

    /// <summary>Firmware version returned by FC07.</summary>
    public ushort Version { get; init; } = 0x1003;

    /// <summary>Use ASCII framing instead of RTU.</summary>
    public bool UseAscii { get; init; }

    /// <summary>Per-area bounds; requests touching addresses above the bound answer exception 02.</summary>
    public IReadOnlyDictionary<ModbusArea, int> AreaBounds { get; init; } =
        new Dictionary<ModbusArea, int>
        {
            [ModbusArea.Coils] = 8192,
            [ModbusArea.DiscreteInputs] = 4096,
            [ModbusArea.HoldingRegisters] = 16384,
            [ModbusArea.InputRegisters] = 8192,
        };
}

/// <summary>
/// Hardware-free emulator of a DVP-class MODBUS slave. It is a *mock*, presented as such:
/// it emulates device memory and Modbus responses (X, Y, M, S, L, T/C contacts, T/C present values, D
/// words via the configured map) — it does NOT model scan-cycle program execution.
/// Use it for integration tests and offline demos, never as proof of PLC behaviour.
/// </summary>
public sealed class MockDvpPlc
{
    private readonly MockPlcOptions _options;
    private readonly Dictionary<DeviceAddress, bool> _bits = new();
    private readonly Dictionary<DeviceAddress, ushort> _words = new();
    private readonly Random _rng = new(1234);

    /// <summary>Creates the emulator.</summary>
    public MockDvpPlc(MockPlcOptions options) => _options = options;

    /// <summary>Count of received valid requests.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Sets a device value directly (test convenience).</summary>
    public void SetDevice(DeviceAddress device, DeviceValue value)
    {
        if (value.IsWord)
        {
            _words[device] = value.Word;
        }
        else
        {
            _bits[device] = value.Bit;
        }
    }

    /// <summary>Reads a device value directly.</summary>
    public DeviceValue GetDevice(DeviceAddress device, bool asWord = false) =>
        asWord
            ? DeviceValue.FromWord(_words.TryGetValue(device, out ushort w) ? w : (ushort)0)
            : DeviceValue.FromBit(_bits.TryGetValue(device, out bool b) && b);

    /// <summary>Randomizes a few registers (monitoring demos without hardware).</summary>
    public void ApplyNoise(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var d = new DeviceAddress(DeviceKind.D, _rng.Next(0, 500));
            _words[d] = (ushort)_rng.Next(ushort.MaxValue);
        }
    }

    /// <summary>Handles one complete RTU/ASCII frame and produces the response frame.</summary>
    public byte[]? HandleFrame(byte[] frame)
    {
        byte[] adu;
        if (_options.UseAscii)
        {
            adu = ModbusAsciiCodec.Decode(frame) ?? Array.Empty<byte>();
        }
        else
        {
            if (!ModbusCrc.Validate(frame))
            {
                return null; // corrupt → no reply (RTU behaviour)
            }

            adu = frame[..^2];
        }

        if (adu.Length < 2 || adu[0] != _options.Station)
        {
            return null; // not addressed to us
        }

        RequestCount++;
        byte fc = adu[1];
        byte[]? response = fc switch
        {
            0x01 => ReadBits(adu, ModbusArea.Coils),
            0x02 => ReadBits(adu, ModbusArea.DiscreteInputs),
            0x03 => ReadWords(adu, ModbusArea.HoldingRegisters),
            0x04 => ReadWords(adu, ModbusArea.InputRegisters),
            0x05 => WriteSingleBit(adu),
            0x06 => WriteSingleWord(adu),
            0x0F => WriteMultipleBits(adu),
            0x10 => WriteMultipleWords(adu),
            0x07 => FlowControl(adu),
            0x08 => Loopback(adu),
            _ => ExceptionPdu(fc, 0x01),
        };

        if (response is null)
        {
            return null;
        }

        byte[] responseAdu = new byte[response.Length + 1];
        responseAdu[0] = _options.Station;
        response.CopyTo(responseAdu, 1);
        return _options.UseAscii
            ? ModbusAsciiCodec.Encode(responseAdu)
            : ModbusCrc.Append(responseAdu);
    }

    private bool BitAt(ModbusArea area, int addr, bool defaultValue)
    {
        if (_options.Map is not null && _options.Map.TryResolveBit(area, addr, out DeviceAddress dev))
        {
            return _bits.TryGetValue(dev, out bool v) ? v : defaultValue;
        }

        return defaultValue;
    }

    private ushort WordAt(ModbusArea area, int addr)
    {
        if (_options.Map is not null && _options.Map.TryResolveWord(area, addr, out DeviceAddress dev))
        {
            return _words.TryGetValue(dev, out ushort v) ? v : (ushort)0;
        }

        return 0;
    }

    private bool InBounds(ModbusArea area, int start, int count) =>
        start >= 0 && count > 0 && start + count <= _options.AreaBounds.GetValueOrDefault(area, 0);

    private byte[]? ReadBits(byte[] adu, ModbusArea area)
    {
        int start = (adu[2] << 8) | adu[3];
        int count = (adu[4] << 8) | adu[5];
        if (!InBounds(area, start, count))
        {
            return ExceptionPdu(0x01, 0x02);
        }

        // Physical X inputs default ON when not mapped (so monitoring demos show live data).
        bool xDefault = area == ModbusArea.DiscreteInputs;
        int bytes = (count + 7) / 8;
        var resp = new byte[2 + bytes];
        resp[0] = 0x01;
        resp[1] = (byte)bytes;
        for (int i = 0; i < count; i++)
        {
            if (BitAt(area, start + i, xDefault))
            {
                resp[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return resp;
    }

    private byte[]? ReadWords(byte[] adu, ModbusArea area)
    {
        int start = (adu[2] << 8) | adu[3];
        int count = (adu[4] << 8) | adu[5];
        if (!InBounds(area, start, count))
        {
            return ExceptionPdu(adu[1], 0x02);
        }

        var resp = new byte[2 + count * 2];
        resp[0] = adu[1];
        resp[1] = (byte)(count * 2);
        for (int i = 0; i < count; i++)
        {
            ushort v = WordAt(area, start + i);
            resp[2 + (i * 2)] = (byte)(v >> 8);
            resp[3 + (i * 2)] = (byte)v;
        }

        return resp;
    }

    private byte[]? WriteSingleBit(byte[] adu)
    {
        int addr = (adu[2] << 8) | adu[3];
        bool value = adu[4] == 0xFF;
        if (!InBounds(ModbusArea.Coils, addr, 1))
        {
            return ExceptionPdu(0x05, 0x02);
        }

        DeviceAddress dev = new(DeviceKind.M, addr); // raw-mode fallback: unmapped coils behave like M
        if (_options.Map is not null && !_options.Map.TryResolveBit(ModbusArea.Coils, addr, out dev))
        {
            dev = new DeviceAddress(DeviceKind.M, addr);
        }

        _bits[dev] = value;
        byte[] resp = [0x05, adu[2], adu[3], adu[4], (byte)(value ? 0xFF : 0x00)];
        return resp;
    }

    private byte[]? WriteSingleWord(byte[] adu)
    {
        int addr = (adu[2] << 8) | adu[3];
        ushort value = (ushort)((adu[4] << 8) | adu[5]);
        if (!InBounds(ModbusArea.HoldingRegisters, addr, 1))
        {
            return ExceptionPdu(0x06, 0x02);
        }

        DeviceAddress dev = new(DeviceKind.D, addr); // raw-mode fallback
        if (_options.Map is not null && !_options.Map.TryResolveWord(ModbusArea.HoldingRegisters, addr, out dev))
        {
            dev = new DeviceAddress(DeviceKind.D, addr);
        }

        _words[dev] = value;
        byte[] resp = [0x06, adu[2], adu[3], adu[4], adu[5]];
        return resp;
    }

    private byte[]? WriteMultipleBits(byte[] adu)
    {
        int start = (adu[2] << 8) | adu[3];
        int count = (adu[4] << 8) | adu[5];
        if (!InBounds(ModbusArea.Coils, start, count))
        {
            return ExceptionPdu(0x0F, 0x02);
        }

        for (int i = 0; i < count; i++)
        {
            bool v = (adu[7 + (i / 8)] & (1 << (i % 8))) != 0;
            DeviceAddress dev = new(DeviceKind.M, start + i);
            if (_options.Map is not null)
            {
                _options.Map.TryResolveBit(ModbusArea.Coils, start + i, out dev);
            }

            _bits[dev] = v;
        }

        byte[] resp = [0x0F, adu[2], adu[3], adu[4], adu[5]];
        return resp;
    }

    private byte[]? WriteMultipleWords(byte[] adu)
    {
        int start = (adu[2] << 8) | adu[3];
        int count = (adu[4] << 8) | adu[5];
        if (!InBounds(ModbusArea.HoldingRegisters, start, count))
        {
            return ExceptionPdu(0x10, 0x02);
        }

        for (int i = 0; i < count; i++)
        {
            ushort v = (ushort)((adu[7 + (i * 2)] << 8) | adu[8 + (i * 2)]);
            DeviceAddress dev = new(DeviceKind.D, start + i);
            if (_options.Map is not null)
            {
                _options.Map.TryResolveWord(ModbusArea.HoldingRegisters, start + i, out dev);
            }

            _words[dev] = v;
        }

        byte[] resp = [0x10, adu[2], adu[3], adu[4], adu[5]];
        return resp;
    }

    private byte[] FlowControl(byte[] adu)
    {
        // [07][08][modelHi][modelLo][verHi][verLo][reqCnt][0][maxCnt][0] — layout per Delta MODBUS guides (unverified).
        int requested = (adu[2] << 8) | adu[3];
        return
        [
            0x07, 0x0A,
            (byte)(_options.ModelCode >> 8), (byte)_options.ModelCode,
            (byte)(_options.Version >> 8), (byte)_options.Version,
            (byte)(requested >> 8), (byte)requested,
            0x00, 0x0A,
        ];
    }

    private byte[]? Loopback(byte[] adu)
    {
        int bc = adu[4];
        var resp = new byte[4 + bc];
        resp[0] = 0x08;
        resp[1] = 0x00;
        resp[2] = 0x00;
        resp[3] = (byte)bc;
        Array.Copy(adu, 5, resp, 4, bc);
        return resp;
    }

    private static byte[] ExceptionPdu(byte fc, byte code) => [(byte)(fc | 0x80), code];
}

/// <summary>
/// Transport pair for the mock: frames written by the client are answered synchronously by a
/// MockDvpPlc instance. This is what the unit/integration tests run the real Modbus stack on.
/// </summary>
public sealed class MockPlcTransport : IPlcTransport
{
    private readonly MockDvpPlc _plc;
    private byte[]? _pending;
    private bool _connected;

    /// <summary>Latency injected per round trip (ms) — exercises timeouts on demand.</summary>
    public int SimulatedLatencyMs { get; set; }

    /// <summary>When true, the next request is dropped (timeout simulation).</summary>
    public bool DropNextResponse { get; set; }

    /// <summary>Creates the transport over the emulator.</summary>
    public MockPlcTransport(MockDvpPlc plc) => _plc = plc;

    /// <inheritdoc/>
    public bool IsConnected => _connected;

    /// <inheritdoc/>
    public string Description => "mock://dvp";

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (!_connected)
        {
            throw new TransportException("Mock transport is not connected.");
        }

        _pending = frame.ToArray();
        if (SimulatedLatencyMs > 0)
        {
            await Task.Delay(SimulatedLatencyMs, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<byte[]?> ReadFrameAsync(CancellationToken ct = default)
    {
        if (_pending is null)
        {
            return Task.FromResult<byte[]?>(null);
        }

        byte[]? response = DropNextResponse ? null : _plc.HandleFrame(_pending);
        DropNextResponse = false;
        _pending = null;
        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}

// NOTE on exception encoding: ExceptionPdu returns [0x80 placeholder, fc|0x80, code]; the caller
// prefixes the station byte, so responses are [station][fc|0x80][code] as the spec requires.
