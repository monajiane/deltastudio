using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Program;
using DeltaStudio.Core.Project;
using Xunit;

namespace DeltaStudio.Core.Tests;

public class ProjectRoundTripTests
{
    private static PlcProject Sample()
    {
        var p = new PlcProject { Target = new PlcTarget { ModelId = "DVP14SS2T", ModbusStation = 2 } };
        p.Metadata.Name = "RoundTrip";
        Rung r = p.Program.AddRung("start/stop");
        r.Logic
            .Add(new ContactNode { Device = DeviceAddress.Parse("X1"), Polarity = ContactPolarity.NormallyClosed })
            .Add(new ParallelNetwork
            {
                Branches =
                {
                    new SeriesNetwork { Elements = { new ContactNode { Device = DeviceAddress.Parse("X0") } } },
                    new SeriesNetwork { Elements = { new ContactNode { Device = DeviceAddress.Parse("Y0") } } },
                },
            })
            .Add(new CoilNode { Device = DeviceAddress.Parse("Y0") });
        Rung r2 = p.Program.AddRung("timer");
        var tmr = new InstructionCall { Mnemonic = "TMR" };
        tmr.Operands.Add(new DeviceOperand(DeviceAddress.Parse("T0")));
        tmr.Operands.Add(new ConstantOperand(50));
        r2.Logic.Add(new InstructionNode { Call = tmr });
        p.Symbols.Add(new SymbolDefinition { Name = "Start", Address = DeviceAddress.Parse("X0"), Comment = "NO button" });
        return p;
    }

    [Fact]
    public async Task SaveOpen_PreservesSemantics()
    {
        string dir = Path.Combine(Path.GetTempPath(), "deltastudio-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            PlcProject original = Sample();
            await ProjectFileIO.SaveAsync(dir, original);
            Assert.True(File.Exists(Path.Combine(dir, "project.json")));
            Assert.True(File.Exists(Path.Combine(dir, "program", "main.json")));
            Assert.True(File.Exists(Path.Combine(dir, "symbols.json")));

            PlcProject reloaded = await ProjectFileIO.OpenAsync(dir);
            Assert.Equal(original.Metadata.Name, reloaded.Metadata.Name);
            Assert.Equal(2, reloaded.Target.ModbusStation);
            Assert.Equal(original.Program.Main.Count, reloaded.Program.Main.Count);
            Assert.Equal("start/stop", reloaded.Program.Main[0].Comment);

            // structural comparison: serialize both, compare node text
            string a = PlcProjectJson.SerializeProgram(original.Program);
            string b = PlcProjectJson.SerializeProgram(reloaded.Program);
            Assert.Equal(a, b);

            Assert.Single(reloaded.Symbols);
            Assert.Equal(DeviceAddress.Parse("X0"), reloaded.Symbols[0].Address);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SchemaVersionGuard()
    {
        const string bad = "{\"schemaVersion\":99,\"rungs\":[]}";
        Assert.Throws<NotSupportedException>(() => PlcProjectJson.DeserializeProgram(bad));
    }

    [Fact]
    public void UnknownNodeKindRejected()
    {
        const string bad = "{\"schemaVersion\":1,\"rungs\":[{\"number\":1,\"logic\":{\"type\":\"weird\"}}]}";
        Assert.Throws<FormatException>(() => PlcProjectJson.DeserializeProgram(bad));
    }
}
