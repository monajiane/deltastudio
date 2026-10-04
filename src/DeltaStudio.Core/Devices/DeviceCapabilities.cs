using DeltaStudio.Core.Common;

namespace DeltaStudio.Core.Devices;

/// <summary>Inclusive contiguous index range in which a device class is valid for one model.</summary>
public sealed record DeviceRange(int Min, int Max, string? Label = null)
{
    /// <summary>True when <paramref name="index"/> falls inside the range.</summary>
    public bool Contains(int index) => index >= Min && index <= Max;

    /// <inheritdoc/>
    public override string ToString() => $"{Min}..{Max}{(Label is null ? string.Empty : $" ({Label})")}";
}

/// <summary>
/// What one device class offers on one concrete PLC model. Every concrete table shipped in
/// DeltaStudio.Delta carries the source document it was transcribed from and a verification flag;
/// ranges that were never cross-checked against an official Delta manual are marked
/// <see cref="CapabilityStatus.Partial"/> or <see cref="CapabilityStatus.Experimental"/>.
/// </summary>
public sealed record DeviceCapabilities(
    DeviceKind Kind,
    IReadOnlyList<DeviceRange> Ranges,
    DeviceAccess Access,
    CapabilityStatus Verification,
    string? Source = null,
    string? Notes = null)
{
    /// <summary>Maximum valid linear index across all ranges.</summary>
    public int MaxIndex => Ranges.Count == 0 ? -1 : Ranges.Max(r => r.Max);

    /// <summary>True when the index is covered by at least one range.</summary>
    public bool Contains(DeviceAddress address) => address.Kind == Kind && Ranges.Any(r => r.Contains(address.Number));
}
