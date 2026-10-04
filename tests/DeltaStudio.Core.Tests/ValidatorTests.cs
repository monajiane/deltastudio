using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Model;
using DeltaStudio.Core.Program;
using DeltaStudio.Core.Project;
using DeltaStudio.Core.Validation;
using DeltaStudio.Delta.Instructions;
using DeltaStudio.Delta.Models;
using Xunit;

namespace DeltaStudio.Core.Tests;

// NOTE: reference to the Delta catalog is deliberate — validation is only meaningful against a
// real instruction database; the Core itself remains vendor-neutral (no dependency edge back).
public class ValidatorTests
{
    private static readonly PlcModelDefinition Model = DvpModelCatalog.TryGet("DVP14SS2T", out PlcModelDefinition? m)
        ? m!
        : throw new InvalidOperationException();

    private static ProjectValidator Validator() => new(new DvpInstructionCatalog());

    private static PlcProject SingleRung(SeriesNetwork logic, string? comment = null)
    {
        var p = new PlcProject();
        p.Program.AddRung(comment).Logic = logic;
        return p;
    }

    private static bool Has(ValidationReport r, string code, DiagnosticSeverity? severity = null) =>
        r.Diagnostics.Any(d => d.Code == code && (severity is null || d.Severity == severity));

    [Fact]
    public void CleanMotorStartStop_NoErrors()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X1"), Polarity = ContactPolarity.NormallyClosed });
        logic.Add(new ParallelNetwork
        {
            Branches =
            {
                new SeriesNetwork { Elements = { new ContactNode { Device = DeviceAddress.Parse("X0") } } },
                new SeriesNetwork { Elements = { new ContactNode { Device = DeviceAddress.Parse("Y0") } } },
            },
        });
        logic.Add(new CoilNode { Device = DeviceAddress.Parse("Y0") });
        ValidationReport r = Validator().Validate(SingleRung(logic), Model);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void OutOfRangeDevice_Error()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X400") }); // 256 > max 255
        logic.Add(new CoilNode { Device = DeviceAddress.Parse("Y0") });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1003"));
    }

    [Fact]
    public void WritingToInput_IsError()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        logic.Add(new CoilNode { Device = DeviceAddress.Parse("X1") });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1004"));
    }

    [Fact]
    public void CoilOnTimerDevice_MustUseTimerBlock()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        logic.Add(new CoilNode { Device = DeviceAddress.Parse("T5") });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1005"));
    }

    [Fact]
    public void DoubleCoil_Warning()
    {
        var p = new PlcProject();
        var a = p.Program.AddRung(); a.Logic.Add(new CoilNode { Device = DeviceAddress.Parse("Y0") });
        var b = p.Program.AddRung(); b.Logic.Add(new CoilNode { Device = DeviceAddress.Parse("Y0") });
        ValidationReport r = Validator().Validate(p, Model);
        Assert.True(Has(r, "DS1014", DiagnosticSeverity.Warning));
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void UnknownInstruction_Rejected()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        logic.Add(new InstructionNode { Call = new InstructionCall { Mnemonic = "WARP DRIVE" } });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1006"));
    }

    [Fact]
    public void BadOperandShape_Rejected()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        var call = new InstructionCall { Mnemonic = "MOV" };
        call.Operands.Add(new DeviceOperand(DeviceAddress.Parse("X1"))); // source must be word
        call.Operands.Add(new DeviceOperand(DeviceAddress.Parse("X2"))); // target must be word
        logic.Add(new InstructionNode { Call = call });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1009"));
    }

    [Fact]
    public void ConstantOutOfRange_Rejected()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        var call = new InstructionCall { Mnemonic = "MOV" };
        call.Operands.Add(new ConstantOperand(99999));
        call.Operands.Add(new DeviceOperand(DeviceAddress.Parse("D10")));
        logic.Add(new InstructionNode { Call = call });
        Assert.True(Has(Validator().Validate(SingleRung(logic), Model), "DS1013"));
    }

    [Fact]
    public void EmptyRung_Errors_And_EmptyProgram_Warns()
    {
        var p = new PlcProject();
        p.Program.AddRung();
        ValidationReport r = Validator().Validate(p, Model);
        Assert.True(Has(r, "DS1010"));
    }

    [Fact]
    public void DuplicateSymbol_Rejected()
    {
        var p = SingleRung(new SeriesNetwork
        {
            Elements = { new CoilNode { Device = DeviceAddress.Parse("Y0") } },
        });
        p.Symbols.Add(new SymbolDefinition { Name = "A", Address = DeviceAddress.Parse("X0") });
        p.Symbols.Add(new SymbolDefinition { Name = "a", Address = DeviceAddress.Parse("X1") });
        Assert.True(Has(Validator().Validate(p, Model), "DS2001"));
    }

    [Fact]
    public void EncodingAlwaysFlaggedUnverified()
    {
        var logic = new SeriesNetwork();
        logic.Add(new ContactNode { Device = DeviceAddress.Parse("X0") });
        var call = new InstructionCall { Mnemonic = "MOV" };
        call.Operands.Add(new ConstantOperand(5));
        call.Operands.Add(new DeviceOperand(DeviceAddress.Parse("D0")));
        logic.Add(new InstructionNode { Call = call });
        ValidationReport r = Validator().Validate(SingleRung(logic), Model);
        Assert.True(Has(r, "DS1090", DiagnosticSeverity.Info)); // honesty marker present
    }
}
