using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow : Window
{
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();
    private static UpdateCheckResult? _lastUpdateResult;

    public enum UpdateStatusKind
    {
        NoRemoteVersion,
        Uncomparable,
        UpdateAvailable,
        UpToDate,
        LocalNewer,
        NetworkError,
        Timeout,
        Error,
    }

    public sealed record UpdateCheckResult(
        string CurrentVersion,
        string? LatestVersion,
        UpdateStatusKind Status,
        bool HasUpdate,
        bool CheckSucceeded,
        string? Detail = null)
    {
        public string LatestVersionText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("最新版本：未获取到", "Latest: unavailable"),
            UpdateStatusKind.NetworkError or UpdateStatusKind.Error => LocalizationManager.Text("最新版本：检查失败", "Latest: check failed"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("最新版本：检查超时", "Latest: timed out"),
            _ when !string.IsNullOrWhiteSpace(LatestVersion) => LocalizationManager.Text($"最新版本：v{LatestVersion}", $"Latest: v{LatestVersion}"),
            _ => LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked"),
        };

        public string StatusText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("状态：未获取到 Release / 标签，仓库可能尚未公开", "Status: no release/tag found; the repository may not be public yet"),
            UpdateStatusKind.Uncomparable => LocalizationManager.Text("状态：已获取线上版本，但版本号格式无法比较", "Status: remote version found, but its format cannot be compared"),
            UpdateStatusKind.UpdateAvailable => LocalizationManager.Text("状态：发现新版本", "Status: update available"),
            UpdateStatusKind.UpToDate => LocalizationManager.Text("状态：当前已是最新版本", "Status: up to date"),
            UpdateStatusKind.LocalNewer => LocalizationManager.Text("状态：当前版本高于线上最新版本", "Status: local version is newer than the latest online version"),
            UpdateStatusKind.NetworkError => LocalizationManager.Text($"状态：网络请求失败（{Detail ?? "连接错误"}）", $"Status: network request failed ({Detail ?? "connection error"})"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("状态：检查超时，请稍后重试", "Status: update check timed out; try again later"),
            _ => LocalizationManager.Text("状态：检查更新时发生错误", "Status: an error occurred while checking for updates"),
        };
    }

    public static UpdateCheckResult? LastUpdateResult => _lastUpdateResult;

    private AppSettings _settings;
    private readonly HudWindow _hud;
    private readonly CustomHudRuntime _runtime;
    private bool _monitorComboReady;
    private string? _maintenanceStatusZh;
    private string? _maintenanceStatusEn;

    public SettingsWindow(AppSettings settings, HudWindow hud, CustomHudRuntime runtime)
    {
        InitializeComponent();
        _settings = settings;
        _hud = hud;
        _runtime = runtime;

        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();

        LanguageChineseBtn.Click += (_, _) => OnLanguageSelected(AppLanguage.SimplifiedChinese);
        LanguageEnglishBtn.Click += (_, _) => OnLanguageSelected(AppLanguage.English);

        ApplySettingsToControls(settings);
        UpdateLanguageSwitchVisual();

        HudEnabledSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        AlwaysVisibleSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        PositionModeCombo.SelectionChanged += (_, _) => UpdateModeUi();
        SaveBtn.Click += OnSave;
        OpenSettingsFolderBtn.Click += OnOpenSettingsFolder;
        ResetSizeAnimationBtn.Click += OnResetSizeAnimation;
        ExportSettingsBtn.Click += OnExportSettings;
        ImportSettingsBtn.Click += OnImportSettings;
        BackupSettingsBtn.Click += OnBackupSettings;
        OpenLogsFolderBtn.Click += OnOpenLogsFolder;
        ResetAllSettingsBtn.Click += OnResetAllSettings;
        HudOpacitySlider.ValueChanged += (_, _) => UpdateOpacityLabel();
        OpenProjectGithubBtn.Click += (_, _) => OpenUrl("https://github.com/GlacierGlimmer/zmd-charge-plus");
        OpenUpstreamBtn.Click += (_, _) => OpenUrl("https://github.com/QinAnze/zmd-charge");
        OpenWebsiteBtn.Click += (_, _) => OpenUrl("https://zmd-bar.x-neko.com");
        CheckUpdateBtn.Click += OnCheckUpdate;

        AboutArchitectureText.Text = $"Windows · {GetCurrentProcessArchitectureText()}";

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"当前版本：v{currentVersion}", $"Current: v{currentVersion}");
        UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked");
        UpdateStatusText.Text = LocalizationManager.Text("状态：尚未检查", "Status: not checked");
        if (LastUpdateResult is { } cachedUpdateResult)
            ApplyUpdateCheckResult(cachedUpdateResult);

        Opened += (_, _) => PopulateMonitorCombo();

        UpdateModeUi();
    }

    private static string GetCurrentProcessArchitectureText()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            Architecture.Arm => "ARM",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };
    }

    private void RebuildLocalizedChoiceItems()
    {
        int layerIndex = PersistentLayerCombo.SelectedIndex;
        int modeIndex = PositionModeCombo.SelectedIndex;
        int positionIndex = PositionCombo.SelectedIndex;

        PersistentLayerCombo.Items.Clear();
        PersistentLayerCombo.Items.Add(LocalizationManager.Text("始终置顶", "Always on Top"));
        PersistentLayerCombo.Items.Add(LocalizationManager.Text("桌面层（不置顶）", "Desktop Layer"));

        PositionModeCombo.Items.Clear();
        PositionModeCombo.Items.Add(LocalizationManager.Text("预设位置 + 微调", "Preset + Offset"));
        PositionModeCombo.Items.Add(LocalizationManager.Text("自定义坐标", "Custom Coordinates"));

        PositionCombo.Items.Clear();
        string[] zh = ["左上", "顶部居中", "右上", "左侧居中", "屏幕居中", "右侧居中", "左下", "底部居中", "右下"];
        string[] en = ["Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right"];
        for (int i = 0; i < zh.Length; i++)
            PositionCombo.Items.Add(LocalizationManager.Text(zh[i], en[i]));

        if (layerIndex >= 0) PersistentLayerCombo.SelectedIndex = Math.Min(layerIndex, PersistentLayerCombo.Items.Count - 1);
        if (modeIndex >= 0) PositionModeCombo.SelectedIndex = Math.Min(modeIndex, PositionModeCombo.Items.Count - 1);
        if (positionIndex >= 0) PositionCombo.SelectedIndex = Math.Min(positionIndex, PositionCombo.Items.Count - 1);
    }

    private async void OnLanguageSelected(AppLanguage language)
    {
        string preference = LocalizationManager.PreferenceFor(language);
        bool languageChanged = LocalizationManager.Current != language;

        _settings = _settings with { UiLanguage = preference };
        LocalizationManager.SetLanguage(language);

        ApplyLocalization();
        UpdateLanguageSwitchVisual();

        // Language is an immediate UI preference. Persist only the language choice here;
        // other editor changes still require Save & Apply.
        SettingsManager.Save(_settings);

        // Persistent and transient HUD renderers read LocalizationManager at render time,
        // so their next live refresh switches language without replacing unsaved settings.
        if (languageChanged)
            await Task.Delay(1);
    }

    private void ApplyLocalization()
    {
        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();
        if (_monitorComboReady)
            PopulateMonitorCombo(force: true);

        Customizer.ApplyLocalization();

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"当前版本：v{currentVersion}", $"Current: v{currentVersion}");
        if (LastUpdateResult is { } result)
            ApplyUpdateCheckResult(result);
        else
        {
            UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked");
            UpdateStatusText.Text = LocalizationManager.Text("状态：尚未检查", "Status: not checked");
        }

        RefreshMaintenanceStatusLocalization();
    }

    private void SetMaintenanceStatus(string zh, string en)
    {
        _maintenanceStatusZh = zh;
        _maintenanceStatusEn = en;
        RefreshMaintenanceStatusLocalization();
    }

    private void RefreshMaintenanceStatusLocalization()
    {
        if (_maintenanceStatusZh is null || _maintenanceStatusEn is null)
            return;

        MaintenanceStatusText.Text = LocalizationManager.Text(_maintenanceStatusZh, _maintenanceStatusEn);
    }

    private void UpdateLanguageSwitchVisual()
    {
        var activeBg = new SolidColorBrush(Color.Parse("#C6CA4C"));
        var inactiveBg = new SolidColorBrush(Color.Parse("#2B2B2E"));
        var activeFg = new SolidColorBrush(Color.Parse("#171719"));
        var inactiveFg = new SolidColorBrush(Color.Parse("#F0F0F2"));

        bool english = LocalizationManager.IsEnglish;
        LanguageChineseBtn.Background = english ? inactiveBg : activeBg;
        LanguageChineseBtn.Foreground = english ? inactiveFg : activeFg;
        LanguageChineseBtn.FontWeight = english ? FontWeight.Normal : FontWeight.SemiBold;

        LanguageEnglishBtn.Background = english ? activeBg : inactiveBg;
        LanguageEnglishBtn.Foreground = english ? activeFg : inactiveFg;
        LanguageEnglishBtn.FontWeight = english ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private void PopulateMonitorCombo(bool force = false)
    {
        if (_monitorComboReady && !force) return;
        _monitorComboReady = true;

        MonitorCombo.Items.Clear();
        MonitorCombo.Items.Add(LocalizationManager.Text("主显示器（自动）", "Primary Display (Auto)"));

        var screens = Screens.All;
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            var area = s.WorkingArea;
            string primary = ReferenceEquals(s, Screens.Primary) ? LocalizationManager.Text(" · 主", " · Primary") : "";
            MonitorCombo.Items.Add(LocalizationManager.Text($"显示器 {i + 1} · {area.Width}×{area.Height}{primary}", $"Display {i + 1} · {area.Width}×{area.Height}{primary}"));
        }

        int wanted = _settings.MonitorIndex < 0 ? 0 : _settings.MonitorIndex + 1;
        MonitorCombo.SelectedIndex = wanted >= 0 && wanted < MonitorCombo.Items.Count ? wanted : 0;
    }

    private void UpdateModeUi()
    {
        bool persistent = AlwaysVisibleSwitch.IsChecked == true;
        PersistentLayerCombo.IsEnabled = persistent;
        PersistentLayerCombo.Opacity = persistent ? 1.0 : 0.45;

        bool custom = PositionModeCombo.SelectedIndex == 1;
        PresetPositionPanel.IsEnabled = !custom;
        PresetPositionPanel.Opacity = custom ? 0.40 : 1.0;
        CustomPositionPanel.IsEnabled = custom;
        CustomPositionPanel.Opacity = custom ? 1.0 : 0.40;
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        _settings = settings;

        ScaleBox.Value = (decimal)settings.GlobalScale;
        DurationBox.Value = (decimal)settings.DisplayDurationSeconds;
        BounceBox.Value = (decimal)settings.BounceStrength;
        RippleIntensityBox.Value = (decimal)settings.RippleIntensity;
        RippleSpreadBox.Value = (decimal)settings.RippleSpread;
        HudOpacitySlider.Value = Math.Clamp(settings.HudOpacity * 100.0, 10.0, 100.0);
        UpdateOpacityLabel();

        HudEnabledSwitch.IsChecked = settings.HudEnabled;
        StartupSwitch.IsChecked = settings.StartWithWindows;
        AlwaysVisibleSwitch.IsChecked = settings.AlwaysVisible;
        PersistentLayerCombo.SelectedIndex = settings.PersistentLayer == PersistentHudLayer.Desktop ? 1 : 0;
        PositionModeCombo.SelectedIndex = settings.PositionMode == HudPositionMode.CustomCoordinates ? 1 : 0;
        PositionCombo.SelectedIndex = PositionToIndex(settings.HudPosition);
        OffsetXBox.Value = settings.HudOffsetX;
        OffsetYBox.Value = settings.HudOffsetY;
        CustomXBox.Value = settings.HudCustomX;
        CustomYBox.Value = settings.HudCustomY;

        if (_monitorComboReady)
        {
            int wanted = settings.MonitorIndex < 0 ? 0 : settings.MonitorIndex + 1;
            MonitorCombo.SelectedIndex = wanted >= 0 && wanted < MonitorCombo.Items.Count ? wanted : 0;
        }

        Customizer.Load(settings.CustomHud, _hud);
        UpdateModeUi();
    }

    private AppSettings CollectSettingsFromUi()
        => _settings with
        {
            HudEnabled = HudEnabledSwitch.IsChecked == true,
            StartWithWindows = StartupSwitch.IsChecked == true,
            UiLanguage = _settings.UiLanguage,
            GlobalScale = (double)(ScaleBox.Value ?? (decimal)AppSettings.DefaultGlobalScale),
            DisplayDurationSeconds = (double)(DurationBox.Value ?? (decimal)AppSettings.DefaultDisplayDurationSeconds),
            BounceStrength = (double)(BounceBox.Value ?? (decimal)AppSettings.DefaultBounceStrength),
            RippleIntensity = (double)(RippleIntensityBox.Value ?? (decimal)AppSettings.DefaultRippleIntensity),
            RippleSpread = (double)(RippleSpreadBox.Value ?? (decimal)AppSettings.DefaultRippleSpread),
            HudOpacity = Math.Clamp(HudOpacitySlider.Value / 100.0, 0.10, 1.0),

            AlwaysVisible = AlwaysVisibleSwitch.IsChecked == true,
            PersistentLayer = PersistentLayerCombo.SelectedIndex == 1
                ? PersistentHudLayer.Desktop
                : PersistentHudLayer.Topmost,

            PositionMode = PositionModeCombo.SelectedIndex == 1
                ? HudPositionMode.CustomCoordinates
                : HudPositionMode.Preset,
            HudPosition = IndexToPosition(PositionCombo.SelectedIndex),
            HudOffsetX = (int)(OffsetXBox.Value ?? 0),
            HudOffsetY = (int)(OffsetYBox.Value ?? 0),
            HudCustomX = (int)(CustomXBox.Value ?? 0),
            HudCustomY = (int)(CustomYBox.Value ?? 0),
            MonitorIndex = MonitorCombo.SelectedIndex <= 0 ? -1 : MonitorCombo.SelectedIndex - 1,

            CustomHud = Customizer.ExportSettings(),
        };

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        _settings = CollectSettingsFromUi();

        SettingsManager.Save(_settings);
        StartupManager.Apply(_settings.StartWithWindows);

        SaveBtn.IsEnabled = false;
        SaveBtn.Content = LocalizationManager.Text("应用中…", "Applying…");
        try
        {
            await _runtime.ApplySettingsWithTransitionAsync(_settings);
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }

        SaveBtn.Content = LocalizationManager.Text("已保存", "Saved");
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            SaveBtn.Content = LocalizationManager.Text("保存并应用", "Save & Apply");
            timer.Stop();
        };
        timer.Start();
    }

    private async void OnExportSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationManager.Text("导出 Endfield Charge Plus 配置", "Export Endfield Charge Plus Config"),
                SuggestedFileName = $"EndfieldChargePlus-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(LocalizationManager.Text("JSON 配置", "JSON Config")) { Patterns = new[] { "*.json" } },
                },
            });

            if (file is null) return;
            SettingsManager.ExportToFile(file.Path.LocalPath, CollectSettingsFromUi());
            SetMaintenanceStatus("配置已导出。", "Config exported.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to export settings from the settings UI.", ex);
            SetMaintenanceStatus($"导出失败：{ex.Message}", "Export failed. See logs.");
        }
    }

    private async void OnImportSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.Text("导入 Endfield Charge Plus 配置", "Import Endfield Charge Plus Config"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(LocalizationManager.Text("JSON 配置", "JSON Config")) { Patterns = new[] { "*.json" } },
                },
            });

            if (files.Count == 0) return;

            string? backup = SettingsManager.BackupCurrent("before-import");
            AppSettings imported = SettingsManager.ImportFromFile(files[0].Path.LocalPath);
            SettingsManager.Save(imported);
            StartupManager.Apply(imported.StartWithWindows);
            LocalizationManager.Initialize(imported.UiLanguage);
            ApplyLocalization();
            UpdateLanguageSwitchVisual();
            ApplySettingsToControls(imported);
            await _runtime.ApplySettingsWithTransitionAsync(imported);

            if (backup is null)
                SetMaintenanceStatus("配置已导入并应用。", "Config imported and applied.");
            else
                SetMaintenanceStatus(
                    $"配置已导入并应用；旧配置已备份到 {Path.GetFileName(backup)}。",
                    $"Config imported and applied; previous config backed up as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to import settings from the settings UI.", ex);
            SetMaintenanceStatus($"导入失败：{ex.Message}", "Import failed. See logs.");
        }
    }

    private void OnBackupSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Save the current editor state first so the manual backup matches what the user sees.
            var current = CollectSettingsFromUi();
            SettingsManager.Save(current);
            string? backup = SettingsManager.BackupCurrent("manual");
            if (backup is null)
                SetMaintenanceStatus("当前没有可备份的配置文件。", "No config file is available to back up.");
            else
                SetMaintenanceStatus(
                    $"配置已备份：{Path.GetFileName(backup)}",
                    $"Config backed up: {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to create a manual settings backup.", ex);
            SetMaintenanceStatus($"备份失败：{ex.Message}", "Backup failed. See logs.");
        }
    }

    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogsDirectory,
                UseShellExecute = true,
            });
            SetMaintenanceStatus("已打开日志文件夹。", "Logs folder opened.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open the logs directory.", ex);
            SetMaintenanceStatus($"无法打开日志文件夹：{ex.Message}", "Could not open the logs folder. See logs.");
        }
    }

    private void OnResetAllSettings(object? sender, RoutedEventArgs e)
    {
        var defaults = SettingsManager.CreateDefaults();
        LocalizationManager.Initialize(defaults.UiLanguage);
        ApplyLocalization();
        UpdateLanguageSwitchVisual();
        ApplySettingsToControls(defaults);
        SetMaintenanceStatus("已载入首次安装默认值；点击右上角“保存并应用”后生效。", "First-run defaults loaded; click Save & Apply to commit them.");
        AppLog.Info("Default settings were loaded into the settings editor (not yet saved). ");
    }

    private void OnResetSizeAnimation(object? sender, RoutedEventArgs e)
    {
        ScaleBox.Value = (decimal)AppSettings.DefaultGlobalScale;
        DurationBox.Value = (decimal)AppSettings.DefaultDisplayDurationSeconds;
        BounceBox.Value = (decimal)AppSettings.DefaultBounceStrength;
        RippleIntensityBox.Value = (decimal)AppSettings.DefaultRippleIntensity;
        RippleSpreadBox.Value = (decimal)AppSettings.DefaultRippleSpread;
    }

    private void UpdateOpacityLabel()
    {
        HudOpacityValueText.Text = $"{Math.Round(HudOpacitySlider.Value):0}%";
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EndfieldChargePlus", GetCurrentVersionText()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string GetCurrentVersionText()
        => typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = LocalizationManager.Text("检查中…", "Checking…");
        UpdateStatusText.Text = LocalizationManager.Text("状态：正在连接 GitHub 检查更新…", "Status: checking GitHub…");

        UpdateCheckResult result = await CheckForUpdatesAsync();
        ApplyUpdateCheckResult(result);

        CheckUpdateBtn.Content = LocalizationManager.Text("检查更新", "Check Updates");
        CheckUpdateBtn.IsEnabled = true;
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        string currentText = GetCurrentVersionText();

        try
        {
            string? latestTag = await TryGetLatestReleaseTagAsync();
            if (string.IsNullOrWhiteSpace(latestTag))
                latestTag = await TryGetLatestTagAsync();

            if (string.IsNullOrWhiteSpace(latestTag))
            {
                return StoreUpdateResult(new UpdateCheckResult(
                    currentText,
                    LatestVersion: null,
                    Status: UpdateStatusKind.NoRemoteVersion,
                    HasUpdate: false,
                    CheckSucceeded: true));
            }

            string normalizedLatest = NormalizeVersionTag(latestTag);
            Version? current = TryParseVersion(currentText);
            Version? latest = TryParseVersion(normalizedLatest);

            UpdateStatusKind status;
            bool hasUpdate = false;
            if (current is null || latest is null)
            {
                status = UpdateStatusKind.Uncomparable;
            }
            else if (latest > current)
            {
                status = UpdateStatusKind.UpdateAvailable;
                hasUpdate = true;
            }
            else if (latest == current)
            {
                status = UpdateStatusKind.UpToDate;
            }
            else
            {
                status = UpdateStatusKind.LocalNewer;
            }

            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: normalizedLatest,
                Status: status,
                HasUpdate: hasUpdate,
                CheckSucceeded: true));
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("Update check HTTP request failed.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.NetworkError,
                HasUpdate: false,
                CheckSucceeded: false,
                Detail: ex.StatusCode?.ToString()));
        }
        catch (TaskCanceledException ex)
        {
            AppLog.Error("Update check timed out.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.Timeout,
                HasUpdate: false,
                CheckSucceeded: false));
        }
        catch (Exception ex)
        {
            AppLog.Error("Update check failed.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.Error,
                HasUpdate: false,
                CheckSucceeded: false));
        }
    }

    private static UpdateCheckResult StoreUpdateResult(UpdateCheckResult result)
    {
        _lastUpdateResult = result;
        return result;
    }

    public void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"当前版本：v{result.CurrentVersion}", $"Current: v{result.CurrentVersion}");
        UpdateLatestVersionText.Text = result.LatestVersionText;
        UpdateStatusText.Text = result.StatusText;
    }

    private static async Task<string?> TryGetLatestReleaseTagAsync()
    {
        using HttpResponseMessage response = await UpdateHttpClient.GetAsync(
            "https://api.github.com/repos/GlacierGlimmer/zmd-charge-plus/releases/latest");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        return json.RootElement.TryGetProperty("tag_name", out JsonElement tag)
            ? tag.GetString()
            : null;
    }

    private static async Task<string?> TryGetLatestTagAsync()
    {
        using HttpResponseMessage response = await UpdateHttpClient.GetAsync(
            "https://api.github.com/repos/GlacierGlimmer/zmd-charge-plus/tags?per_page=1");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        JsonElement first = root[0];
        return first.TryGetProperty("name", out JsonElement name)
            ? name.GetString()
            : null;
    }

    private static string NormalizeVersionTag(string tag)
    {
        string value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        int suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            value = value[..suffix];

        return value;
    }

    private static Version? TryParseVersion(string value)
        => Version.TryParse(NormalizeVersionTag(value), out Version? version) ? version : null;

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void OnOpenSettingsFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsManager.SettingsDirectory);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsManager.SettingsDirectory,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static int PositionToIndex(HudPosition p) => p switch
    {
        HudPosition.TopLeft => 0,
        HudPosition.TopCenter => 1,
        HudPosition.TopRight => 2,
        HudPosition.CenterLeft => 3,
        HudPosition.Center => 4,
        HudPosition.CenterRight => 5,
        HudPosition.BottomLeft => 6,
        HudPosition.BottomCenter => 7,
        HudPosition.BottomRight => 8,
        _ => 1,
    };

    private static HudPosition IndexToPosition(int index) => index switch
    {
        0 => HudPosition.TopLeft,
        1 => HudPosition.TopCenter,
        2 => HudPosition.TopRight,
        3 => HudPosition.CenterLeft,
        4 => HudPosition.Center,
        5 => HudPosition.CenterRight,
        6 => HudPosition.BottomLeft,
        7 => HudPosition.BottomCenter,
        8 => HudPosition.BottomRight,
        _ => HudPosition.TopCenter,
    };
}
