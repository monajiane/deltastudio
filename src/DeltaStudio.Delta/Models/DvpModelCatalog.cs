using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Model;

namespace DeltaStudio.Delta.Models;

/// <summary>
/// Verified capability data for the supported DVP CPUs.
/// Sources (public Delta documents, fetched 2026-10):
/// - "DVP-ES2/EX2/SS2/SA2/SX2/SE and TP Operation Manual — Programming" (device tables):
///   https://deltronics.ru/images/manual/DVP-ES2-EX2-SS2-SA2-SX2-SE-TP_PM_EN_20181030.pdf
/// - DVP-SS2 series specification sheet (capacities, I/O counts):
///   https://deltaacdrives.com/Accessories/Delta-DVP-SS2-Series-Standard-Slim-Instruction-Sheet.pdf
/// Verification flags below are honest: family-wide tables in that manual cover SS2/SA2/SX2/SE
/// (SV2 shares the ES2-class tables); per-order-code limits (extension modules in use, etc.)
/// still shift the boundaries, so each definition is marked at best Partial until checked on a
/// real CPU with the matching configuration.
/// </summary>
public static class DvpModelCatalog
{
    private const string ProgrammingManual = "DVP-ES2/EX2/SS2/SA2/SX2/SE Operation Manual - Programming (Delta, Oct 2018), ch. 1-3/2 device tables";

    /// <summary>All built-in models keyed by id.</summary>
    public static IReadOnlyDictionary<string, PlcModelDefinition> All { get; } = Build();

    /// <summary>Resolves a model id (case-insensitive).</summary>
    public static bool TryGet(string modelId, out PlcModelDefinition? model)
    {
        model = All.Values.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        return model is not null;
    }

    /// <summary>Ids for picker UIs / MCP tools.</summary>
    public static IReadOnlyList<string> ModelIds => All.Keys.ToArray();

