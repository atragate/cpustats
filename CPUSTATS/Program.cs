using Avalonia;

namespace CPUSTATS;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows() && args is ["--sensor-service"])
        {
            System.ServiceProcess.ServiceBase.Run(new Services.WindowsSensorService());
            return;
        }
#endif
        using var displayOwner = new Mutex(false, "L280Studio.DisplayOwner", out var firstInstance);
#if WINDOWS
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, "CPUSTATS.ShowWindow");
        if (!firstInstance)
        {
            if (!args.Contains("--tray")) showWindow.Set();
            return;
        }

        var showRequest = ThreadPool.RegisterWaitForSingleObject(showWindow,
            (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowWindow(null, EventArgs.Empty)),
            null, Timeout.Infinite, false);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { showRequest.Unregister(null); }
#else
        if (firstInstance) BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#endif
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
