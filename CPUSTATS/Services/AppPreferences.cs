using System.Text.Json;

namespace CPUSTATS.Services;

internal enum AppTheme { System, Light, Dark }
internal enum CaseDisplayMode { Alternate, Celsius, Usage, Fahrenheit }

internal sealed record AppPreferences(AppTheme Theme, CaseDisplayMode DisplayMode, int RotationSeconds)
{
    public static readonly int[] RotationOptions = [3, L280Protocol.DefaultRotationSeconds, 10, 15, 30];
    private static readonly AppPreferences Defaults = new(AppTheme.System, CaseDisplayMode.Alternate, L280Protocol.DefaultRotationSeconds);
    private static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string SettingsPath = Path.Combine(LocalData, "CPUSTATS", "settings.json");
    private static readonly string PreviousSettingsPath = Path.Combine(LocalData, "RigBeat", "settings.json");
    private static readonly string LegacySettingsPath = Path.Combine(LocalData, "CaseGauge", "settings.json");

    public static AppPreferences Load()
    {
        try
        {
            var path = new[] { SettingsPath, PreviousSettingsPath, LegacySettingsPath }.FirstOrDefault(File.Exists);
            if (path is null) return Defaults;
            if (JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) is { } saved)
                return new(
                    Enum.IsDefined(saved.Theme) ? saved.Theme : Defaults.Theme,
                    Enum.IsDefined(saved.DisplayMode) ? saved.DisplayMode : Defaults.DisplayMode,
                    RotationOptions.Contains(saved.RotationSeconds) ? saved.RotationSeconds : Defaults.RotationSeconds);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return Defaults;
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, SettingsPath, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
