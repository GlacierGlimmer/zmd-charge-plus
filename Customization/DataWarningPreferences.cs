using System.Text.Json;

namespace EndfieldChargePlus.Customization;

// Separate from settings snapshots so Save & Apply cannot overwrite a dialog's choice.
internal sealed class DataWarningPreferences
{
    private readonly string _path;
    private readonly object _gate = new();
    private HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    internal DataWarningPreferences(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path)) _ignored.UnionWith((JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to read ignored HUD data warnings.", ex); }
    }
    internal bool IsIgnored(string key) { lock (_gate) return _ignored.Contains(key); }
    internal void Ignore(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            var updated = new HashSet<string>(_ignored, StringComparer.OrdinalIgnoreCase);
            updated.UnionWith(keys.Where(k => !string.IsNullOrWhiteSpace(k)));
            if (updated.SetEquals(_ignored)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string temp = _path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(updated.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray()));
                File.Move(temp, _path, overwrite: true);
                _ignored = updated;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
