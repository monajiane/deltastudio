using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;

namespace DeltaStudio.Core.Model;

/// <summary>Communication interfaces a physical PLC exposes (abstract; not all are implemented).</summary>
[Flags]
public enum PlcCommCapability
{
    /// <summary>None declared.</summary>
    None = 0,

    /// <summary>RS-232 port (COM1 on DVP).</summary>
    Rs232 = 1,

    /// <summary>RS-485 port (COM2/COM3 on DVP).</summary>
    Rs485 = 2,

    /// <summary>Ethernet (EH3/ES3 class only; not present on SS2/SA2/SX2/SE/SV2).</summary>
    Ethernet = 4,

    /// <summary>CANopen option board.</summary>
    Canopen = 8,
}

/// <summary>
/// Capability descriptor of one concrete PLC model. This is the extension point for future
/// vendors (Siemens/Omron/Mitsubishi): a new backend supplies its own definitions with the
/// same shape and nothing above this layer changes.
/// </summary>
public sealed record PlcModelDefinition
{
    /// <summary>Stable identifier used in project files, e.g. "DVP14SS2T".</summary>
    public required string Id { get; init; }

    /// <summary>Vendor family, e.g. "DVP-SS2". Backends may define their own families.</summary>
    public required string Family { get; init; }

    /// <summary>Display name for UI/tooling.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Program memory capacity in steps (per vendor datasheet).</summary>
    public int ProgramCapacitySteps { get; init; }

    /// <summary>Per-device-class memory capabilities.</summary>
    public required IReadOnlyDictionary<DeviceKind, DeviceCapabilities> Devices { get; init; }

    /// <summary>Declared communication interfaces.</summary>
    public PlcCommCapability Comm { get; init; }

    /// <summary>Number of high-speed pulse output axes (0 when none; capability metadata only — motion instructions are out of scope until verified).</summary>
    public int PulseOutputAxes { get; init; }

    /// <summary>Number of built-in high-speed counter channels.</summary>
    public int HighSpeedCounterChannels { get; init; }

    /// <summary>Transistor (T), relay (R) or mixed output type marker from the order code.</summary>
    public string OutputType { get; init; } = "Unknown";

    /// <summary>Document source the ranges were taken from.</summary>
    public string? Source { get; init; }

    /// <summary>How well the numbers in this definition are verified.</summary>
    public CapabilityStatus Verification { get; init; } = CapabilityStatus.Partial;

    /// <summary>Free-form notes, including what is still unverified.</summary>
    public string? Notes { get; init; }

    /// <summary>Tries to find capabilities for a device class.</summary>
    public bool TryGetDevice(DeviceKind kind, out DeviceCapabilities? capabilities)
        => Devices.TryGetValue(kind, out capabilities);
}
