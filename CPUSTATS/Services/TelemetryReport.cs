namespace CPUSTATS.Services;

public static class TelemetryReport
{
    public static bool IsValid(ReadOnlySpan<byte> report) => report.Length == L280Protocol.ReportLength;

    public static byte[] Encode(TelemetrySnapshot values, DisplayPage page = DisplayPage.Celsius, byte sequence = 0) =>
        page == DisplayPage.Usage
            ? L280Protocol.Encode(values.CpuUsage, values.GpuUsage, page, sequence)
            : L280Protocol.Encode(ConvertTemperature(values.CpuTemperature, page), ConvertTemperature(values.GpuTemperature, page),
                page, sequence, values.CpuTemperature, values.GpuTemperature);

    private static int? ConvertTemperature(int? celsius, DisplayPage page) =>
        page == DisplayPage.Fahrenheit && celsius is not null ? (int)Math.Floor(celsius.Value * 1.8 + 32) : celsius;
}
