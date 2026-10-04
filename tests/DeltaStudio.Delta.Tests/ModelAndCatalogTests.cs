using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Model;
using DeltaStudio.Delta.Comm;
using DeltaStudio.Delta.Instructions;
using DeltaStudio.Delta.Models;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Modbus;
using Xunit;

namespace DeltaStudio.Delta.Tests;

public class ModelCatalogTests
{
    [Theory]
    [InlineData("DVP14SS2T")]
    [InlineData("DVP14SS211R")]
    [InlineData("DVP12SS211S")]
    [InlineData("DVP28SS211T")]
    [InlineData("DVP16SA211R")]
    [InlineData("DVP16SE211T")]
    [InlineData("DVP20SV210T")]
    [InlineData("DVP16SX211T")]
    public void SpecifiedModelsExist(string id)
    {
        Assert.True(DvpModelCatalog.TryGet(id, out PlcModelDefinition? m));
        Assert.NotNull(m);
    }

    [Fact]
    public void Dvp14ss2t_HasDocumentedIoCounts()
    {
        Assert.True(DvpModelCatalog.TryGet("DVP14SS2T", out var m));
        // DVP-14SS2: 8 inputs (X0..X7), 6 outputs (Y0..Y5) per datasheet — octal display ⇒ X7 is index 7.
        DeviceCapabilities x = m!.Devices[DeviceKind.X];
        Assert.True(x.Contains(new DeviceAddress(DeviceKind.X, 7)));
        DeviceCapabilities y = m.Devices[DeviceKind.Y];
        Assert.True(y.Contains(new DeviceAddress(DeviceKind.Y, 5)));
    }

    [Fact]
    public void Ss2_ProgramCapacityIs8kSteps()
    {
        Assert.True(DvpModelCatalog.TryGet("DVP14SS2T", out var m));
        Assert.Equal(8192, m!.ProgramCapacitySteps);
    }

    [Fact]
    public void Sa2_ClassProgramCapacityIs16kSteps()
    {
        Assert.True(DvpModelCatalog.TryGet("DVP16SA211R", out var m));
        Assert.Equal(16384, m!.ProgramCapacitySteps);
    }

    [Fact]
    public void EveryModelCarriesVerificationProvenance()
    {
        foreach (PlcModelDefinition m in DvpModelCatalog.All.Values)
        {
            Assert.NotNull(m.Source);
            Assert.True(m.Verification is CapabilityStatus.Partial or CapabilityStatus.Experimental,
                $"model {m.Id} must not claim full verification without hardware checks");
        }
    }

    [Fact]
    public void TimerRanges_MatchManualBands()
    {
        Assert.True(DvpModelCatalog.TryGet("DVP14SS2T", out var m));
        DeviceCapabilities t = m!.Devices[DeviceKind.T];
        Assert.Contains(t.Ranges, r => r.Min == 127 && r.Max == 127);   // the 1 ms general timer
        Assert.Contains(t.Ranges, r => r.Min == 246 && r.Max == 249);   // accumulative
        Assert.Equal(DeviceAccess.ContactAndValue, t.Access);
    }
}

public class InstructionCatalogTests
{
    private static readonly DvpInstructionCatalog Catalog = new();

    [Theory]
    [InlineData("LD")] [InlineData("MOV")] [InlineData("DMOV")] [InlineData("ADD")] [InlineData("CMP")]
    [InlineData("ZRST")] [InlineData("TMR")] [InlineData("CTR")] [InlineData("PLSY")] [InlineData("DRVI")]
    [InlineData("FROM")] [InlineData("TO")] [InlineData("SET")] [InlineData("RST")] [InlineData("OUT")]
    public void KnownMnemonics(string m) => Assert.True(Catalog.TryGet(m, out _), m);

    [Theory]
    [InlineData("NOPQ")] [InlineData("ENCO")] [InlineData("DECO")] [InlineData("TOFF")] [InlineData("SEAL")]
    public void UnknownMnemonics_AbsentNotGuessed(string m) => Assert.False(Catalog.TryGet(m, out _), m);

    [Fact]
    public void Mov_ShapeFromManual()
    {
        Assert.True(Catalog.TryGet("MOV", out var def));
        Assert.Equal(2, def!.Operands.Count);
        Assert.Equal(12, def.Fnc); // FNC12 MOV
        Assert.True(def.HasDoubleVariant);
    }

