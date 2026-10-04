using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Modbus;

namespace DeltaStudio.Delta.Comm;

/// <summary>One contiguous device→area binding inside the map.</summary>
public sealed record ModbusMapEntry(ModbusArea Area, int ModbusStart, DeviceKind Kind, int DeviceStart, int Count, string Label);

/// <summary>
/// MODBUS mapping for the DVP family.
/// The table below is transcribed from the mapping chapter of Delta's published
/// "MODBUS ASCII/RTU Application and Circuits" guides (also mirrored in widely used community
/// references). It is marked <see cref="CapabilityStatus.Partial"/>: the mapping is documented,
/// but DeltaStudio has not confirmed it against a physical CPU — every read/write therefore
/// goes through this table so a field-verified override can be swapped in per site.
/// The table is also overridable from JSON (see <see cref="FromEntries"/>) because ES2/EX2/
/// SS2/SA2/SX2/SE share it while EH3/ES3 differ.
/// </summary>
public sealed class DvpModbusAddressMap : IModbusDeviceMap
{
    /// <summary>Implementation status of this mapping (documentation verified; hardware not).</summary>
    public static ProtocolStatus VerificationStatus => ProtocolStatus.PartiallySupported;

    /// <summary>Notes for docs/UI.</summary>
    public const string VerificationNote =
        "Bit/word mapping transcribed from Delta MODBUS ASCII/RTU guides; no hardware confirmation has been " +
        "performed by DeltaStudio. Verify on-site before relying on writes. Source examples in docs/communication.md.";

    private static readonly ModbusMapEntry[] DefaultEntries =
    [
        new(ModbusArea.Coils,           0x0000, DeviceKind.S, 0, 1024, "S0-S1023 step relays"),
        new(ModbusArea.Coils,           0x0400, DeviceKind.L, 0, 256,  "L0-L255 latched"),
        new(ModbusArea.Coils,           0x0500, DeviceKind.Y, 0, 256,  "Y0-Y377 outputs (linear index)"),
        new(ModbusArea.Coils,           0x0600, DeviceKind.T, 0, 256,  "T0-T255 timer contacts"),
        new(ModbusArea.Coils,           0x0800, DeviceKind.M, 0, 1024, "M0-M1023"),
        new(ModbusArea.Coils,           0x0C00, DeviceKind.M, 1024, 3072, "M1024-M4095"),
        new(ModbusArea.Coils,           0x0E00, DeviceKind.C, 0, 256,  "C0-C255 counter contacts"),

        new(ModbusArea.DiscreteInputs,  0x0400, DeviceKind.X, 0, 256,  "X0-X377 inputs"),
        new(ModbusArea.DiscreteInputs,  0x0600, DeviceKind.T, 0, 256,  "T contacts (read-only view)"),

        new(ModbusArea.HoldingRegisters, 0x0600, DeviceKind.T, 0, 256, "T0-T255 present values"),
        new(ModbusArea.HoldingRegisters, 0x0E00, DeviceKind.C, 0, 256, "C0-C255 present values"),
        // D starts at 0x1000 (0x0E00+256 == 0x1000 ⇒ no overlap; same layout the guides show).
        new(ModbusArea.HoldingRegisters, 0x1000, DeviceKind.D, 0, 8192, "D0-D8191"),
    ];

    private readonly ModbusMapEntry[] _entries;

    /// <summary>Creates the map with the default DVP table.</summary>
    public DvpModbusAddressMap() : this(DefaultEntries)
    {
    }

    private DvpModbusAddressMap(ModbusMapEntry[] entries) => _entries = entries;

    /// <summary>Creates the map from an explicit table (site overrides, EH-class variants).</summary>
    public static DvpModbusAddressMap FromEntries(IEnumerable<ModbusMapEntry> entries) => new(entries.ToArray());

    /// <summary>Table entries (for resource exposure / diagnostics).</summary>
    public IReadOnlyList<ModbusMapEntry> Entries => _entries;

    /// <inheritdoc/>
    public bool TryMapBit(DeviceAddress device, out ModbusArea area, out int address)
    {
        area = default;
        address = 0;
        foreach (ModbusMapEntry e in _entries)
        {
            if (e.Kind != device.Kind || e.Area == ModbusArea.HoldingRegisters || e.Area == ModbusArea.InputRegisters)
            {
                continue;
            }

            if (device.Number >= e.DeviceStart && device.Number < e.DeviceStart + e.Count)
            {
                area = e.Area;
                address = e.ModbusStart + (device.Number - e.DeviceStart);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryMapWord(DeviceAddress device, out ModbusArea area, out int address)
    {
        area = default;
        address = 0;

        // 32-bit counters occupy two consecutive words: their map index already accounts for that.
        foreach (ModbusMapEntry e in _entries)
        {
            if (e.Kind != device.Kind || (e.Area != ModbusArea.HoldingRegisters && e.Area != ModbusArea.InputRegisters))
            {
                continue;
            }

            if (device.Number >= e.DeviceStart && device.Number < e.DeviceStart + e.Count)
            {
                area = e.Area;
                address = e.ModbusStart + (device.Number - e.DeviceStart);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryResolveBit(ModbusArea area, int address, out DeviceAddress device)
    {
        device = default;
        foreach (ModbusMapEntry e in _entries)
        {
            if (e.Area != area || (area != ModbusArea.Coils && area != ModbusArea.DiscreteInputs))
            {
                continue;
            }

            if (address >= e.ModbusStart && address < e.ModbusStart + e.Count)
            {
                device = new DeviceAddress(e.Kind, e.DeviceStart + (address - e.ModbusStart));
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryResolveWord(ModbusArea area, int address, out DeviceAddress device)
    {
        device = default;
        foreach (ModbusMapEntry e in _entries)
        {
            if (e.Area != area || (area != ModbusArea.HoldingRegisters && area != ModbusArea.InputRegisters))
            {
                continue;
            }

            if (address >= e.ModbusStart && address < e.ModbusStart + e.Count)
            {
                device = new DeviceAddress(e.Kind, e.DeviceStart + (address - e.ModbusStart));
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool IsWritable(DeviceAddress device) => device.Kind switch
    {
        DeviceKind.X => false,
        DeviceKind.Y or DeviceKind.M or DeviceKind.S or DeviceKind.L or DeviceKind.D => true,
        // T/C: the contact is not writable; present value is (enforced via the bit/word split above).
        _ => false,
    };

    /// <inheritdoc/>
    public bool IsDoubleWord(DeviceAddress device) =>
        device.Kind == DeviceKind.C && device.Number is >= 200 and <= 255; // 32-bit counters
}
