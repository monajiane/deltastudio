using DeltaStudio.Core.Devices;
using Xunit;

namespace DeltaStudio.Core.Tests;

public class DeviceAddressTests
{
    [Theory]
    [InlineData("X0", DeviceKind.X, 0)]
    [InlineData("X7", DeviceKind.X, 7)]
    [InlineData("X10", DeviceKind.X, 8)]      // octal!
    [InlineData("x17", DeviceKind.X, 15)]
    [InlineData("Y377", DeviceKind.Y, 255)]
    [InlineData("M0", DeviceKind.M, 0)]
    [InlineData("M100", DeviceKind.M, 100)]   // decimal
    [InlineData("D4096", DeviceKind.D, 4096)]
    [InlineData("T255", DeviceKind.T, 255)]
    [InlineData(" C21 ", DeviceKind.C, 21)]
    public void Parses(string text, DeviceKind kind, int number)
    {
        Assert.True(DeviceAddress.TryParse(text, out DeviceAddress a, out _));
        Assert.Equal(kind, a.Kind);
        Assert.Equal(number, a.Number);
    }

    [Theory]
    [InlineData("X8")]     // invalid octal digit
    [InlineData("X18")]
    [InlineData("Y99")]
    [InlineData("K5")]     // constants are not devices
    [InlineData("H1F")]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("Q0")]      // unknown prefix
    public void Rejects(string text)
    {
        Assert.False(DeviceAddress.TryParse(text, out _, out string? err));
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData(DeviceKind.X, 8, "X10")]
    [InlineData(DeviceKind.X, 15, "X17")]
    [InlineData(DeviceKind.Y, 255, "Y377")]
    [InlineData(DeviceKind.M, 4095, "M4095")]
    [InlineData(DeviceKind.D, 0, "D0")]
    public void FormatsAsDeltaManuals(DeviceKind kind, int number, string text)
        => Assert.Equal(text, new DeviceAddress(kind, number).ToString());

    [Fact]
    public void EqualityAndDictKey()
    {
        var a = DeviceAddress.Parse("X10");
        var b = new DeviceAddress(DeviceKind.X, 8);
        Assert.Equal(a, b);
        var set = new HashSet<DeviceAddress> { a };
        Assert.Contains(b, set);
    }
}
