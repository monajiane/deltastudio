using System.Text;
using DeltaStudio.Core.Common;
using DeltaStudio.Core.Instructions;
using DeltaStudio.Core.Model;
using DeltaStudio.Core.Program;
using DeltaStudio.Core.Project;
using DeltaStudio.Core.Validation;
using DeltaStudio.Compiler.Il;

namespace DeltaStudio.Compiler.Dvp;

/// <summary>Result of the Delta assembly stage.</summary>
public sealed record DvpAssemblyResult
{
    /// <summary>Symbolic listing text (one line per emitted element, with step counts where verified).</summary>
    public required string Listing { get; init; }

    /// <summary>Validation report the listing was produced under.</summary>
    public required ValidationReport Report { get; init; }

    /// <summary>True only if machine code could be produced. Always false today: Delta's object encoding is not published (see docs/communication.md).</summary>
    public required bool BinaryEmittable { get; init; }

    /// <summary>Why binary emission is blocked (or would be); empty when emittable.</summary>
    public required string BinaryBlockedReason { get; init; }
}

/// <summary>
/// Abstraction over the (undocumented) Delta object-code encoding. DeltaStudio ships NO
/// implementation; when one is written (verified against captured WPLSoft traffic or official
/// material), it plugs in here without touching the IR, validator or UI.
/// </summary>
public interface IDvpObjectCodeEncoder
{
    /// <summary>Whether this encoder has verified encodings.</summary>
    ProtocolStatus Status { get; }

    /// <summary>Encodes a validated program to object code bytes for download. Not implemented → <see cref="NotImplementedException"/>.</summary>
    byte[] Encode(PlcProgram program, PlcModelDefinition model);
}

/// <summary>
/// Translates the IR to a human-readable Delta symbolic listing and (when an encoder exists)
/// object code. Compilation always runs full validation first — MCP/WinUI can offer
/// "compile" without pretending a download-capable binary can be produced.
/// </summary>
public sealed class DvpAssembler
{
    private readonly ProjectValidator _validator;
    private readonly IInstructionCatalog _catalog;
    private readonly IDvpObjectCodeEncoder? _encoder;

    /// <summary>Creates the assembler.</summary>
    public DvpAssembler(IInstructionCatalog catalog, ProjectValidator validator, IDvpObjectCodeEncoder? encoder = null)
    {
        _catalog = catalog;
        _validator = validator;
        _encoder = encoder;
    }

    /// <summary>Validates then assembles.</summary>
    public DvpAssemblyResult Assemble(PlcProject project, PlcModelDefinition model)
    {
        ValidationReport report = _validator.Validate(project, model);
        string listing = BuildListing(project.Program, model);

        bool binary = _encoder is not null && _encoder.Status == ProtocolStatus.Supported && !report.HasErrors;
        string blocked = binary
            ? string.Empty
            : _encoder is null
                ? "No Delta object-code encoder is available: Delta's program memory encoding is not published. " +
                  "Compilation stops at the symbolic listing. See docs/instructions.md and docs/communication.md."
                : "The configured encoder reports non-supported status.";

        return new DvpAssemblyResult
        {
            Listing = listing,
            Report = report,
            BinaryEmittable = binary,
            BinaryBlockedReason = blocked,
        };
    }

    /// <summary>Builds the symbolic listing (independent of validation errors).</summary>
    public string BuildListing(PlcProgram program, PlcModelDefinition model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; DeltaStudio symbolic listing — target {model.Id} ({model.Family})");
        sb.AppendLine($"; Program capacity: {model.ProgramCapacitySteps} steps");
        sb.AppendLine($"; NOTE: addresses/steps are symbolic. Object codes are UNVERIFIED (Delta does not publish them).");
        sb.AppendLine();

        int globalStep = 0;
        for (int r = 0; r < program.Main.Count; r++)
        {
            Rung rung = program.Main[r];
            sb.AppendLine($"; ---- Rung {r + 1}{(string.IsNullOrEmpty(rung.Comment) ? "" : " : " + rung.Comment)}");
            IlFormatter formatter = new();
            string[] lines = formatter.Format(rung).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string op = line.Trim().Split(' ')[0].ToUpperInvariant();
                int cost = StepCost(op);
                globalStep += cost;
                sb.AppendLine($"{globalStep,6:0000}  {line.Trim()}");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"; Total counted basic steps: ~{globalStep} (application-instruction costs are not verified per model)");
        sb.AppendLine("FEND");
        return sb.ToString();
    }

    private static int StepCost(string op) => op switch
    {
        "LD" or "LDI" or "LDP" or "LDF" => 1,
        "AND" or "ANI" or "ANP" or "ANF" => 1,
        "OR" or "ORI" or "ORP" or "ORF" => 1,
        "ANB" or "ORB" => 1,
        "OUT" or "SET" or "RST" => 1,
        _ => 0, // unknown cost — never invented
    };
}
