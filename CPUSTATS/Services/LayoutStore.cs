using System.Text.Json;
using CPUSTATS.Models;

namespace CPUSTATS.Services;

public sealed class LayoutStore
{
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "L280Studio", "layouts");

    public LayoutStore() => Directory.CreateDirectory(_directory);

    public IReadOnlyList<string> List() => Directory.GetFiles(_directory, "*.json").Select(x => Path.GetFileNameWithoutExtension(x)!).OrderBy(x => x).ToArray();

    public async Task SaveAsync(DashboardLayout layout, CancellationToken token = default)
    {
        var safeName = string.Concat(layout.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        await File.WriteAllTextAsync(Path.Combine(_directory, safeName + ".json"), JsonSerializer.Serialize(layout, _options), token);
    }

    public async Task<DashboardLayout?> LoadAsync(string name, CancellationToken token = default)
    {
        var path = Path.Combine(_directory, Path.GetFileNameWithoutExtension(name) + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<DashboardLayout>(await File.ReadAllTextAsync(path, token), _options) : null;
    }
}
