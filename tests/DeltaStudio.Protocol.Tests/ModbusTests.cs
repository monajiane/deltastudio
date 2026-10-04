using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Modbus;
using DeltaStudio.Protocols.Transport;
using Xunit;

namespace DeltaStudio.Protocol.Tests;

public class ModbusFramingTests
{
    // Reference values independently computed from the MODBUS spec algorithm (poly 0xA001 reflected).
    [Theory]
    [InlineData("010300000001", "0A84")]
    [InlineData("11010013000A", "584F")]
    [InlineData("FF100000000204000A000B", "81A5")]
    [InlineData("0107000A", "1E30")]
    public void Crc16_KnownVectors(string hex, string expectedCrcLE)
    {
        byte[] data = Convert.FromHexString(hex);
        ushort crc = ModbusCrc.Compute(data);
        ushort expected = Convert.ToUInt16(expectedCrcLE, 16);
        Assert.Equal(expected, crc);

        byte[] frame = ModbusCrc.Append(data);
        Assert.True(ModbusCrc.Validate(frame));
        frame[^1] ^= 0xFF;
        Assert.False(ModbusCrc.Validate(frame));
    }

    [Fact]
    public void Ascii_EncodeDecode()
    {
        byte[] adu = [0x01, 0x03, 0x00, 0x00, 0x00, 0x01];
        byte[] frame = ModbusAsciiCodec.Encode(adu);
        string text = System.Text.Encoding.ASCII.GetString(frame);
        Assert.StartsWith(":010300000001", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);

        byte[]? back = ModbusAsciiCodec.Decode(frame);
        Assert.NotNull(back);
        Assert.Equal(adu, back);

        // corrupt LRC → reject
        byte[] bad = (byte[])frame.Clone();
        bad[^3] = bad[^3] == (byte)'F' ? (byte)'E' : (byte)'F';
        Assert.Null(ModbusAsciiCodec.Decode(bad));
    }
}

public class MockTransportTests
{
    [Fact]
    public async Task Timeout_OnDroppedResponse()
    {
        var plc = new Protocols.Mock.MockDvpPlc(new Protocols.Mock.MockPlcOptions());
        await using var transport = new Protocols.Mock.MockPlcTransport(plc) { DropNextResponse = true };
        await transport.ConnectAsync();
        var client = new ModbusClient(transport) { Station = 1 };
        await Assert.ThrowsAsync<TransportTimeoutException>(
            () => client.ReadHoldingRegistersAsync(0x1000, 1));
    }

    [Fact]
    public async Task WrongStation_Ignored()
    {
        var plc = new Protocols.Mock.MockDvpPlc(new Protocols.Mock.MockPlcOptions { Station = 2 });
        await using var transport = new Protocols.Mock.MockPlcTransport(plc);
        await transport.ConnectAsync();
        var client = new ModbusClient(transport) { Station = 1 }; // we ask station 1, mock is 2
        await Assert.ThrowsAsync<TransportTimeoutException>(
            () => client.ReadCoilsAsync(0, 1));
    }
}
