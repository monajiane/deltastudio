using DeltaStudio.Core.Common;
using DeltaStudio.Core.Instructions;

namespace DeltaStudio.Delta.Instructions;

/// <summary>
/// Delta DVP instruction database. Semantics (operand shapes, categories) are transcribed from
/// the Delta programming manuals' instruction tables; STEP COSTS and MACHINE ENCODINGS are not
/// published and every entry carries EncodingVerified=false. Unknown/ambiguous instructions are
/// simply absent — the catalog refuses what it cannot justify.
/// </summary>
public sealed class DvpInstructionCatalog : IInstructionCatalog
{
    private const string Doc = "Delta DVP-ES2/EX2/SS2/SA2/SX2/SE Programming Manual, Ch.3 (basic) + FNC instruction lists";

    private readonly Dictionary<string, InstructionDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the catalog.</summary>
    public DvpInstructionCatalog()
    {
        Add("LD", InstructionCategory.TerminalLogic, "Load: starts a logic block with a normally-open contact.", fnc: null);
        Add("LDI", InstructionCategory.TerminalLogic, "Load inverse: starts a block with a normally-closed contact.");
        Add("LDP", InstructionCategory.TerminalLogic, "Load pulse: rising-edge detection contact.");
        Add("LDF", InstructionCategory.TerminalLogic, "Load falling: falling-edge detection contact.");
        Add("AND", InstructionCategory.TerminalLogic, "Series normally-open contact.");
        Add("ANI", InstructionCategory.TerminalLogic, "Series normally-closed contact.");
        Add("ANP", InstructionCategory.TerminalLogic, "Series rising-edge contact.");
        Add("ANF", InstructionCategory.TerminalLogic, "Series falling-edge contact.");
        Add("OR", InstructionCategory.TerminalLogic, "Parallel normally-open contact.");
        Add("ORI", InstructionCategory.TerminalLogic, "Parallel normally-closed contact.");
        Add("ORP", InstructionCategory.TerminalLogic, "Parallel rising-edge contact.");
        Add("ORF", InstructionCategory.TerminalLogic, "Parallel falling-edge contact.");
        Add("ANB", InstructionCategory.TerminalLogic, "Block AND: serial-combines the previous two logic blocks.");
        Add("ORB", InstructionCategory.TerminalLogic, "Block OR: parallel-combines the previous two logic blocks.");
        Add("MPS", InstructionCategory.TerminalLogic, "Branch stack push (used for nested branches).");
        Add("MRD", InstructionCategory.TerminalLogic, "Branch stack read.");
        Add("MPP", InstructionCategory.TerminalLogic, "Branch stack pop.");
        Add("OUT", InstructionCategory.Output, "Coil output, refreshed every scan.",
            operands: [new("device", OperandConstraint.BitDevice)]);
        Add("SET", InstructionCategory.Output, "Latching coil (keeps device on).",
            operands: [new("device", OperandConstraint.BitDevice)]);
        Add("RST", InstructionCategory.Output, "Reset coil (also clears T/C values, counters).",
            operands: [new("device", OperandConstraint.BitDevice)]);
        Add("TMR", InstructionCategory.Timer, "Timer block: T device + preset (time base depends on the T number).",
            operands: [new("timer", OperandConstraint.Timer), new("preset", OperandConstraint.WordDeviceOrConstant)],
            doc: Doc + " (timer section)");
        Add("CTR", InstructionCategory.Counter, "16-bit counter block: C device + preset.",
            operands: [new("counter", OperandConstraint.Counter), new("preset", OperandConstraint.WordDeviceOrConstant)],
            doc: Doc + " (counter section)");
        Add("STL", InstructionCategory.ProgramControl, "Step ladder: activates an S state (SFC support).",
            operands: [new("state", OperandConstraint.BitDevice)], doc: Doc + " (SFC chapter)");
        Add("RET", InstructionCategory.ProgramControl, "Step ladder return.");
        Add("FEND", InstructionCategory.ProgramControl, "Main program end (interrupt sections may follow).");
        Add("MOV", InstructionCategory.DataComparisonTransfer, "Transfer: S → D.",
            operands: [new("source", OperandConstraint.WordDeviceOrConstant), new("target", OperandConstraint.WordDevice)],
            fnc: 12, hasD: true);
        Add("DMOV", InstructionCategory.DataComparisonTransfer, "32-bit transfer.",
            operands: [new("source", OperandConstraint.DeviceOrConstant32), new("target", OperandConstraint.WordDevice)],
            fnc: 12);
        Add("CMP", InstructionCategory.DataComparisonTransfer, "Compare S1 vs S2, results to 3 consecutive bit devices.",
            operands:
            [
                new("source1", OperandConstraint.WordDeviceOrConstant),
                new("source2", OperandConstraint.WordDeviceOrConstant),
                new("bits", OperandConstraint.BitDevice, Notes: "M/S — occupies 3 consecutive bits (>): <>, <"),
            ],
            fnc: 10, hasD: true);
        Add("ZCP", InstructionCategory.DataComparisonTransfer, "Zone compare S1≤S≤S2 → 3 bits.",
            operands:
            [
                new("low", OperandConstraint.WordDeviceOrConstant),
                new("high", OperandConstraint.WordDeviceOrConstant),
                new("source", OperandConstraint.WordDeviceOrConstant),
                new("bits", OperandConstraint.BitDevice),
            ],
            fnc: 11, hasD: true);
        Add("ADD", InstructionCategory.Arithmetic, "Add: S1 + S2 → D (carry flag M8022).",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 20, hasD: true);
        Add("SUB", InstructionCategory.Arithmetic, "Subtract: S1 - S2 → D.",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 21, hasD: true);
        Add("MUL", InstructionCategory.Arithmetic, "Multiply: S1 × S2 → D (32-bit result in two words).",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 22);
        Add("DIV", InstructionCategory.Arithmetic, "Divide: S1 ÷ S2 → D (quotient D, remainder D+1).",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 23);
        Add("INC", InstructionCategory.Arithmetic, "Increment destination.",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 32, hasD: true);
        Add("DEC", InstructionCategory.Arithmetic, "Decrement destination.",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 33, hasD: true);
        Add("INCD", InstructionCategory.Arithmetic, "Increment destination (32-bit).",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 32);
        Add("DECD", InstructionCategory.Arithmetic, "Decrement destination (32-bit).",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 33);
        Add("DADD", InstructionCategory.Arithmetic, "32-bit add: S1 + S2 → D.",
            operands:
            [
                new("s1", OperandConstraint.DeviceOrConstant32),
                new("s2", OperandConstraint.DeviceOrConstant32),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 20);
        Add("WAND", InstructionCategory.AdvancedMath, "Logic AND of words.",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 24);
        Add("WOR", InstructionCategory.AdvancedMath, "Logic OR of words.",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 25);
        Add("WXOR", InstructionCategory.AdvancedMath, "Logic XOR of words.",
            operands:
            [
                new("s1", OperandConstraint.WordDeviceOrConstant),
                new("s2", OperandConstraint.WordDeviceOrConstant),
                new("d", OperandConstraint.WordDevice),
            ],
            fnc: 26);
        Add("ZRST", InstructionCategory.DataComparisonTransfer, "Range reset: clears all devices S1..S2.",
            operands: [new("from", OperandConstraint.AnyDevice), new("to", OperandConstraint.AnyDevice)],
            fnc: 40, hasD: false);
        Add("SFR", InstructionCategory.DataComparisonTransfer, "Device preset via table: pairs of (device, value) from S.",
            operands: [new("table", OperandConstraint.WordDeviceOrConstant), new("count", OperandConstraint.Constant)],
            fnc: 41);
        Add("FROM", InstructionCategory.ExtendedFunction, "Read module register (special/right expansion).",
            operands: [new("module", OperandConstraint.Constant), new("addr", OperandConstraint.WordDeviceOrConstant), new("dest", OperandConstraint.WordDevice), new("count", OperandConstraint.Constant)],
            fnc: 78);
        Add("TO", InstructionCategory.ExtendedFunction, "Write module register.",
            operands: [new("module", OperandConstraint.Constant), new("addr", OperandConstraint.WordDeviceOrConstant), new("source", OperandConstraint.WordDeviceOrConstant), new("count", OperandConstraint.Constant)],
            fnc: 79);
        Add("HSCS", InstructionCategory.HighSpeedCounting, "High-speed comparison set.",
            operands: [new("s1", OperandConstraint.AnyDevice), new("s2", OperandConstraint.AnyDevice), new("d", OperandConstraint.BitDevice)],
            fnc: 53);
        Add("HSCR", InstructionCategory.HighSpeedCounting, "High-speed comparison reset.",
            operands: [new("s1", OperandConstraint.AnyDevice), new("s2", OperandConstraint.AnyDevice), new("d", OperandConstraint.BitDevice)],
            fnc: 54);
        Add("SPD", InstructionCategory.HighSpeedCounting, "Speed detection on input X.",
            operands: [new("x", OperandConstraint.BitDevice), new("time", OperandConstraint.WordDeviceOrConstant), new("d", OperandConstraint.WordDevice)],
            fnc: 55);
        Add("PLSY", InstructionCategory.Positioning, "Pulse output (frequency, count).",
            operands: [new("freq", OperandConstraint.WordDeviceOrConstant), new("count", OperandConstraint.AnyDevice), new("y", OperandConstraint.BitDevice)],
            fnc: 56,
            families: ["DVP-SA2", "DVP-SX2", "DVP-SE", "DVP-SV2", "DVP-SS2"]);
        Add("PLSR", InstructionCategory.Positioning, "Pulse with duty ratio.",
            operands: [new("freq", OperandConstraint.AnyDevice), new("count", OperandConstraint.AnyDevice), new("duty", OperandConstraint.AnyDevice), new("y", OperandConstraint.BitDevice)],
            fnc: 66);
        Add("DRVI", InstructionCategory.Positioning, "Relative positioning.",
            operands: [new("dist", OperandConstraint.AnyDevice), new("freq", OperandConstraint.AnyDevice), new("y1", OperandConstraint.BitDevice), new("y0", OperandConstraint.BitDevice)],
            fnc: 59);
        Add("DRVA", InstructionCategory.Positioning, "Absolute positioning.",
            operands: [new("pos", OperandConstraint.AnyDevice), new("freq", OperandConstraint.AnyDevice), new("y1", OperandConstraint.BitDevice), new("y0", OperandConstraint.BitDevice)],
            fnc: 58);
        Add("DPLSY", InstructionCategory.Positioning, "32-bit pulse output.",
            operands: [new("freq", OperandConstraint.AnyDevice), new("count", OperandConstraint.AnyDevice), new("y", OperandConstraint.BitDevice)],
            fnc: 56);
        Add("DPLSR", InstructionCategory.Positioning, "32-bit pulse with duty.",
            operands: [new("freq", OperandConstraint.AnyDevice), new("count", OperandConstraint.AnyDevice), new("duty", OperandConstraint.AnyDevice), new("y", OperandConstraint.BitDevice)],
            fnc: 66);
        Add("DDRVI", InstructionCategory.Positioning, "32-bit relative positioning.",
            operands: [new("dist", OperandConstraint.AnyDevice), new("freq", OperandConstraint.AnyDevice), new("y1", OperandConstraint.BitDevice), new("y0", OperandConstraint.BitDevice)],
            fnc: 59);
        Add("DDRVA", InstructionCategory.Positioning, "32-bit absolute positioning.",
            operands: [new("pos", OperandConstraint.AnyDevice), new("freq", OperandConstraint.AnyDevice), new("y1", OperandConstraint.BitDevice), new("y0", OperandConstraint.BitDevice)],
            fnc: 58);
        Add("TRD", InstructionCategory.Clock, "Read real-time clock into D+0..D+5.",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 72, families: ["DVP-SA2", "DVP-SX2", "DVP-SE", "DVP-SS2"]);
        Add("TWRT", InstructionCategory.Clock, "Write real-time clock from D+0..D+5.",
            operands: [new("d", OperandConstraint.WordDevice)], fnc: 73, families: ["DVP-SA2", "DVP-SX2", "DVP-SE", "DVP-SS2"]);
        Add("ALT", InstructionCategory.Output, "Toggle output on each rising condition.",
            operands: [new("d", OperandConstraint.BitDevice)], fnc: 51);
        Add("MC", InstructionCategory.ProgramControl, "Master control (zone start).",
            operands: [new("n", OperandConstraint.Constant), new("d", OperandConstraint.BitDevice)], fnc: 120);
        Add("MCR", InstructionCategory.ProgramControl, "Master control reset (zone end).",
            operands: [new("n", OperandConstraint.Constant)], fnc: 121);

        // Special relays/registers used by higher layers are documented in docs/delta-dvp.md;
        // they are devices, not instructions, and need no catalog entries.
    }

    /// <inheritdoc/>
    public string Backend => "DeltaDvp";

    /// <inheritdoc/>
    public bool TryGet(string mnemonic, out InstructionDefinition? definition)
    {
        mnemonic = mnemonic.Trim().ToUpperInvariant();
        definition = null;
        return _byName.TryGetValue(mnemonic, out definition);
    }

    /// <inheritdoc/>
    public IReadOnlyList<InstructionDefinition> All => _byName.Values.OrderBy(d => d.Fnc ?? 0).ThenBy(d => d.Mnemonic).ToList();

    private void Add(
        string mnemonic,
        InstructionCategory category,
        string summary,
        IReadOnlyList<OperandSpec>? operands = null,
        int? fnc = null,
        bool hasD = false,
        string[]? families = null,
        string? doc = null,
        string? mnem = null)
    {
        string actual = mnem ?? mnemonic;
        _byName[actual] = new InstructionDefinition
        {
            Mnemonic = actual,
            Category = category,
            Summary = summary,
            Operands = operands ?? (category == InstructionCategory.TerminalLogic
                ? [new OperandSpec("device", OperandConstraint.AnyDevice)]
                : Array.Empty<OperandSpec>()),
            Fnc = fnc,
            HasDoubleVariant = hasD,
            SupportedFamilies = families,
            DocumentationRef = doc ?? Doc,
            SemanticStatus = fnc is null ? CapabilityStatus.Implemented : CapabilityStatus.Partial,
            EncodingVerified = false,
        };
    }
}
