using CPUSTATS.Services;
using Xunit;

namespace CPUSTATS.Tests;

public sealed class IntelGtTemperatureTests
{
    [Fact]
    public void DecodesVerifiedArrowLakeRegisterSample()
    {
        Assert.Equal(34, IntelGtTemperature.Decode(0x80691480, 0x88470000));
    }

    [Theory]
    [InlineData(0x80691480u, 0x08470000u)] // No valid reading
    [InlineData(0x80691480u, 0x886a0000u)] // Delta exceeds TjMax
    [InlineData(0xffffffffu, 0x88470000u)] // Unreadable target
    [InlineData(0x80691480u, 0xffffffffu)] // Unreadable status
    [InlineData(0x80001480u, 0x88470000u)] // Implausible TjMax
    public void RejectsInvalidRegisters(uint target, uint status)
    {
        Assert.Null(IntelGtTemperature.Decode(target, status));
    }
}
