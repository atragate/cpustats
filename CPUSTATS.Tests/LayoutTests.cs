using CPUSTATS.Models;
using CPUSTATS.Services;
using Xunit;

namespace CPUSTATS.Tests;

public sealed class LayoutTests
{
    [Fact]
    public async Task LayoutRoundTripsThroughStore()
    {
        var store = new LayoutStore();
        var layout = new DashboardLayout { Name = "test-layout", Width = 320, Height = 480 };
        layout.Widgets.Add(new DashboardWidget { Type = "text", Label = "CPU", Source = "cpu.usage" });
        await store.SaveAsync(layout);
        var loaded = await store.LoadAsync("test-layout");
        Assert.NotNull(loaded);
        Assert.Equal(320, loaded!.Width);
        Assert.Single(loaded.Widgets);
    }
}