    [Fact]
    public void NothingClaimsEncodingVerified()
    {
        Assert.All(Catalog.All, def => Assert.False(def.EncodingVerified));
    }

    [Fact]
    public void CaseInsensitive() => Assert.True(Catalog.TryGet("mov", out _));
}

public class ModbusMapTests
{
    private static readonly DvpModbusAddressMap Map = new();

    [Theory]
    [InlineData("S0", ModbusArea.Coils, 0x0000)]
    [InlineData("Y0", ModbusArea.Coils, 0x0500)]
    [InlineData("Y1", ModbusArea.Coils, 0x0501)]
    [InlineData("T0", ModbusArea.Coils, 0x0600)]        // contact view
    [InlineData("M0", ModbusArea.Coils, 0x0800)]
    [InlineData("M2000", ModbusArea.Coils, 0x0C00 + 976)]
    [InlineData("C0", ModbusArea.Coils, 0x0E00)]
    [InlineData("X0", ModbusArea.DiscreteInputs, 0x0400)]
    [InlineData("X10", ModbusArea.DiscreteInputs, 0x0400 + 8)] // octal X10 = index 8
    public void BitMapping(string device, ModbusArea area, int addr)
    {
        Assert.True(Map.TryMapBit(DeviceAddress.Parse(device), out ModbusArea a, out int ad));
        Assert.Equal(area, a);
        Assert.Equal(addr, ad);
    }

    [Theory]
    [InlineData("D0", 0x1000)]
    [InlineData("D100", 0x1064)]
    [InlineData("T5", 0x0600 + 5)]
    [InlineData("C3", 0x0E00 + 3)]
    public void WordMapping(string device, int addr)
    {
        Assert.True(Map.TryMapWord(DeviceAddress.Parse(device), out ModbusArea a, out int ad));
        Assert.Equal(ModbusArea.HoldingRegisters, a);
        Assert.Equal(addr, ad);
    }

    [Fact]
    public void NoOverlap_BetweenCounterValuesAndD()
    {
        // C0..C255 present values occupy 0x0E00..0x0EFF; D0 starts at 0x1000.
        Assert.True(Map.TryMapWord(new DeviceAddress(DeviceKind.C, 255), out _, out int cMax));
        Assert.True(Map.TryMapWord(DeviceAddress.Parse("D0"), out _, out int d0));
        Assert.True(d0 > cMax);
    }

    [Fact]
    public void ReverseMapping_WorksForMock()
    {
        Assert.True(Map.TryResolveBit(ModbusArea.Coils, 0x0502, out DeviceAddress y));
        Assert.Equal(DeviceAddress.Parse("Y2"), y);
        Assert.True(Map.TryResolveWord(ModbusArea.HoldingRegisters, 0x1005, out DeviceAddress d));
        Assert.Equal(DeviceAddress.Parse("D5"), d);
    }

    [Fact]
    public void WritePermission_Matrix()
    {
        Assert.False(Map.IsWritable(DeviceAddress.Parse("X0")));
        Assert.True(Map.IsWritable(DeviceAddress.Parse("Y0")));
        Assert.True(Map.IsWritable(DeviceAddress.Parse("M50")));
        Assert.True(Map.IsWritable(DeviceAddress.Parse("D10")));
        Assert.False(Map.IsWritable(DeviceAddress.Parse("T0"))); // contact view; value handled via word map
        Assert.Equal(ProtocolStatus.PartiallySupported, DvpModbusAddressMap.VerificationStatus);
    }
}

public class ProgrammingProtocolPolicyTests
{
    [Fact]
    public void StatusIsHonestlyNotImplemented()
    {
        IDeltaProgrammingProtocol p = new DeltaProgrammingProtocol();
        Assert.Equal(ProtocolStatus.NotImplemented, p.Status);
        var ex = Assert.ThrowsAny<NotSupportedException>(() => p.DownloadProgramAsync(new byte[4]).GetAwaiter().GetResult());
        Assert.Contains("NOT_IMPLEMENTED", ex.Message);
    }

    [Fact]
    public async Task AllOperationsRefuse()
    {
        IDeltaProgrammingProtocol p = new DeltaProgrammingProtocol();
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => p.UploadProgramAsync());
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => p.RunAsync());
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => p.StopAsync());
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => p.DownloadProgramAsync(new byte[16]));
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => p.ReadChecksumAsync());
    }
}
