using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CPUSTATS.Controls;

internal sealed class UsageStrip : Control
{
    public static readonly StyledProperty<int?> ValueProperty =
        AvaloniaProperty.Register<UsageStrip, int?>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<UsageStrip, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<UsageStrip, IBrush?>(nameof(Track));

    static UsageStrip() => AffectsRender<UsageStrip>(ValueProperty, FillProperty, TrackProperty);

    public int? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public override void Render(DrawingContext context)
    {
        const int segments = 20;
        const double gap = 3;
        var width = (Bounds.Width - gap * (segments - 1)) / segments;
        if (width <= 0 || Bounds.Height <= 0) return;
        var filled = (int)Math.Ceiling(Math.Clamp(Value ?? 0, 0, 100) * segments / 100.0);
        for (var index = 0; index < segments; index++)
            context.DrawRectangle(index < filled ? Fill : Track, null,
                new Rect(index * (width + gap), 0, width, Bounds.Height), 1.5, 1.5);
    }
}
