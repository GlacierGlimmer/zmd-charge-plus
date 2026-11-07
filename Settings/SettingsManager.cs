using System;
using System.IO;
using System.Text.Json;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Settings;

public static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string SettingsDirectory { get; } = AppPaths.DataDirectory;

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");
    public static string BackupsDirectory => Path.Combine(SettingsDirectory, "Backups");

    internal static string? ResolveSettingsReadPath(string primary,string localAppData,string? packageRoot)
    {
        if(!Path.IsPathFullyQualified(primary))throw new ArgumentException("Settings path must be absolute.",nameof(primary));
        var candidates=new List<string>{primary};
        if(packageRoot is not null)
        {
            if(!Path.IsPathFullyQualified(packageRoot))throw new ArgumentException("Package data root must be absolute.",nameof(packageRoot));
            // Preserve the existing Store user's data before considering a
            // portable copy or an older, separately exported Store backup.
            candidates.Add(Path.Combine(packageRoot,"LocalCache","Local","EndfieldChargePlus","settings.json"));
            candidates.Add(Path.Combine(packageRoot,"AC","EndfieldChargePlus","settings.json"));
            var packages=Directory.GetParent(packageRoot);
            if(packages?.Name.Equals("Packages",StringComparison.OrdinalIgnoreCase)==true && packages.Parent is {} local)
                localAppData=local.FullName;
        }
        if(Path.IsPathFullyQualified(localAppData))
        {
            candidates.Add(Path.Combine(localAppData,"EndfieldChargePlus","StoreBackup","settings.json"));
            candidates.Add(Path.Combine(localAppData,"EndfieldChargePlus","settings.json"));
            candidates.Add(Path.Combine(localAppData,"EndfieldCharge-CustomHUD","settings.json"));
            candidates.Add(Path.Combine(localAppData,"EndfieldCharge","settings.json"));
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    public static AppSettings Load()
    {
        string? packageRoot=AppPaths.PackageFamilyName is null ? null : Directory.GetParent(SettingsDirectory)?.Parent?.FullName;
        string? path=ResolveSettingsReadPath(SettingsPath,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),packageRoot);

        if (path is null)
        {
            var firstRun = CreateDefaults() with { HudEnabled = true };
            AppLog.Info("No settings file found. Using first-run defaults.");
            return firstRun;
        }

        try
        {
            AppSettings loaded = DeserializeFile(path);
            var normalized = Normalize(loaded) with
            {
                // Every application launch starts with the HUD master switch ON.
                HudEnabled = true,
            };

            if (!File.Exists(SettingsPath))
            {
                AppLog.Info($"Migrating legacy settings from: {path}");
                Save(normalized);
            }

            return normalized;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings from: {path}", ex);
            BackupCorruptFile(path);

            var fallback = CreateDefaults() with { HudEnabled = true };
            try { Save(fallback); }
            catch (Exception saveEx) { AppLog.Error("Failed to persist fallback settings.", saveEx); }
            return fallback;
        }
    }

    public static AppSettings CreateDefaults()
        => Normalize(new AppSettings());

    private static readonly object SaveGate=new();
    public static void Save(AppSettings settings)
    {
        lock(SaveGate)
        {
        settings = Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(BackupsDirectory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        string temp = SettingsPath + ".tmp";

        try
        {
            if (File.Exists(SettingsPath))
            {
                string previous = Path.Combine(BackupsDirectory, "settings.previous.json");
                File.Copy(SettingsPath, previous, overwrite: true);
            }

            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, overwrite: true);
            AppLog.Info("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            AppLog.Error("Failed to save settings.", ex);
            throw;
        }
        }
    }

    internal static void SaveImmediatePreferences(AppSettings preferences)
    {
        lock(SaveGate)
        {
        // A data warning may have saved ignored keys since Settings was opened.
        // Read the latest file, and merge only the three immediate UI preferences.
        var latest=File.Exists(SettingsPath) ? Normalize(DeserializeFile(SettingsPath)) : preferences;
        Save(latest with {UiLanguage=preferences.UiLanguage,GlobalFontScale=preferences.GlobalFontScale,GlobalUiScale=preferences.GlobalUiScale,
            HudInstances=latest.HudInstances.Select(i=>i with {Settings=i.Settings with {
                GlobalFontScale=preferences.GlobalFontScale,GlobalUiScale=preferences.GlobalUiScale}}).ToList()});
        }
    }

    public static void ExportToFile(string destinationPath, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is empty.", nameof(destinationPath));

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(Normalize(settings), JsonOptions);
        File.WriteAllText(destinationPath, json);
        AppLog.Info($"Settings exported to: {destinationPath}");
    }

    public static AppSettings ImportFromFile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Configuration file does not exist.", sourcePath);

        AppSettings imported = Normalize(DeserializeFile(sourcePath));
        AppLog.Info($"Settings imported from: {sourcePath}");
        return imported;
    }

    public static string? BackupCurrent(string reason = "manual")
    {
        if (!File.Exists(SettingsPath)) return null;

        Directory.CreateDirectory(BackupsDirectory);
        string safeReason = SanitizeFilePart(reason);
        string target = Path.Combine(BackupsDirectory, $"settings.{safeReason}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(SettingsPath, target, overwrite: false);
        TrimBackups();
        AppLog.Info($"Settings backup created: {target}");
        return target;
    }

    private static AppSettings DeserializeFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("Configuration JSON did not contain a valid settings object.");
    }

    private static AppSettings Normalize(AppSettings settings)
        => settings with
        {
            HudOpacity = Math.Clamp(settings.HudOpacity, 0.10, 1.0),
            GlobalScale=double.IsFinite(settings.GlobalScale) ? Math.Clamp(settings.GlobalScale,0.4,1.4) : AppSettings.DefaultGlobalScale,
            GlobalFontScale = GlobalAppearance.NormalizeFontScale(settings.GlobalFontScale),
            GlobalUiScale = GlobalAppearance.NormalizeUiScale(settings.GlobalUiScale),
            Workspace = WorkspacePreferences.Normalize(settings.Workspace),
            SamplingIntervalSeconds = double.IsFinite(settings.SamplingIntervalSeconds)
                ? Math.Clamp(settings.SamplingIntervalSeconds, 0.1, 60) : 1,
            HudInstances = (settings.HudInstances ?? new()).Where(i => i is not null).Take(15)
                .GroupBy(i => i.Id).Select(g => g.First()).Select(i => i with {
                    Id=string.IsNullOrWhiteSpace(i.Id) ? Guid.NewGuid().ToString("N") : i.Id,
                    Name=string.IsNullOrWhiteSpace(i.Name) ? "HUD" : i.Name.Trim(),
                    Settings=Normalize((i.Settings ?? new()) with { MultiHudEnabled=false, HudInstances=new() }) }).ToList(),
            HudFontFamily = (settings.HudFontFamily ?? "").Trim(),
            HudToggleHotkey = HudHotkeyGesture.TryParse(settings.HudToggleHotkey, out var hotkey) ? hotkey.Text : "",
            QuickHotkeys = HudQuickHotkeys.Normalize(settings.QuickHotkeys),
            HudHotkeyShowAnimation = string.Equals(settings.HudHotkeyShowAnimation,"Simple",StringComparison.OrdinalIgnoreCase) ? "Simple" : "Full",
            HotZonePosition=(HudPosition)Math.Clamp((int)settings.HotZonePosition,0,8),
            HotZoneWidth=Math.Clamp(settings.HotZoneWidth,8,8192),HotZoneHeight=Math.Clamp(settings.HotZoneHeight,2,8192),
            HotZoneOffsetX=Math.Clamp(settings.HotZoneOffsetX,-32768,32768),HotZoneOffsetY=Math.Clamp(settings.HotZoneOffsetY,-32768,32768),
            HotZoneDwellMilliseconds=Math.Clamp(settings.HotZoneDwellMilliseconds,0,3000),
            ClickToCycle = settings.ClickToCycle,
            UiLanguage = LocalizationManager.NormalizePreference(settings.UiLanguage),
            CustomHud = HudSettingsNormalizer.Normalize(settings.CustomHud ?? CustomHudSettings.CreateDefault()),
        };

    private static void BackupCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Directory.CreateDirectory(BackupsDirectory);
            string target = Path.Combine(BackupsDirectory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(path, target, overwrite: false);
            AppLog.Warn($"Corrupt settings file backed up to: {target}");
            TrimBackups();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to back up the corrupt settings file.", ex);
        }
    }

    private static void TrimBackups()
    {
        try
        {
            foreach (var file in new DirectoryInfo(BackupsDirectory)
                         .GetFiles("settings.*.json")
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Skip(20))
            {
                try { file.Delete(); }
                catch { }
            }
        }
        catch { }
    }

    private static string SanitizeFilePart(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '-');
        return string.IsNullOrWhiteSpace(value) ? "backup" : value.Trim();
    }
}
