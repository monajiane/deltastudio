using DeltaStudio.Core.Common;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Transport;

namespace DeltaStudio.Protocols.Modbus;

/// <summary>Modbus exception (exception response PDU).</summary>
public sealed class ModbusException : IOException
{
    /// <summary>Creates with code.</summary>
    public ModbusException(byte function, byte code)
        : base($"MODBUS exception: FC {function:X02}, code {code:X02} ({Decode(code)})")
    {
        Function = function;
        Code = code;
    }

    /// <summary>Function that failed.</summary>
    public byte Function { get; }

    /// <summary>Exception code.</summary>
    public byte Code { get; }

    private static string Decode(byte code) => code switch
    {
        1 => "ILLEGAL FUNCTION",
        2 => "ILLEGAL DATA ADDRESS",
        3 => "ILLEGAL DATA VALUE",
        4 => "SLAVE DEVICE FAILURE",
        _ => "UNKNOWN",
    };
}

/// <summary>
/// Standard MODBUS master over any IPlcTransport. Fully functional and unit-tested against the
/// mock slave; the *device→address mapping* is what Delta adds (see DeltaStudio.Delta).
/// </summary>
public sealed class ModbusClient
{
    private readonly IPlcTransport _transport;
    private readonly bool _ascii;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Slave station number.</summary>
    public byte Station { get; set; } = 1;

    /// <summary>Creates a client. ascii=true for Modbus ASCII framing, false for RTU.</summary>
    public ModbusClient(IPlcTransport transport, bool ascii = false)
    {
        _transport = transport;
        _ascii = ascii;
    }

    /// <summary>Transport availability for UI status.</summary>
    public ProtocolStatus Status => _transport.IsConnected ? ProtocolStatus.Supported : ProtocolStatus.PartiallySupported;

    /// <summary>FC01.</summary>
    public Task<bool[]> ReadCoilsAsync(ushort start, ushort count, CancellationToken ct = default) =>
        ReadBitsAsync(0x01, start, count, ct);

    /// <summary>FC02.</summary>
    public Task<bool[]> ReadDiscreteInputsAsync(ushort start, ushort count, CancellationToken ct = default) =>
        ReadBitsAsync(0x02, start, count, ct);

    /// <summary>FC03.</summary>
    public Task<ushort[]> ReadHoldingRegistersAsync(ushort start, ushort count, CancellationToken ct = default) =>
        ReadWordsAsync(0x03, start, count, ct);

    /// <summary>FC04.</summary>
    public Task<ushort[]> ReadInputRegistersAsync(ushort start, ushort count, CancellationToken ct = default) =>
        ReadWordsAsync(0x04, start, count, ct);

    /// <summary>FC05 (0xFF00 ON / 0x0000 OFF).</summary>
    public async Task WriteCoilAsync(ushort address, bool value, CancellationToken ct = default)
    {
        byte[] response = await TransactAsync(
            BuildPayload(0x05, (byte)(address >> 8), (byte)address, (byte)(value ? 0xFF : 0x00), 0x00), ct).ConfigureAwait(false);
        ExpectEcho(response, 0x05, 5);
    }

    /// <summary>FC06.</summary>
    public async Task WriteHoldingRegisterAsync(ushort address, ushort value, CancellationToken ct = default)
    {
        byte[] response = await TransactAsync(
            BuildPayload(0x06, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value), ct).ConfigureAwait(false);
        ExpectEcho(response, 0x06, 5);
    }

    /// <summary>FC15.</summary>
    public async Task WriteCoilsAsync(ushort start, IReadOnlyList<bool> values, CancellationToken ct = default)
    {
        int byteCount = (values.Count + 7) / 8;
        var pdu = new List<byte> { 0x0F, (byte)(start >> 8), (byte)start, (byte)(values.Count >> 8), (byte)values.Count, (byte)byteCount };
        byte acc = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                acc |= (byte)(1 << (i % 8));
            }

