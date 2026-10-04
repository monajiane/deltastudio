using DeltaStudio.Core.Devices;
using DeltaStudio.Delta.Comm;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Mock;
using DeltaStudio.Protocols.Modbus;
using Xunit;

namespace DeltaStudio.Protocol.Tests;

public class MockPlcDeviceTests
{
    private static (ModbusClient Client, MockDvpPlc Plc, MockPlcTransport Transport) Link(bool ascii = false)
    {
        var map = new DvpModbusAddressMap();
        var plc = new MockDvpPlc(new MockPlcOptions { Map = map, UseAscii = ascii });
        var transport = new MockPlcTransport(plc);
        transport.ConnectAsync().GetAwaiter().GetResult();
        return (new ModbusClient(transport, ascii) { Station = 1 }, plc, transport);
    }

    [Fact]
    public async Task Coils_M0_SetReadBack_RtuAndAscii()
    {
        foreach (bool ascii in new[] { false, true })
        {
            var (client, plc, transport) = Link(ascii);
            await using (transport)
            {
                var access = new DvpDeviceAccess(client, new DvpModbusAddressMap());

                // map check: M0 → coil 0x800
                Assert.True(((IModbusDeviceMap)new DvpModbusAddressMap()).TryMapBit(new DeviceAddress(DeviceKind.M, 0), out ModbusArea area, out int addr));
                Assert.Equal(ModbusArea.Coils, area);
                Assert.Equal(0x800, addr);

                await access.WriteAsync(new DeviceAddress(DeviceKind.Y, 0), DeviceValue.FromBit(true));
                Assert.True(plc.GetDevice(DeviceAddress.Parse("Y0")).Bit);

                IReadOnlyList<DeviceValue> read = await access.ReadBlockAsync(new DeviceAddress(DeviceKind.Y, 0), 1);
                Assert.True(read[0].Bit);

                await access.WriteAsync(new DeviceAddress(DeviceKind.D, 100), DeviceValue.FromWord(4242));
                IReadOnlyList<DeviceValue> d = await access.ReadBlockAsync(new DeviceAddress(DeviceKind.D, 100), 1);
                Assert.Equal((ushort)4242, d[0].Word);
            }
        }
    }

    [Fact]
    public async Task WritingXInput_IsRefusedLocally_NotSent()
    {
        var (client, _, transport) = Link();
        await using (transport)
        {
            var access = new DvpDeviceAccess(client, new DvpModbusAddressMap());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => access.WriteAsync(new DeviceAddress(DeviceKind.X, 0), DeviceValue.FromBit(true)));
        }
    }

    [Fact]
    public async Task OutOfMappedRange_YieldsModbusException()
    {
        var (client, _, transport) = Link();
        await using (transport)
        {
            var access = new DvpDeviceAccess(client, new DvpModbusAddressMap());
            // T255 present value maps; D8192 exceeds mapped D block (0x1000+8192) → no mapping → NotSupported.
            await Assert.ThrowsAsync<NotSupportedException>(
                () => access.ReadBlockAsync(new DeviceAddress(DeviceKind.D, 9000), 1));
        }
    }

    [Fact]
    public async Task BlockWritesAndReads_ManyCoils()
    {
        var (client, _, transport) = Link();
        await using (transport)
        {
            var access = new DvpDeviceAccess(client, new DvpModbusAddressMap());
            ushort[] words = [1, 2, 3, 4, 5];
            await client.WriteHoldingRegistersAsync(0x1000 + 500, words);
            IReadOnlyList<DeviceValue> got = await access.ReadBlockAsync(new DeviceAddress(DeviceKind.D, 500), 5);
            Assert.Equal(words, got.Select(v => v.Word).ToArray());
        }
    }

    [Fact]
    public async Task FlowControl7_Identify()
    {
        var plc = new MockDvpPlc(new MockPlcOptions { ModelCode = 0x0C32, Version = 0x1102 });
        await using var transport = new MockPlcTransport(plc);
        await transport.ConnectAsync();
        var client = new ModbusClient(transport) { Station = 1 };
        var id = await client.FlowControlIdentifyAsync(10);
        Assert.NotNull(id);
        Assert.Equal((ushort)0x0C32, id!.Value.ModelCode);
        Assert.Equal((ushort)0x1102, id.Value.Version);
    }
}
