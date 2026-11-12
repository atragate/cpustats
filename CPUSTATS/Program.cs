using Avalonia;

namespace CPUSTATS;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var displayOwner = new Mutex(false, "L280Studio.DisplayOwner", out var firstInstance);
        if (!firstInstance)
        {
            Console.Error.WriteLine("CPUSTATS is already running.");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
