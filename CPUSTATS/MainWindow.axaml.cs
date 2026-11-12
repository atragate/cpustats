using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CPUSTATS.Services;

namespace CPUSTATS;

public sealed partial class MainWindow : Window
{
    private readonly HidTransport _transport = new();
    private readonly TelemetryService _telemetry = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions StatusJson = new() { WriteIndented = true };
    private AppPreferences _preferences = AppPreferences.Load();
    private TelemetrySnapshot _current = new(null, null, null, null, null);
    private TimeSpan _lastSample = TimeSpan.FromSeconds(-2);
    private TimeSpan _rotationStartedAt;
    private TimeSpan _retryAt;
    private byte _sequence;
    private bool _busy, _closed, _settingsReady;
    private string _transportStatus = "Waiting for first USB write";
    private Bitmap? _brandBitmap;
    private string? _brandVariant;

    internal CaseDisplayMode SelectedCaseDisplayMode => _preferences.DisplayMode;
    internal bool PanelUpdatesPaused { get; private set; }
    internal event EventHandler? PanelOptionsChanged;

    private DisplayPage CurrentPage => _preferences.DisplayMode switch
    {
        CaseDisplayMode.Celsius => DisplayPage.Celsius,
        CaseDisplayMode.Usage => DisplayPage.Usage,
        CaseDisplayMode.Fahrenheit => DisplayPage.Fahrenheit,
        _ => L280Protocol.PageAt(_elapsed.Elapsed - _rotationStartedAt, _preferences.RotationSeconds)
    };

