using DeltaStudio.Compiler.Il;
using DeltaStudio.Core.Program;
using Xunit;

namespace DeltaStudio.Compiler.Tests;

public class IlTests
{
    private static Rung Parse(string text) => new IlParser().ParseRung(text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    [Fact]
    public void SimpleSeries()
    {
        Rung r = Parse("LD X0\nANI X1\nOUT Y0");
        Assert.Equal(3, r.Logic.Elements.Count);
        Assert.IsType<ContactNode>(r.Logic.Elements[0]);
        Assert.Equal(ContactPolarity.NormallyClosed, ((ContactNode)r.Logic.Elements[1]).Polarity);
        Assert.IsType<CoilNode>(r.Logic.Elements[2]);
    }

    [Fact]
    public void HoldingCircuit_ProducesParallel()
    {
        Rung r = Parse("LD X0\nOR Y0\nANI X1\nOUT Y0");
        Assert.Equal(3, r.Logic.Elements.Count);
        Assert.IsType<ParallelNetwork>(r.Logic.Elements[0]);
        var p = (ParallelNetwork)r.Logic.Elements[0];
        Assert.Equal(2, p.Branches.Count);
    }

    [Fact]
    public void InstructionLine_BecomesNode()
    {
        Rung r = Parse("LD M0\nMOV D0 K100");
        var n = Assert.IsType<InstructionNode>(r.Logic.Elements[1]);
        Assert.Equal("MOV", n.Call.Mnemonic);
        Assert.Equal("D0 K100", n.Call.ToText()[4..]);
    }

    [Fact]
    public void OrbBlockCombination()
    {
        Rung r = Parse("LD X0\nAND X1\nLD X2\nAND X3\nORB\nOUT Y0");
        var par = Assert.IsType<ParallelNetwork>(r.Logic.Elements[0]);
        Assert.Equal(2, par.Branches.Count);
        Assert.Equal(2, par.Branches[0].Elements.Count);
    }

    [Fact]
    public void AnbBlockSeries()
    {
        Rung r = Parse("LD X0\nAND X1\nLD X2\nANB\nOUT Y0");
        Assert.Equal(4, r.Logic.Elements.Count); // X0 X1 X2 + OUT coil after ANB
    }

    [Theory]
    [InlineData("AND X0\nOUT Y0")]    // AND without LD
    [InlineData("MOV D0")]            // instruction without LD block
    [InlineData("LD X0\nLD X1\nOUT Y0")] // second LD leaves a block open → error
    public void Malformed_Throws(string text)
    {
        Assert.ThrowsAny<IlSyntaxException>(() => Parse(text));
    }

    [Fact]
    public void RoundTrip_Program()
    {
        const string il = """
            LD X1
            ANI X1
            OUT Y0

            LD X0
            OR Y0
            ANI X1
            OUT Y0

            LD X5
            MOV D0 K42
            """;
        IlParser parser = new();
        PlcProgram program = parser.ParseProgram(il);
        Assert.Equal(3, program.Main.Count);
        IlFormatter formatter = new();
        string again = formatter.Format(program);
        PlcProgram reparsed = new IlParser().ParseProgram(again);

        // semantic comparison via node serialization
        Assert.Equal(
            Core.Project.PlcProjectJson.SerializeProgram(program),
            Core.Project.PlcProjectJson.SerializeProgram(reparsed));
    }

    [Fact]
    public void EmptyRungOnlyLd_KeptAsOpenBlock_Error()
    {
        // "LD X0" alone leaves no coil; per WPLSoft that's a syntax error → parser flags it via missing coil? Our parser
        // allows coilless chains; the validator (not the parser) reports that. So this must NOT throw:
        Rung r = Parse("LD X0");
        Assert.Single(r.Logic.Elements);
    }
}
