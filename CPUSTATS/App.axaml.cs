using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CPUSTATS.Services;

namespace CPUSTATS;

public sealed class App : Application
{
    private MainWindow? _window;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            window.Closing += HideWindowOnClose;
            _window = window;
            if (desktop.Args?.Contains("--tray") != true)
                desktop.MainWindow = window;
            var menu = CreateTrayMenu(window);
#if WINDOWS
            if (OperatingSystem.IsWindows() && WindowsTray.TryCreate(window, menu,
                () => ShowWindow(null, EventArgs.Empty)) is { } nativeTray)
            {
                desktop.Exit += (_, _) =>
                {
                    if (OperatingSystem.IsWindows()) nativeTray.Dispose();
                };
            }
            else
#endif
            {
                var tray = new TrayIcon { Menu = menu, Icon = window.Icon, ToolTipText = "CPUSTATS" };
                tray.Clicked += ShowWindow;
                TrayIcon.SetIcons(this, new TrayIcons { tray });
                window.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Window.IconProperty)
                        tray.Icon = window.Icon;
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private NativeMenu CreateTrayMenu(MainWindow window)
    {
        var open = new NativeMenuItem("Open CPUSTATS");
        open.Click += ShowWindow;

        var modes = new NativeMenu();
        var modeItems = new Dictionary<CaseDisplayMode, NativeMenuItem>();
        foreach (var (mode, label) in new (CaseDisplayMode, string)[]
        {
            (CaseDisplayMode.Alternate, "Alternate temperature and usage"),
            (CaseDisplayMode.Celsius, "Temperature (°C)"),
            (CaseDisplayMode.Fahrenheit, "Temperature (°F)"),
            (CaseDisplayMode.Usage, "Usage (%)")
        })
        {
            var item = new NativeMenuItem(label) { ToggleType = NativeMenuItemToggleType.Radio };
            item.Click += (_, _) => window.SelectCaseDisplayMode(mode);
            modeItems.Add(mode, item);
            modes.Add(item);
        }

        var pause = new NativeMenuItem();
        pause.Click += async (_, _) => await window.TogglePanelUpdatesAsync();
        var reconnect = new NativeMenuItem("Reconnect case display");
        reconnect.Click += async (_, _) => await window.ReconnectAsync();
        var quit = new NativeMenuItem("Quit CPUSTATS");
        quit.Click += Quit;

        void UpdateMenu()
        {
            foreach (var (mode, item) in modeItems)
                item.IsChecked = mode == window.SelectedCaseDisplayMode;
            pause.Header = window.PanelUpdatesPaused ? "Resume panel updates" : "Pause panel updates";
            reconnect.IsEnabled = !window.PanelUpdatesPaused;
        }

        window.PanelOptionsChanged += (_, _) => UpdateMenu();
        UpdateMenu();

        return new NativeMenu
        {
            open,
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Case display mode") { Menu = modes },
            pause,
            reconnect,
            new NativeMenuItemSeparator(),
            quit
        };
    }

    private static void HideWindowOnClose(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason != WindowCloseReason.WindowClosing || sender is not Window window)
            return;

        e.Cancel = true;
        window.Hide();
    }

    internal void ShowWindow(object? sender, EventArgs e)
    {
        if (_window is not { } window || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        desktop.MainWindow = window;

        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void Quit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow ??= _window;
            desktop.Shutdown();
        }
    }
}
