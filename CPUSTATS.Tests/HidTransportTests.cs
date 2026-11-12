using CPUSTATS.Services;
using Xunit;

namespace CPUSTATS.Tests;

public sealed class HidTransportTests
{
    [Fact]
    public void AcceptsOnlyTheKnownL280ReportSize()
    {
        Assert.True(TelemetryReport.IsValid(new byte[65]));
        Assert.False(TelemetryReport.IsValid(new byte[64]));
        Assert.False(TelemetryReport.IsValid(new byte[66]));
    }

    [Theory]
    [InlineData(0, 42, 38, "A005B600320274027601660574000000")]
    [InlineData(0, 27, 64, "9204A4009602720352017405B2000000")]
    [InlineData(0, 0, 100, "BC04B400B4028803B401B405B4000000")]
    [InlineData(0, 123, 999, "DA05FD01F603FE03FA01EC05FF000000")]
    [InlineData(1, 42, 38, "A005B602320074007603660574000000")]
    [InlineData(2, 42, 38, "A007B604320474047605660774000000")]
    [InlineData(2, 27, 64, "9206A4049604720552057407B2000000")]
    [InlineData(2, 0, 100, "BC06B404B4048805B405B407B4000000")]
    public void MatchesVendorSegmentEncoder(int mode, int cpu, int gpu, string expected)
    {
        var report = L280Protocol.Encode(cpu, gpu, (DisplayPage)mode, 254);
        Assert.Equal(65, report.Length);
        Assert.Equal(new byte[] { 0, 254, 0, 2 }, report[..4]);
        Assert.Equal(expected, Convert.ToHexString(report.AsSpan(41,16)));
    }

    [Theory]
    [InlineData(0, DisplayPage.Celsius)]
    [InlineData(5.999, DisplayPage.Celsius)]
    [InlineData(6, DisplayPage.Usage)]
    [InlineData(11.999, DisplayPage.Usage)]
    [InlineData(12, DisplayPage.Celsius)]
    public void RotatesEverySixSeconds(double seconds, DisplayPage expected) =>
        Assert.Equal(expected, L280Protocol.PageAt(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(3, 2.999, DisplayPage.Celsius)]
    [InlineData(3, 3, DisplayPage.Usage)]
    [InlineData(3, 6, DisplayPage.Celsius)]
    [InlineData(15, 14.999, DisplayPage.Celsius)]
    [InlineData(15, 15, DisplayPage.Usage)]
    [InlineData(15, 30, DisplayPage.Celsius)]
    public void UsesTheSelectedDisplayInterval(int interval, double seconds, DisplayPage expected) =>
        Assert.Equal(expected, L280Protocol.PageAt(TimeSpan.FromSeconds(seconds), interval));

    [Fact]
    public void FahrenheitReportUsesCelsiusForTheBars()
    {
        var snapshot = new TelemetrySnapshot(42, 15, 38, 20, 50);
        var report = TelemetryReport.Encode(snapshot, DisplayPage.Fahrenheit, 254);

        Assert.Equal(L280Protocol.Encode(107, 100, DisplayPage.Fahrenheit, 254, 42, 38), report);
    }

    [Fact]
    public void MissingTemperaturesAreDifferentFromZero()
    {
        Assert.NotEqual(L280Protocol.Encode(0,0,DisplayPage.Celsius,0), L280Protocol.Encode(null,null,DisplayPage.Celsius,0));
    }
}
