using System.Buffers.Binary;
using System.Text;
using CPUSTATS.Services;
using Xunit;

namespace CPUSTATS.Tests;

public sealed class LovingCoolFeedTests
{
    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void UsesEmbeddedTimestampToRejectStaleReadings(int ageSeconds, bool accepted)
    {
        var sampledAt = new DateTimeOffset(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local));
        var json = Encoding.UTF8.GetBytes("""
            {"systemTime":"2026-01-02 03:04:05","cpuLayout":{"temperature_list":[{"sensorname":"CPU Package","value":42}]}}
            """);
        var feed = new byte[sizeof(int) + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(feed, json.Length);
        json.CopyTo(feed, sizeof(int));

        var snapshot = LovingCoolFeed.Parse(feed, sampledAt.AddSeconds(ageSeconds));

        Assert.Equal(accepted ? 42 : (int?)null, snapshot?.CpuTemperature);
    }
}
