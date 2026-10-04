using DeltaStudio.Core.Devices;

namespace DeltaStudio.Protocols.Abstractions;

/// <summary>Modbus data table (area).</summary>
public enum ModbusArea
{
    /// <summary>0x — read/write bits (FC01/05/15).</summary>
    Coils,
    /// <summary>1x — read-only bits (FC02).</summary>
    DiscreteInputs,
    /// <summary>3x — read-only words (FC04).</summary>
    InputRegisters,
    /// <summary>4x — read/write words (FC03/06/16).</summary>
    HoldingRegisters,
}

/// <summary>A single device value: bit or 16-bit word.</summary>
public readonly struct DeviceValue
{
    private DeviceValue(bool bit, ushort word, bool isWord)
    {
        Bit = bit;
        Word = word;
        IsWord = isWord;
    }

    /// <summary>True when this value carries a 16-bit word.</summary>
    public bool IsWord { get; }

    /// <summary>Bit value (valid when !IsWord).</summary>
    public bool Bit { get; }

    /// <summary>Word value (valid when IsWord).</summary>
    public ushort Word { get; }

    /// <summary>Word as signed.</summary>
    public short SignedWord => unchecked((short)Word);

    /// <summary>Creates a bit value.</summary>
    public static DeviceValue FromBit(bool v) => new(v, 0, false);

    /// <summary>Creates a word value.</summary>
    public static DeviceValue FromWord(ushort v) => new(false, v, true);

    /// <summary>Creates a word value from signed.</summary>
    public static DeviceValue FromInt16(short v) => new(false, unchecked((ushort)v), true);

    /// <inheritdoc/>
    public override string ToString() => IsWord ? Word.ToString() : (Bit ? "1" : "0");
}

/// <summary>Result of PLC identification (whatever the transport can honestly determine).</summary>
public sealed record PlcIdentification
{
    /// <summary>Vendor tag.</summary>
    public string Vendor { get; init; } = "Unknown";

    /// <summary>Model code reported by the CPU (raw).</summary>
    public ushort ModelCode { get; init; }

    /// <summary>Firmware version reported (raw).</summary>
    public ushort Version { get; init; }

    /// <summary>Model id resolved against a catalog, when the CPU code maps to a known definition.</summary>
    public string? ResolvedModelId { get; init; }

    /// <summary>
    /// True when identification data is trustworthy for this transport. DVP identification via
    /// MODBUS FC07 flow-control is community-documented, not confirmed against hardware here → false.
    /// </summary>
    public bool VerifiedAgainstHardware { get; init; }

    /// <summary>Free-form human summary.</summary>
    public string? Notes { get; init; }
}

/// <summary>Device-oriented read/write access to a connected PLC.</summary>
public interface IPlcDeviceAccess
{
    /// <summary>Reads a contiguous block of same-kind devices (count words/bits).</summary>
    Task<IReadOnlyList<DeviceValue>> ReadBlockAsync(DeviceAddress start, int count, CancellationToken ct = default);

    /// <summary>Writes one device (bit or word).</summary>
    Task WriteAsync(DeviceAddress address, DeviceValue value, CancellationToken ct = default);

    /// <summary>Writes a contiguous block of words (D only; T/C present values where the model exposes them).</summary>
    Task WriteBlockAsync(DeviceAddress start, IReadOnlyList<DeviceValue> values, CancellationToken ct = default);
}

/// <summary>
/// A live PLC connection: identify, device access, run-state. Monitoring/polling is layered
/// above this in Infrastructure so UI and MCP share one implementation.
/// </summary>
public interface IPlcLink : IAsyncDisposable
{
    /// <summary>True while the physical transport is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Device read/write for this link.</summary>
    IPlcDeviceAccess Devices { get; }

    /// <summary>Asks the CPU to identify itself.</summary>
    Task<PlcIdentification> IdentifyAsync(CancellationToken ct = default);

    /// <summary>Best-effort run state (e.g. DVP: read M1000 RUN supervision relay). Null when the state cannot be determined honestly.</summary>
    Task<bool?> QueryRunStateAsync(CancellationToken ct = default);

    /// <summary>Aborts the link (no PLC state change).</summary>
    Task DisconnectAsync(CancellationToken ct = default);
}