    public MainWindow()
    {
        ApplyTheme(_preferences.Theme);
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => RefreshBrand();
        RefreshBrand();
        Rotation.ItemsSource = AppPreferences.RotationOptions.Select(seconds =>
        {
            var item = new ComboBoxItem { Content = $"{seconds} seconds" };
            item.Classes.Add("preference-item");
            return item;
        }).ToArray();
        DisplayMode.SelectedIndex = (int)_preferences.DisplayMode;
        ThemeChoice.SelectedIndex = (int)_preferences.Theme;
        Rotation.SelectedIndex = Array.IndexOf(AppPreferences.RotationOptions, _preferences.RotationSeconds);
        UpdateRotationAvailability();
        _settingsReady = true;

        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _brandBitmap?.Dispose();
            if (!_busy) _telemetry.Dispose();
        };
    }

    private void RefreshBrand()
    {
        if (_closed) return;
        // The original full-color artwork matches the Windows executable icon.
        var variant = ActualThemeVariant == ThemeVariant.Dark ? "mono-white" : "dark";
        if (_brandVariant == variant) return;

        using var asset = AssetLoader.Open(new Uri($"avares://CPUSTATS/Assets/Brand/cpustats-{variant}.png"));
        var bitmap = new Bitmap(asset);
        BrandMark.Source = bitmap;
        Icon = new WindowIcon(bitmap);
        _brandBitmap?.Dispose();
        _brandBitmap = bitmap;
        _brandVariant = variant;
    }

    private static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e) => ShowSettings(true);
    private void BackButton_Click(object? sender, RoutedEventArgs e) => ShowSettings(false);

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Classes.Set("wide", e.NewSize.Width >= 980 && e.NewSize.Height >= 560);
        Classes.Set("large", e.NewSize.Width >= 1440 && e.NewSize.Height >= 760);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !SettingsPage.IsVisible || DisplayMode.IsDropDownOpen
            || Rotation.IsDropDownOpen || ThemeChoice.IsDropDownOpen) return;
        ShowSettings(false);
        e.Handled = true;
    }

    private void ShowSettings(bool show)
    {
        SettingsPage.IsVisible = BackButton.IsVisible = show;
        OverviewPage.IsVisible = SettingsButton.IsVisible = !show;
        PageScroll.Offset = default;
        if (show) BackButton.Focus();
        else SettingsButton.Focus();
    }

    private void Preferences_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || DisplayMode.SelectedIndex < 0 || ThemeChoice.SelectedIndex < 0 || Rotation.SelectedIndex < 0)
            return;
        var next = new AppPreferences((AppTheme)ThemeChoice.SelectedIndex, (CaseDisplayMode)DisplayMode.SelectedIndex,
            AppPreferences.RotationOptions[Rotation.SelectedIndex]);
        if (next == _preferences) return;
        if (next.DisplayMode != _preferences.DisplayMode || next.RotationSeconds != _preferences.RotationSeconds)
            _rotationStartedAt = _elapsed.Elapsed;
        if (next.Theme != _preferences.Theme) ApplyTheme(next.Theme);
        _preferences = next;
        UpdateRotationAvailability();
        var saved = _preferences.Save();
        SettingsNotice.IsVisible = !saved;
        SettingsNotice.Text = saved ? "" : "Changes apply now, but CPUSTATS couldn’t save them for next time.";
        PanelOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectCaseDisplayMode(CaseDisplayMode mode) => DisplayMode.SelectedIndex = (int)mode;

    private void UpdateRotationAvailability()
    {
        Rotation.IsEnabled = _preferences.DisplayMode == CaseDisplayMode.Alternate;
        RotationHint.Text = Rotation.IsEnabled ? "When alternating" : "Choose Alternate to use this";
    }

    internal async Task TogglePanelUpdatesAsync()
    {
        PanelUpdatesPaused = !PanelUpdatesPaused;
        ReconnectButton.IsEnabled = !PanelUpdatesPaused;
        PanelOptionsChanged?.Invoke(this, EventArgs.Empty);
        if (PanelUpdatesPaused)
        {
            _transportStatus = "Panel updates paused";
            DeviceStatus.Text = _transportStatus;
        }
        else
        {
            await ReconnectAsync();
        }
    }

    private async void ReconnectButton_Click(object? sender, RoutedEventArgs e) => await ReconnectAsync();

    internal async Task ReconnectAsync()
    {
        if (PanelUpdatesPaused) return;
        _retryAt = TimeSpan.Zero;
        DeviceStatus.Text = "Looking for your display…";
        await RefreshAndSendAsync();
    }

    private async void Timer_Tick(object? sender, EventArgs e) => await RefreshAndSendAsync();

    private async Task RefreshAndSendAsync()
    {
        if (_busy || _closed) return;
        _busy = true;
        try
        {
            var sampled = _elapsed.Elapsed - _lastSample >= TimeSpan.FromSeconds(1);
            if (sampled)
            {
                _current = await Task.Run(_telemetry.Read);
                _lastSample = _elapsed.Elapsed;
            }

            if (_closed) return;
            var page = CurrentPage;
            byte[]? packet = null;

            if (!PanelUpdatesPaused && _elapsed.Elapsed >= _retryAt)
            {
                packet = DateTimeOffset.Now - _current.SampledAt > TimeSpan.FromSeconds(5)
                    ? L280Protocol.Encode(null, null, page, _sequence++)
                    : TelemetryReport.Encode(_current, page, _sequence++);
                try
                {
                    await Task.Run(() => _transport.SendAsync(packet));
                    if (!PanelUpdatesPaused)
                    {
                        _transportStatus = $"USB write completed · {page}";
                        DeviceStatus.Text = "Connected";
                    }
                }
                catch (Exception ex)
                {
                    if (!PanelUpdatesPaused)
                    {
                        _transportStatus = $"USB write failed: {ex.Message}";
                        DeviceStatus.Text = "Can’t reach the display. Check its USB connection.";
                        _retryAt = _elapsed.Elapsed + TimeSpan.FromSeconds(2);
                    }
                }
            }

            if (_closed || !sampled) return;
            UpdateReadings();
            WriteStatus(page, packet);
        }
        catch (Exception ex)
        {
            if (_closed) return;
            SensorNotice.Text = "Couldn’t update the PC readings. CPUSTATS will try again.";
            SensorNotice.IsVisible = true;
            _current = _current with { Warning = ex.Message };
            _lastSample = _elapsed.Elapsed;
        }
        finally
        {
            _busy = false;
            if (_closed) _telemetry.Dispose();
        }
    }

    private void UpdateReadings()
    {
        SetModelName(CpuModel, _current.CpuName);
        SetModelName(GpuModel, _current.GpuName);
        CpuTemperature.Text = Number(_current.CpuTemperature);
        GpuTemperature.Text = Number(_current.GpuTemperature);
        CpuUnit.IsVisible = _current.CpuTemperature.HasValue;
        GpuUnit.IsVisible = _current.GpuTemperature.HasValue;
        CpuUsage.Text = Percent(_current.CpuUsage);
        GpuUsage.Text = Percent(_current.GpuUsage);
        RamUsage.Text = Percent(_current.RamUsage);
        CpuStrip.Value = _current.CpuUsage;
        GpuStrip.Value = _current.GpuUsage;
        RamStrip.Value = _current.RamUsage;
        LastUpdated.Text = $"Last updated  {_current.SampledAt.ToLocalTime().ToString("T", CultureInfo.CurrentCulture)}";

        var missing = (_current.CpuTemperature is null, _current.GpuTemperature is null) switch
        {
            (true, true) => "CPU and GPU temperatures are unavailable.",
            (true, false) => "CPU temperature is unavailable.",
            (false, true) => "GPU temperature is unavailable.",
            _ => null
        };
        SensorNotice.IsVisible = missing is not null;
        SensorNotice.Text = missing is not null && OperatingSystem.IsWindows() && _current.Warning?.Contains("restart") == true
            ? missing + " Close CPUSTATS and open it as administrator."
            : missing;
        ToolTip.SetTip(SensorNotice, _current.Warning);
    }

    private static void SetModelName(TextBlock label, string? name)
    {
        var displayName = string.IsNullOrWhiteSpace(name) ? "Model unavailable" : name;
        if (label.Text == displayName) return;
        label.Text = displayName;
        ToolTip.SetTip(label, name);
    }

    private void WriteStatus(DisplayPage page, byte[]? packet)
    {
        try
        {
            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "last-status.json"),
                JsonSerializer.Serialize(new
                {
                    Telemetry = _current, Page = page.ToString(),
                    Transport = _transportStatus, Report = packet is null ? null : Convert.ToHexString(packet)
                }, StatusJson));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "—";
    private static string Percent(int? value) => value.HasValue ? $"{Number(value)}%" : "—";
}
