using System.Text.Json;
using CPUSTATS.Services;
using Xunit;

namespace CPUSTATS.Tests;

public sealed class LinuxIntelGpuTemperatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);

    private static string Sample(long age = 0, int? temperature = 34) => JsonSerializer.Serialize(new
    {
        version = 1, pciAddress = "0000:00:02.0", vendorId = "0x8086", deviceId = "0x7d67",
        sampledAtUnixMs = Now.ToUnixTimeMilliseconds() - age, celsius = temperature
    });

    private static int? Read(string json, string pci = "0000:00:02.0", string device = "0x7d67") =>
        LinuxIntelGpuTemperature.Parse(json, pci, "0x8086", device, Now);

    [Fact]
    public void AcceptsFreshMatchingSensor() => Assert.Equal(34, Read(Sample()));

    [Theory]
    [InlineData(5001)]
    [InlineData(-1000)]
    public void RejectsStaleOrFutureSamples(long age) => Assert.Null(Read(Sample(age)));

    [Fact]
    public void RejectsOtherGpu()
    {
        Assert.Null(Read(Sample(), pci: "0000:03:00.0"));
        Assert.Null(Read(Sample(), device: "0x1234"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(126)]
    public void RejectsUnavailableOrInvalidTemperature(int? temperature) => Assert.Null(Read(Sample(temperature: temperature)));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"version\":\"wrong\"}")]
    public void RejectsMalformedSample(string json) => Assert.Null(Read(json));
}