    private static Dictionary<string, PlcModelDefinition> Build()
    {
        var defs = new List<PlcModelDefinition>
        {
            Ss2("DVP12SS211R", "12SS211R", 6, 4, "Relay"),
            Ss2("DVP12SS211T", "12SS211T", 6, 4, "Transistor NPN"),
            Ss2("DVP12SS211S", "12SS211S", 6, 4, "Transistor PNP"),
            Ss2("DVP14SS211R", "14SS211R", 8, 6, "Relay"),
            Ss2("DVP14SS211T", "14SS211T", 8, 6, "Transistor NPN"),
            Ss2("DVP14SS211S", "14SS211S", 8, 6, "Transistor PNP"),
            Ss2("DVP14SS2T", "14SS2T", 8, 6, "Transistor NPN"),
            Ss2("DVP14SS2R", "14SS2R", 8, 6, "Relay"),
            Ss2("DVP14SS2S", "14SS2S", 8, 6, "Transistor PNP"),
            Ss2("DVP28SS211R", "28SS211R", 16, 12, "Relay"),
            Ss2("DVP28SS211T", "28SS211T", 16, 12, "Transistor NPN"),
            Ss2("DVP28SS211S", "28SS211S", 16, 12, "Transistor PNP"),
            FamilyWide("DVP16SA211R", "DVP-SA2", "16SA211R", 16, 16, 16384, "Relay"),
            FamilyWide("DVP16SA211T", "DVP-SA2", "16SA211T", 16, 16, 16384, "Transistor NPN"),
            FamilyWide("DVP32SA211R", "DVP-SA2", "32SA211R", 16, 16, 16384, "Relay"),
            FamilyWide("DVP16SX211R", "DVP-SX2", "16SX211R", 8, 8, 16384, "Relay (analog CPU)"),
            FamilyWide("DVP16SX211T", "DVP-SX2", "16SX211T", 8, 8, 16384, "Transistor (analog CPU)"),
            FamilyWide("DVP16SE211R", "DVP-SE", "16SE211R", 8, 8, 16384, "Relay"),
            FamilyWide("DVP16SE211T", "DVP-SE", "16SE211T", 8, 8, 16384, "Transistor"),
            FamilyWide("DVP32SE211T", "DVP-SE", "32SE211T", 16, 16, 16384, "Transistor"),
            FamilyWide("DVP20SV11R", "DVP-SV2", "20SV211R", 12, 8, 16384, "Relay"),
            FamilyWide("DVP20SV210T", "DVP-SV2", "20SV210T", 12, 8, 16384, "Transistor NPN (2-axis)"),
            FamilyWide("DVP20SV211T", "DVP-SV2", "20SV211T", 12, 8, 16384, "Transistor NPN (2-axis)"),
            FamilyWide("DVP32SV210T", "DVP-SV2", "32SV210T", 16, 16, 16384, "Transistor NPN (2-axis)"),
        };

        return defs.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>SS2 14-point family entry — program 8k steps, data registers 5k words.</summary>
    private static PlcModelDefinition Ss2(string id, string shortName, int inputs, int outputs, string outputType) =>
        new()
        {
            Id = id,
            Family = "DVP-SS2",
            DisplayName = $"Delta {shortName} ({inputs}IN/{outputs}OUT, {outputType})",
            ProgramCapacitySteps = 8192,
            Comm = PlcCommCapability.Rs232 | PlcCommCapability.Rs485,
            PulseOutputAxes = 4,
            HighSpeedCounterChannels = 4,
            OutputType = outputType,
            Devices = Ss2Devices(inputs, outputs),
            Verification = CapabilityStatus.Partial,
            Source = ProgrammingManual + "; SS2 instruction sheet",
            Notes = "CPU-level device tables verified from the shared ES2/SS2/SA2/SX2/SE programming manual; " +
                    $"built-in point count ({inputs}X/{outputs}Y) from the SS2 datasheet. Extension-module ranges not yet modelled.",
        };

    /// <summary>16k-step family entries (SA2/SX2/SE/SV2 share the manual tables).</summary>
    private static PlcModelDefinition FamilyWide(string id, string family, string shortName, int inputs, int outputs, int programSteps, string outputType) =>
        new()
        {
            Id = id,
            Family = family,
            DisplayName = $"Delta {shortName} ({family})",
            ProgramCapacitySteps = programSteps,
            Comm = PlcCommCapability.Rs232 | PlcCommCapability.Rs485,
            PulseOutputAxes = family == "DVP-SV2" ? 4 : 2,
            HighSpeedCounterChannels = family == "DVP-SV2" ? 8 : 6,
            OutputType = outputType,
            Devices = Sa2Devices(inputs, outputs),
            Verification = CapabilityStatus.Partial,
            Source = ProgrammingManual,
            Notes = "Shared SA2/SX2/SE table; SV2 uses the same core ranges (ES2-class manual). " +
                    "Per-CPU ordering-code differences require hardware verification.",
        };

    internal static IReadOnlyDictionary<DeviceKind, DeviceCapabilities> Ss2Devices(int xCount, int yCount)
    {
        return new Dictionary<DeviceKind, DeviceCapabilities>
        {
            [DeviceKind.X] = new(DeviceKind.X,
                [new DeviceRange(0, xCount - 1, "built-in"), new DeviceRange(xCount, 255, "extension modules — DISABLED until modelled")],
                DeviceAccess.ReadOnly, CapabilityStatus.Partial, ProgrammingManual,
                "Octal addressing. Only built-in range is enforced as fully verified; extension ranges are placeholders kept visible but flagged."),
            [DeviceKind.Y] = new(DeviceKind.Y,
                [new DeviceRange(0, yCount - 1, "built-in"), new DeviceRange(yCount, 255, "extension modules — DISABLED until modelled")],
                DeviceAccess.ReadWrite, CapabilityStatus.Partial, ProgrammingManual),
            [DeviceKind.M] = new(DeviceKind.M,
                [
                    new DeviceRange(0, 511, "general"),
                    new DeviceRange(512, 999, "latched"),
                    new DeviceRange(1000, 1999, "special (some latched)"),
                    new DeviceRange(2000, 2047, "general"),
                    new DeviceRange(2048, 4095, "latched"),
                ],
                DeviceAccess.ReadWrite, CapabilityStatus.Implemented, ProgrammingManual,
                "SS2/SA2 class 4096-point M map transcribed from the manual's M table."),
            [DeviceKind.S] = new(DeviceKind.S,
                [
                    new DeviceRange(0, 19, "general (initial state)"),
                    new DeviceRange(20, 127, "latched (SFC states)"),
                    new DeviceRange(128, 911, "general"),
                    new DeviceRange(912, 1023, "alarm"),
                ],
                DeviceAccess.ReadWrite, CapabilityStatus.Implemented, ProgrammingManual),
            [DeviceKind.L] = new(DeviceKind.L,
                [new DeviceRange(0, 255, "latched")],
                DeviceAccess.ReadWrite, CapabilityStatus.Partial, ProgrammingManual,
                "L exists in the family tables; availability on SS2 specifically needs hardware confirmation."),
            [DeviceKind.T] = new(DeviceKind.T,
                [
                    new DeviceRange(0, 126, "100ms general"),
                    new DeviceRange(127, 127, "1ms general"),
                    new DeviceRange(128, 183, "10ms latched"),
                    new DeviceRange(184, 199, "subroutine 1ms"),
                    new DeviceRange(200, 239, "1ms latched"),
                    new DeviceRange(240, 245, "accumulative 0.1ms"),
                    new DeviceRange(246, 249, "accumulative 1ms"),
                    new DeviceRange(250, 255, "special 1ms"),
                ],
                DeviceAccess.ContactAndValue, CapabilityStatus.Implemented, ProgrammingManual),
            [DeviceKind.C] = new(DeviceKind.C,
                [
                    new DeviceRange(0, 111, "16-bit up-count general"),
                    new DeviceRange(112, 127, "16-bit up/down"),
                    new DeviceRange(200, 219, "32-bit up/down latched"),
                    new DeviceRange(220, 234, "32-bit up/down"),
                    new DeviceRange(235, 255, "high-speed 1-phase"),
                    new DeviceRange(256, 261, "high-speed 2-phase/AB (uses 2 devices)"),
                ],
                DeviceAccess.ContactAndValue, CapabilityStatus.Partial, ProgrammingManual,
                "HSC counter allocation depends on X input usage; modelled as the union range."),
            [DeviceKind.D] = new(DeviceKind.D,
                [
                    new DeviceRange(0, 199, "general"),
                    new DeviceRange(200, 511, "latched"),
                    new DeviceRange(512, 999, "latched/file"),
                    new DeviceRange(1000, 1999, "special"),
                    new DeviceRange(2000, 4999, "file registers (5k words total on SS2)"),
                ],
                DeviceAccess.ReadWrite, CapabilityStatus.Partial, ProgrammingManual,
                "SS2 = 5k words per datasheet; ES2/SA2 class = 10k words; file-register split needs per-CPU confirmation."),
        };
    }

    internal static IReadOnlyDictionary<DeviceKind, DeviceCapabilities> Sa2Devices(int xCount, int yCount)
    {
        var d = Ss2Devices(xCount, yCount).ToDictionary(kv => kv.Key, kv => kv.Value);
        d[DeviceKind.D] = d[DeviceKind.D] with
        {
            Ranges =
            [
                new DeviceRange(0, 199, "general"),
                new DeviceRange(200, 511, "latched"),
                new DeviceRange(512, 999, "latched/file"),
                new DeviceRange(1000, 1999, "special"),
                new DeviceRange(2000, 7999, "general"),
                new DeviceRange(8000, 9999, "file registers (K)"),
            ],
            Notes = "10k words per SA2/SX2/SE manual tables.",
        };
        return d;
    }
}
