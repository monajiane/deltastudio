using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;

namespace DeltaStudio.Core.Instructions;

/// <summary>Instruction categories aligned with the Delta programming manual grouping.</summary>
public enum InstructionCategory
{
    /// <summary>LD/AND/OR family terminal logic.</summary>
    TerminalLogic,
    /// <summary>OUT/SET/RST.</summary>
    Output,
    /// <summary>Timers.</summary>
    Timer,
    /// <summary>Counters.</summary>
    Counter,
    /// <summary>MOV/CMP/…</summary>
    DataComparisonTransfer,
    /// <summary>ADD/SUB/MUL/DIV/INC/DEC.</summary>
    Arithmetic,
    /// <summary>BIN/BCD/AND/OR/XOR… (advanced math group).</summary>
    AdvancedMath,
    /// <summary>ROL/ROR/RCR/RCL/SHL…</summary>
    ShiftRotate,
    /// <summary>FNC table/pointer group.</summary>
    FncPointerTable,
    /// <summary>HSCS/HSCR/SPD…</summary>
    HighSpeedCounting,
    /// <summary>PLSY/DRVI/DMOV… positioning group.</summary>
    Positioning,
    /// <summary>FROM/TO/MODRD/MODWR…</summary>
    ExtendedFunction,
    /// <summary>READRTC, clock group.</summary>
    Clock,
    /// <summary>Program control (CJ, SBC, FEND…).</summary>
    ProgramControl,
    /// <summary>Mov/floating point etc.</summary>
    FloatingPoint,
    /// <summary>Anything not in the manual groups above.</summary>
    Other,
}

/// <summary>Operand shape constraint checked by the validator.</summary>
public enum OperandConstraint
{
    /// <summary>Bit device only (X, Y, M, S, L, T/C contacts).</summary>
    BitDevice,
    /// <summary>Word device only (D; T/C current values where the manual defines it).</summary>
    WordDevice,
    /// <summary>Word device or 16-bit K/H constant.</summary>
    WordDeviceOrConstant,
    /// <summary>Word device or 32-bit K/H constant.</summary>
    DeviceOrConstant32,
    /// <summary>Timer reference (Tnn).</summary>
    Timer,
    /// <summary>Counter reference (Cnn).</summary>
    Counter,
    /// <summary>Any device class.</summary>
    AnyDevice,
    /// <summary>K/H constant only.</summary>
    Constant,
    /// <summary>Free-form string argument (e.g. SFR set table id) — kept textual.</summary>
    Raw,
}

/// <summary>Description of one operand slot of an instruction.</summary>
public sealed record OperandSpec(
    string Name,
    OperandConstraint Constraint,
    bool Optional = false,
    string? Notes = null);

/// <summary>
/// Catalog entry describing one instruction. Semantics (mnemonic, operands, validation rules)
/// are transcribed from Delta's published programming manual; the encoding column is a flag,
/// never invented opcodes — see <see cref="EncodingVerified"/>.
/// </summary>
public sealed record InstructionDefinition
{
    /// <summary>Canonical uppercase mnemonic.</summary>
    public required string Mnemonic { get; init; }

    /// <summary>Manual category.</summary>
    public required InstructionCategory Category { get; init; }

    /// <summary>One-paragraph functional description (written from the manual).</summary>
    public required string Summary { get; init; }

    /// <summary>Operand slots in order.</summary>
    public IReadOnlyList<OperandSpec> Operands { get; init; } = Array.Empty<OperandSpec>();

    /// <summary>Model family ids supported; null = every family of the backend.</summary>
    public IReadOnlyList<string>? SupportedFamilies { get; init; }

    /// <summary>Delta FNC number when the manual assigns one (0 for logic instructions).</summary>
    public int? Fnc { get; init; }

    /// <summary>True when a D-prefixed 32-bit variant exists (MOV→DMOV as its own mnemonic on Delta).</summary>
    public bool HasDoubleVariant { get; init; }

    /// <summary>How well the documented semantics were verified during transcription.</summary>
    public CapabilityStatus SemanticStatus { get; init; } = CapabilityStatus.Partial;

    /// <summary>
    /// True only when the byte-level opcode encoding has been verified against an authoritative
    /// source or captured traffic. DeltaStudio currently sets this to false for every instruction
    /// because Delta's object-code encoding is not published — see docs/instructions.md.
    /// </summary>
    public bool EncodingVerified { get; init; }

    /// <summary>Whether the instruction is relevant to live monitoring (coil/contact state, T/C values).</summary>
    public bool MonitoredAtRuntime { get; init; } = true;

    /// <summary>Documentation reference (manual chapter / URL).</summary>
    public string? DocumentationRef { get; init; }
}

/// <summary>Lookup abstraction so any backend can plug its instruction database.</summary>
public interface IInstructionCatalog
{
    /// <summary>Backend name, e.g. "DeltaDvp".</summary>
    string Backend { get; }

    /// <summary>Finds an instruction by mnemonic (case-insensitive).</summary>
    bool TryGet(string mnemonic, out InstructionDefinition? definition);

    /// <summary>All known instructions.</summary>
    IReadOnlyList<InstructionDefinition> All { get; }
}