            if (i % 8 == 7 || i == values.Count - 1)
            {
                pdu.Add(acc);
                acc = 0;
            }
        }

        byte[] response = await TransactAsync(BuildPayload(pdu.ToArray()), ct).ConfigureAwait(false);
        ExpectEcho(response, 0x0F, 5);
    }

    /// <summary>FC16.</summary>
    public async Task WriteHoldingRegistersAsync(ushort start, IReadOnlyList<ushort> values, CancellationToken ct = default)
    {
        var pdu = new List<byte> { 0x10, (byte)(start >> 8), (byte)start, (byte)(values.Count >> 8), (byte)values.Count, (byte)(values.Count * 2) };
        foreach (ushort v in values)
        {
            pdu.Add((byte)(v >> 8));
            pdu.Add((byte)v);
        }

        byte[] response = await TransactAsync(BuildPayload(pdu.ToArray()), ct).ConfigureAwait(false);
        ExpectEcho(response, 0x10, 5);
    }

    /// <summary>
    /// Delta "Flow Control" FC07: on DVP MODBUS slaves the response reports station,
    /// model code and firmware version. This is the identification mechanism documented in
    /// Delta's MODBUS application guides; treat values as raw until verified on hardware.
    /// </summary>
    public async Task<(byte Station, ushort ModelCode, ushort Version)?> FlowControlIdentifyAsync(ushort count, CancellationToken ct = default)
    {
        byte[] response = await TransactAsync(
            BuildPayload(0x07, (byte)(count >> 8), (byte)count), ct).ConfigureAwait(false);
        if (response.Length < 8 || response[1] != 0x07)
        {
            return null;
        }

        // [station][07][byteCount][modelHi][modelLo][verHi][verLo][...] — payload layout per Delta docs.
        return (response[0],
            (ushort)((response[3] << 8) | response[4]),
            (ushort)((response[5] << 8) | response[6]));
    }

    /// <summary>FC08 diagnostics (sub 1 loopback).</summary>
    public async Task<byte[]?> DiagnosticLoopbackAsync(byte[] data, CancellationToken ct = default)
    {
        var pdu = new List<byte> { 0x08, 0x00, 0x00, (byte)(data.Length >> 8), (byte)data.Length };
        pdu.AddRange(data);
        byte[] response = await TransactAsync(BuildPayload(pdu.ToArray()), ct).ConfigureAwait(false);
        if (response.Length < 6 || response[1] != 0x08)
        {
            return null;
        }

        int bc = response[4];
        return response[5..(5 + bc)];
    }

    private async Task<bool[]> ReadBitsAsync(byte fc, ushort start, ushort count, CancellationToken ct)
    {
        byte[] response = await TransactAsync(BuildPayload(fc, (byte)(start >> 8), (byte)start, (byte)(count >> 8), (byte)count), ct).ConfigureAwait(false);
        if (response.Length < 3 || response[1] != fc)
        {
            throw new IOException($"Unexpected MODBUS response for FC{fc:X02}.");
        }

        int bc = response[2];
        var bits = new bool[count];
        for (int i = 0; i < count; i++)
        {
            bits[i] = (response[3 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return bits;
    }

    private async Task<ushort[]> ReadWordsAsync(byte fc, ushort start, ushort count, CancellationToken ct)
    {
        byte[] response = await TransactAsync(BuildPayload(fc, (byte)(start >> 8), (byte)start, (byte)(count >> 8), (byte)count), ct).ConfigureAwait(false);
        if (response.Length < 3 || response[1] != fc)
        {
            throw new IOException($"Unexpected MODBUS response for FC{fc:X02}.");
        }

        var words = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            words[i] = (ushort)((response[3 + (i * 2)] << 8) | response[4 + (i * 2)]);
        }

        return words;
    }

    private static void ExpectEcho(byte[] response, byte fc, int length)
    {
        if (response.Length < length || response[1] != fc)
        {
            throw new IOException($"Unexpected MODBUS response for FC{fc:X02}.");
        }
    }

    private static byte[] BuildPayload(params byte[] pdu)
    {
        byte[] adu = new byte[pdu.Length + 1];
        adu[0] = 0; // station filled in TransactAsync (kept out so ASCII/RTU share one path)
        pdu.CopyTo(adu, 1);
        return adu;
    }

    private async Task<byte[]> TransactAsync(byte[] aduWithStationPlaceholder, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[] adu = (byte[])aduWithStationPlaceholder.Clone();
            adu[0] = Station;

            byte[] frame = _ascii ? WithCrLf(ModbusAsciiCodec.Encode(adu)) : ModbusCrc.Append(adu);
            await _transport.WriteAsync(frame, ct).ConfigureAwait(false);

            byte[]? raw = await _transport.ReadFrameAsync(ct).ConfigureAwait(false)
                ?? throw new TransportTimeoutException($"No MODBUS response within timeout ({_transport.Description}).");

            byte[] responseAdu;
            if (_ascii)
            {
                responseAdu = ModbusAsciiCodec.Decode(raw) ?? throw new IOException("Bad Modbus ASCII frame from PLC.");
            }
            else
            {
                if (!ModbusCrc.Validate(raw))
                {
                    throw new IOException("Modbus CRC check failed.");
                }

                responseAdu = raw[..^2];
            }

            if (responseAdu.Length < 2)
            {
                throw new IOException("Short MODBUS response.");
            }

            if (responseAdu[1] >= 0x80)
            {
                throw new ModbusException((byte)(responseAdu[1] & 0x7F), responseAdu.Length > 2 ? responseAdu[2] : (byte)0);
            }

            return responseAdu;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] WithCrLf(byte[] data)
    {
        byte[] with = new byte[data.Length + 2];
        data.CopyTo(with, 0);
        with[^2] = (byte)'\r';
        with[^1] = (byte)'\n';
        return with;
    }
}
