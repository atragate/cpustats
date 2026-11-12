namespace CPUSTATS.Services;

public enum DisplayPage { Celsius, Fahrenheit, Usage }

/// <summary>L280 segmented-panel output, verified against the supplied PC Monitor encoder.</summary>
public static class L280Protocol
{
    public const int ReportLength = 65;
    public const int SegmentOffset = 41; // report ID + payload offset 40
    // Standard seven-segment masks in a,b,c,d,e,f,g order.
    private static readonly byte[] Digits = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
    // RAM byte indices for a,b,c,d,e,f,g, for CPU hundreds/tens/ones then GPU.
    private static readonly int[][] Wiring = {
        new[] { 0,2,12,10,8,4,6 }, new[] { 12,4,2,6,0,10,8 }, new[] { 2,10,4,12,8,0,6 },
        new[] { 10,0,6,8,4,12,2 }, new[] { 4,2,12,8,10,0,6 }, new[] { 8,10,2,4,0,12,6 }
    };
    private static readonly (int Index, byte Mask)[] CpuBars = {
        (6,64),(8,64),(10,64),(12,64),(1,1),(5,1),(3,1),(2,64),(4,64),(0,64)
    };
    private static readonly (int Index, byte Mask)[] GpuBars = {
        (9,1),(11,1),(0,128),(2,128),(4,128),(7,1),(12,128),(10,128),(8,128),(6,128)
    };

    public const int DefaultRotationSeconds = 6;

    public static DisplayPage PageAt(TimeSpan elapsed, int rotationSeconds = DefaultRotationSeconds) =>
        (long)Math.Max(0, elapsed.TotalSeconds) / Math.Max(1, rotationSeconds) % 2 == 0
            ? DisplayPage.Celsius : DisplayPage.Usage;

    public static byte[] Encode(int? cpu, int? gpu, DisplayPage page, byte sequence,
        int? cpuBarValue = null, int? gpuBarValue = null)
    {
        var report = new byte[ReportLength];
        report[1] = sequence;
        report[3] = 2; // vendor display-update command
        // Compatibility bank emitted by the L280 utility; the case's bank follows it.
        Convert.FromHexString("FD080808FD08DD0FD0DF8F0000000000").CopyTo(report, 25);
        var panel = report.AsSpan(SegmentOffset, 16);
        panel[1] |= 4; // CPU label
        panel[11] |= 4; // GPU label
        switch (page)
        {
            case DisplayPage.Celsius: panel[5] |= 2; panel[7] |= 2; break;
            case DisplayPage.Fahrenheit: panel[3] |= 2; panel[9] |= 2; break;
            case DisplayPage.Usage:
                panel[1] |= 2; panel[11] |= 2; // percent signs
                panel[5] |= 4; panel[3] |= 4; panel[9] |= 4; panel[7] |= 4;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(page));
        }
        DrawNumber(panel, cpu, 0);
        DrawNumber(panel, gpu, 3);
        DrawBars(panel, cpuBarValue ?? cpu, CpuBars);
        DrawBars(panel, gpuBarValue ?? gpu, GpuBars);
        return report;
    }

    private static void DrawNumber(Span<byte> panel, int? value, int firstDigit)
    {
        if (value is null)
        {
            // Three dashes mean unavailable; do not fabricate a zero temperature.
            for (var n = firstDigit; n < firstDigit + 3; n++) DrawDigit(panel, n, 0x40);
            return;
        }
        var number = Math.Clamp(value.Value, 0, 999);
        if (number >= 100) DrawDigit(panel, firstDigit, Digits[number / 100]);
        if (number >= 10) DrawDigit(panel, firstDigit + 1, Digits[number / 10 % 10]);
        DrawDigit(panel, firstDigit + 2, Digits[number % 10]);
    }

    private static void DrawDigit(Span<byte> panel, int position, byte segments)
    {
        for (var bit = 0; bit < 7; bit++)
            if ((segments & (1 << bit)) != 0)
                panel[Wiring[position][bit]] |= (byte)(1 << position);
    }

    private static void DrawBars(Span<byte> panel, int? value, (int Index, byte Mask)[] bars)
    {
        var count = (Math.Clamp(value ?? 0, 0, 100) + 9) / 10;
        foreach (var (index, mask) in bars.Take(count)) panel[index] |= mask;
    }
}
