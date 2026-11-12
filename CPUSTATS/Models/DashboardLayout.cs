namespace CPUSTATS.Models;

public sealed class DashboardLayout
{
    public string Name { get; set; } = "L280 Dashboard";
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 480;
    public string Background { get; set; } = "#10141C";
    public List<DashboardWidget> Widgets { get; set; } = [];
}

public sealed class DashboardWidget
{
    public string Type { get; set; } = "text";
    public string Label { get; set; } = "CPU";
    public string Source { get; set; } = "cpu.usage";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 120;
    public int Height { get; set; } = 40;
    public string Color { get; set; } = "#66E3FF";
}
