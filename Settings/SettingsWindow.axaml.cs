using Avalonia.LogicalTree;
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
        public string NotificationTitle => LocalizationManager.Text("Endfield Charge Plus 有新版本", "Endfield Charge Plus update available");
        public string NotificationMessage
        {
            get
            {
                string latest = string.IsNullOrWhiteSpace(LatestVersion) ? "" : "v" + LatestVersion.Trim().TrimStart('v', 'V');
                return LocalizationManager.Formatted($"发现 {latest}。可在“关于”页面查看更新状态。", (System.FormattableString)$"Found {latest}. See About for update details.");
            }
        }

        public string LatestVersionText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("最新版本：未获取到", "Latest: unavailable"),
            UpdateStatusKind.NetworkError or UpdateStatusKind.Error => LocalizationManager.Text("最新版本：检查失败", "Latest: check failed"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("最新版本：检查超时", "Latest: timed out"),
            _ when !string.IsNullOrWhiteSpace(LatestVersion) => LocalizationManager.Formatted($"最新版本：v{LatestVersion}", (System.FormattableString)$"Latest: v{LatestVersion}"),
            _ => LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked"),
        };

        public string StatusText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("状态：未找到已发布版本，仓库可能尚未公开", "Status: no release/tag found; the repository may not be public yet"),
            UpdateStatusKind.Uncomparable => LocalizationManager.Text("状态：已获取线上版本，但版本号格式无法比较", "Status: remote version found, but its format cannot be compared"),
            UpdateStatusKind.UpdateAvailable => LocalizationManager.Text("状态：发现新版本", "Status: update available"),
            UpdateStatusKind.UpToDate => LocalizationManager.Text("状态：当前已是最新版本", "Status: up to date"),
            UpdateStatusKind.LocalNewer => LocalizationManager.Text("状态：当前版本高于线上最新版本", "Status: local version is newer than the latest online version"),
            UpdateStatusKind.NetworkError => LocalizationManager.Formatted($"状态：网络请求失败（{Detail ?? "连接错误"}）", (System.FormattableString)$"Status: network request failed ({Detail ?? "connection error"})"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("状态：检查超时，请稍后重试", "Status: update check timed out; try again later"),
            _ => LocalizationManager.Text("状态：检查更新时发生错误", "Status: an error occurred while checking for updates"),
        };
    }

    public static UpdateCheckResult? LastUpdateResult => _lastUpdateResult;

    private AppSettings _settings;
    private readonly HudWindow _hud;
    private readonly CustomHudRuntime _runtime;
    private bool _monitorComboReady;
    private bool _applyingPositionUi;
    internal Action<AppSettings> PersistLanguagePreference {get;set;}=SettingsManager.SaveImmediatePreferences;
    private string? _maintenanceStatusZh;
    private string? _maintenanceStatusEn;
    private object?[]? _maintenanceStatusArguments;
    private string? _startupStatusZh;
    private string? _startupStatusEn;

    public SettingsWindow(AppSettings settings, HudWindow hud, CustomHudRuntime runtime)
    {
        InitializeComponent();
        InitializeWindowChrome();
        InitializeRepositoryLinks();
        InitializeAboutDetails();
        _settings = settings;
        _hud = hud;
        _runtime = runtime;
        InitializeDataManagement();
        InitializeHudHotkeyEditor();
        InitializeHotZoneEditor();
        InitializeHardwarePanel();
        ManualView.NavigationRequested+=NavigateToSettingsSection;

        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();
        RefreshWindowChrome();
        RefreshRepositoryLinks();

        LanguageButton.Click+=async (_,_)=>await new LanguageSelectionWindow(
            ()=>_rootSettings,OnLanguageSelected,ApplyGlobalAppearanceAsync).ShowDialog(this);

        InitializeFontChoices();
        ApplySettingsToControls(settings);
        UpdateLanguageSwitchVisual();
        InitializeInstanceEditor();
        InitializeWorkspace();
        InitializeRuntimeSelection();

        HudEnabledSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        AlwaysVisibleSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        PositionModeCombo.SelectionChanged += (_, _) => UpdateModeUi();
        PositionCombo.SelectionChanged += OnPositionPresetChanged;
        PersistentLayerCombo.SelectionChanged+=(_,_)=>RefreshLayerDescription();
        SaveBtn.Click += OnSave;
        OpenSettingsFolderBtn.Click += OnOpenSettingsFolder;
        ResetSizeAnimationBtn.Click += OnResetSizeAnimation;
        ExportSettingsBtn.Click += OnExportSettings;
        ImportSettingsBtn.Click += OnImportSettings;
        BackupSettingsBtn.Click += OnBackupSettings;
        OpenLogsFolderBtn.Click += OnOpenLogsFolder;
        ResetAllSettingsBtn.Click += OnResetAllSettings;
        HudOpacitySlider.ValueChanged += (_, _) => UpdateOpacityLabel();
        OpenIssueTrackerBtn.Click += (_, _) => OpenUrl("https://ecp-bugs.x-neko.com");
        OpenWebsiteBtn.Click += (_, _) => OpenUrl("https://zmd-bar.x-neko.com");
        CheckUpdateBtn.Click += OnCheckUpdate;

        AboutArchitectureText.Text = $"Windows · {GetCurrentProcessArchitectureText()}";

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        BuildDateText.Text=LocalizationManager.Formatted($"构建日期  {ProductInfo.BuildDate}",$"Build  {ProductInfo.BuildDate}");
        Title = EndfieldChargePlus.Interop.ProcessPrivileges.SettingsTitle(currentVersion,ProductInfo.Name);
        UpdateCurrentVersionText.Text = LocalizationManager.Formatted($"当前版本：v{currentVersion}", (System.FormattableString)$"Current: v{currentVersion}");
        UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked");
        UpdateStatusText.Text = LocalizationManager.Text("状态：尚未检查", "Status: not checked");
        if (LastUpdateResult is { } cachedUpdateResult)
            ApplyUpdateCheckResult(cachedUpdateResult);

        Opened += (_, _) => PopulateMonitorCombo();
        // Both installed/portable copies and Store apps must show the actual
        // Windows startup state instead of a potentially stale JSON preference.
        StartupSwitch.IsEnabled = false;
        Opened += async (_, _) => await RefreshStartupSwitchAsync();

        ManualView.ApplyLocalization();
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
        bool previousPositionUi=_applyingPositionUi;
        _applyingPositionUi=true;
        try
        {
            int layerIndex = PersistentLayerCombo.SelectedIndex;
            int modeIndex = PositionModeCombo.SelectedIndex;
            int positionIndex = PositionCombo.SelectedIndex;

            PersistentLayerCombo.Items.Clear();
            PersistentLayerCombo.Items.Add(LocalizationManager.Text("桌面层（不置顶）", "Desktop Layer"));
            PersistentLayerCombo.Items.Add(LocalizationManager.Text("始终置顶", "Always on Top"));
            PersistentLayerCombo.Items.Add(LocalizationManager.Text("超级置顶（实验性）", "Super Topmost (Experimental)"));
            RefreshLayerDescription();

            PositionModeCombo.Items.Clear();
            PositionModeCombo.Items.Add(LocalizationManager.Text("预设位置 + 微调", "Preset + Offset"));
            PositionModeCombo.Items.Add(LocalizationManager.Text("自定义坐标", "Custom Coordinates"));

            PositionCombo.Items.Clear();
            string[] zh = ["左上（工作区）", "顶部居中（工作区）", "右上（工作区）", "左侧居中（工作区）", "居中（工作区）", "右侧居中（工作区）", "左下（任务栏 / Dock 上）", "底部居中（任务栏 / Dock 上）", "右下（任务栏 / Dock 上）", "屏幕左上角", "屏幕顶部居中", "屏幕右上角", "屏幕左侧居中", "屏幕正中央", "屏幕右侧居中", "屏幕左下角", "屏幕底部居中", "屏幕右下角"];
            string[] en = ["Top Left (work area)", "Top Center (work area)", "Top Right (work area)", "Center Left (work area)", "Center (work area)", "Center Right (work area)", "Bottom Left (above taskbar / Dock)", "Bottom Center (above taskbar / Dock)", "Bottom Right (above taskbar / Dock)", "Screen Top Left", "Screen Top Center", "Screen Top Right", "Screen Center Left", "Screen Center", "Screen Center Right", "Screen Bottom Left", "Screen Bottom Center", "Screen Bottom Right"];
            for (int i = 0; i < zh.Length; i++)
                PositionCombo.Items.Add(LocalizationManager.Text(zh[i], en[i]));

            if (layerIndex >= 0) PersistentLayerCombo.SelectedIndex = Math.Min(layerIndex, PersistentLayerCombo.Items.Count - 1);
            if (modeIndex >= 0) PositionModeCombo.SelectedIndex = Math.Min(modeIndex, PositionModeCombo.Items.Count - 1);
            if (positionIndex >= 0) PositionCombo.SelectedIndex = Math.Min(positionIndex, PositionCombo.Items.Count - 1);
        }
        finally { _applyingPositionUi=previousPositionUi; }
    }

    private async void OnLanguageSelected(string preference)
    {
        preference=LocalizationManager.NormalizePreference(preference);
        bool languageChanged = _settings.UiLanguage != preference;

        _settings = _settings with { UiLanguage = preference };
        LocalizationManager.SetPreference(preference);

        ApplyLocalization();
        UpdateLanguageSwitchVisual();

        // Language is an immediate UI preference. Persist only the language choice here;
        // other editor changes still require Save & Apply.
        _rootSettings = _rootSettings with { UiLanguage = _settings.UiLanguage };
        _persistedRootSettings=_persistedRootSettings with { UiLanguage=_rootSettings.UiLanguage };
        try {PersistLanguagePreference(_persistedRootSettings);}
        catch(Exception ex)
        {
            Diagnostics.AppLog.Error("Unable to save the language preference.",ex);
            MaintenanceStatusText.Text=ex.Message;
            _ = SaveErrorDialog.Show(this,ex);
            ToolTip.SetTip(LanguageButton,ex.Message);
        }

        // Persistent and transient HUD renderers read LocalizationManager at render time,
        // so their next live refresh switches language without replacing unsaved settings.
        if (languageChanged)
            await Task.Delay(1);
    }

    private void ApplyLocalization()
    {
        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();
        RefreshWindowChrome();
        RefreshRepositoryLinks();
        if (_monitorComboReady)
            PopulateMonitorCombo(force: true);

        Customizer.ApplyLocalization();
        if (_workspaceReady) { RefreshWorkspaceLocalization(); RefreshQuickActionsLocalization(); }
        ApplyInstanceEditorLocalization();

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        BuildDateText.Text=LocalizationManager.Formatted($"构建日期  {ProductInfo.BuildDate}",$"Build  {ProductInfo.BuildDate}");
        Title = EndfieldChargePlus.Interop.ProcessPrivileges.SettingsTitle(currentVersion,ProductInfo.Name);
        UpdateCurrentVersionText.Text = LocalizationManager.Formatted($"当前版本：v{currentVersion}", (System.FormattableString)$"Current: v{currentVersion}");
        if (LastUpdateResult is { } result)
            ApplyUpdateCheckResult(result);
        else
        {
            UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未获取", "Latest: not checked");
            UpdateStatusText.Text = LocalizationManager.Text("状态：尚未检查", "Status: not checked");
        }

        RefreshMaintenanceStatusLocalization();
        RefreshStartupStatusLocalization();
        RefreshHudHotkeyLocalization();
        RebuildHotZoneChoices();
        ApplyHardwareLocalization();
        ManualView.ApplyLocalization();
        UpdateLanguageSwitchVisual();
    }

    private void SetStartupStatus(string? zh, string? en)
    {
        _startupStatusZh = zh;
        _startupStatusEn = en;
        RefreshStartupStatusLocalization();
    }

    private void RefreshStartupStatusLocalization()
    {
        StartupStatusText.IsVisible = _startupStatusZh is not null && _startupStatusEn is not null;
        if (StartupStatusText.IsVisible)
            StartupStatusText.Text = LocalizationManager.Text(_startupStatusZh!, _startupStatusEn!);
    }

    private async Task RefreshStartupSwitchAsync()
    {
        StartupApplyResult current = await StartupManager.ReadStatusAsync();
        if (current.Succeeded)
        {
            StartupSwitch.IsChecked = current.IsEnabled;
            _settings = _settings with { StartWithWindows = current.IsEnabled };
            if (current.State == "DisabledByUser")
                SetStartupStatus("此应用已在 Windows 启动应用中被禁用，请在那里手动重新启用。",
                    "Windows disabled startup for this app. Re-enable it in Windows Startup Apps.");
            else if (current.State is "DisabledByPolicy" or "EnabledByPolicy")
                SetStartupStatus("开机启动受系统策略管理。", "Startup is managed by system policy.");
            else
                SetStartupStatus(null, null);
        }
        else
            SetStartupStatus("无法读取 Windows 启动任务状态。请查看日志。",
                "Cannot read the Windows startup task. See logs.");
        StartupSwitch.IsEnabled = current.Succeeded && current.State is not ("DisabledByPolicy" or "EnabledByPolicy");
    }

    private void SetMaintenanceStatus(string zh, string en)
    {
        _maintenanceStatusZh = zh;
        _maintenanceStatusEn = en;
        _maintenanceStatusArguments=null;
        RefreshMaintenanceStatusLocalization();
    }

    private void SetMaintenanceStatus(string zh,FormattableString en)
    {
        _maintenanceStatusZh=zh;_maintenanceStatusEn=en.Format;_maintenanceStatusArguments=en.GetArguments();RefreshMaintenanceStatusLocalization();
    }

    private void RefreshMaintenanceStatusLocalization()
    {
        if (_maintenanceStatusZh is null || _maintenanceStatusEn is null)
            return;

        MaintenanceStatusText.Text = _maintenanceStatusArguments is null ? LocalizationManager.Text(_maintenanceStatusZh, _maintenanceStatusEn)
            : LocalizationManager.Formatted(_maintenanceStatusZh,System.Runtime.CompilerServices.FormattableStringFactory.Create(_maintenanceStatusEn,_maintenanceStatusArguments));
    }

    private void UpdateLanguageSwitchVisual()
    {
        FlowDirection=LocalizationManager.IsRightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        // JSON, tokens, formulas and numeric input keep their stable reading order.
        foreach(var box in this.GetLogicalDescendants().OfType<TextBox>())
            box.FlowDirection=box.Name?.Contains("Search",StringComparison.Ordinal)==true || box.Name is "ProfileNameBox" or "TriggerNameBox" ? FlowDirection : Avalonia.Media.FlowDirection.LeftToRight;
        string languageLabel="Language / "+LocalizationManager.CurrentLanguage.LanguageWord;
        LanguageButtonLabel.Text=languageLabel;
        ToolTip.SetTip(LanguageButton,languageLabel);
        Avalonia.Automation.AutomationProperties.SetName(LanguageButton,languageLabel);
        GlobalAppearance.Apply(this,_rootSettings.GlobalFontScale,_rootSettings.GlobalUiScale);
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
            MonitorCombo.Items.Add(LocalizationManager.Formatted($"显示器 {i + 1} · {area.Width}×{area.Height}{primary}", (System.FormattableString)$"Display {i + 1} · {area.Width}×{area.Height}{primary}"));
        }

        int wanted = _settings.MonitorIndex < 0 ? 0 : _settings.MonitorIndex + 1;
        MonitorCombo.SelectedIndex = wanted >= 0 && wanted < MonitorCombo.Items.Count ? wanted : 0;
    }

    private CheckBox ClickToCycleSwitch => Customizer.FindControl<CheckBox>("PersistentClickSwitch")!;

    private void UpdateModeUi()
    {
        bool multi=_multiSwitch?.IsChecked ?? _rootSettings.MultiHudEnabled;
        bool anyPersistent=(string.IsNullOrEmpty(_selectedInstanceId) ? AlwaysVisibleSwitch.IsChecked==true : _rootSettings.AlwaysVisible)
            || (multi && _rootSettings.HudInstances.Any(i=>i.Enabled && (i.Id==_selectedInstanceId ? AlwaysVisibleSwitch.IsChecked==true : i.Settings.AlwaysVisible)));
        ClickToCycleSwitch.IsEnabled=anyPersistent;
        bool persistent = AlwaysVisibleSwitch.IsChecked == true;
        HotZonePanel.IsEnabled=!persistent;HotZonePanel.Opacity=persistent ? .45 : 1;
        PersistentLayerCombo.IsEnabled = persistent;
        PersistentLayerCombo.Opacity = persistent ? 1.0 : 0.45;

        bool custom = PositionModeCombo.SelectedIndex == 1;
        PresetPositionPanel.IsEnabled = !custom;
        PresetPositionPanel.Opacity = custom ? 0.40 : 1.0;
        CustomPositionPanel.IsEnabled = custom;
        CustomPositionPanel.Opacity = custom ? 1.0 : 0.40;
    }

    private void OnPositionPresetChanged(object? sender,SelectionChangedEventArgs e)
    {
        if (_applyingPositionUi || PositionCombo.SelectedIndex<0) return;
        OffsetXBox.Value=0;
        OffsetYBox.Value=0;
    }

    private void ApplyEditorControls(AppSettings settings)
    {
        bool previousPositionUi=_applyingPositionUi;
        _applyingPositionUi=true;
        try
        {
            _settings = settings;
            LoadHotZoneControls(settings);

            ScaleBox.Value = (decimal)settings.GlobalScale;
            DurationBox.Value = (decimal)settings.DisplayDurationSeconds;
            BounceBox.Value = (decimal)settings.BounceStrength;
            RippleIntensityBox.Value = (decimal)settings.RippleIntensity;
            RippleSpreadBox.Value = (decimal)settings.RippleSpread;
            SamplingIntervalBox.Value = (decimal)_rootSettings.SamplingIntervalSeconds;
            HudOpacitySlider.Value = Math.Clamp(settings.HudOpacity * 100.0, 10.0, 100.0);
            UpdateOpacityLabel();

            if(!string.IsNullOrWhiteSpace(settings.HudFontFamily) && !HudFontCombo.Items.Contains(settings.HudFontFamily))HudFontCombo.Items.Add(settings.HudFontFamily);
            HudFontCombo.SelectedIndex = 0;
            for (int i=1; i<HudFontCombo.Items.Count; i++)
                if (string.Equals(HudFontCombo.Items[i]?.ToString(), settings.HudFontFamily, StringComparison.OrdinalIgnoreCase)) HudFontCombo.SelectedIndex=i;
            ClickToCycleSwitch.IsChecked = _rootSettings.ClickToCycle;
            HudEnabledSwitch.IsChecked = settings.HudEnabled;
            StartupSwitch.IsChecked = settings.StartWithWindows;
            AlwaysVisibleSwitch.IsChecked = settings.AlwaysVisible;
            PersistentLayerCombo.SelectedIndex = settings.PersistentLayer == PersistentHudLayer.Desktop ? 0 : settings.PersistentLayer == PersistentHudLayer.SuperTopmost ? 2 : 1;
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

            Customizer.SetSamplingInterval(_rootSettings.SamplingIntervalSeconds);
            Customizer.Load(settings.CustomHud, _runtime.GetInstanceHud(_selectedInstanceId));
            string previewId=_selectedInstanceId;
            Customizer.PreviewPresenter=(data,refresh)=>_settingsEditorOpen && previewId==_selectedInstanceId ? _runtime.PreviewInstanceAsync(previewId,data,refresh) : Task.CompletedTask;
            UpdateModeUi();
        }
        finally { _applyingPositionUi=previousPositionUi; }
    }

    private AppSettings CollectEditorSettings()
        => _settings with
        {
            HudEnabled = HudEnabledSwitch.IsChecked == true,
            StartWithWindows = StartupSwitch.IsChecked == true,
            UiLanguage = _rootSettings.UiLanguage,
            Workspace = WorkspacePreferences.Normalize(_workspaceDraft),
            GlobalFontScale = _rootSettings.GlobalFontScale,
            GlobalUiScale = _rootSettings.GlobalUiScale,
            GlobalScale = (double)(ScaleBox.Value ?? (decimal)AppSettings.DefaultGlobalScale),
            DisplayDurationSeconds = (double)(DurationBox.Value ?? (decimal)AppSettings.DefaultDisplayDurationSeconds),
            BounceStrength = (double)(BounceBox.Value ?? (decimal)AppSettings.DefaultBounceStrength),
            RippleIntensity = (double)(RippleIntensityBox.Value ?? (decimal)AppSettings.DefaultRippleIntensity),
            RippleSpread = (double)(RippleSpreadBox.Value ?? (decimal)AppSettings.DefaultRippleSpread),
            SamplingIntervalSeconds = (double)(SamplingIntervalBox.Value ?? 1m),
            HudOpacity = Math.Clamp(HudOpacitySlider.Value / 100.0, 0.10, 1.0),

            HudFontFamily = HudFontCombo.SelectedIndex > 0 ? HudFontCombo.SelectedItem?.ToString() ?? "" : "",
            ClickToCycle = ClickToCycleSwitch.IsChecked == true,
            AlwaysVisible = AlwaysVisibleSwitch.IsChecked == true,
            HotZoneEnabled=HotZoneEnabledCheck.IsChecked==true,
            TransientHoverHold=_transientHoverHold.IsChecked==true,HotZonePosition=(HudPosition)Math.Max(0,HotZonePositionCombo.SelectedIndex),
            HotZoneWidth=(int)(HotZoneWidthBox.Value ?? 240),HotZoneHeight=(int)(HotZoneHeightBox.Value ?? 20),
            HotZoneOffsetX=(int)(HotZoneOffsetXBox.Value ?? 0),HotZoneOffsetY=(int)(HotZoneOffsetYBox.Value ?? 0),
            HotZoneDwellMilliseconds=(int)HotZoneDwellSlider.Value,HotZoneAdvanceQueue=HotZoneAdvanceCheck.IsChecked==true,
            PersistentLayer = PersistentLayerCombo.SelectedIndex == 0 ? PersistentHudLayer.Desktop : PersistentLayerCombo.SelectedIndex == 2 ? PersistentHudLayer.SuperTopmost : PersistentHudLayer.Topmost,

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
        if (!SaveBtn.IsEnabled) return;
        try
        {
        SaveBtn.IsEnabled=false;
        SaveBtn.Content=LocalizationManager.Text("应用中…","Applying…");
        LanguageButton.IsEnabled=false;
        var editorTabs=this.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
        if(editorTabs is not null) editorTabs.IsEnabled=false;
        _settings = CollectSettingsFromUi();

        // Store uses StartupTask; Setup/portable copies use the owned HKCU entry.
        bool requestedStartup = _settings.StartWithWindows;
        StartupApplyResult startup = await StartupManager.ApplyAsync(requestedStartup);
        if(startup.Error is not null)throw new InvalidOperationException(LocalizationManager.Text("保存失败","Save failed"),startup.Error);
        if(!startup.Succeeded && !startup.IsPackaged)throw new InvalidOperationException(startup.State=="DisabledByUser"
            ? LocalizationManager.Text("Windows 已禁用此应用开机启动，请到「设置 → 应用 → 启动」手动开启。","Windows disabled startup for this app. Enable it in Settings > Apps > Startup.")
            : LocalizationManager.Text("未能更改开机启动状态。请检查 Windows 启动应用与日志。","Could not change startup state. Check Windows Startup Apps and logs."));
        {
            _settings = _settings with { StartWithWindows = startup.IsEnabled };
            StartupSwitch.IsChecked = startup.IsEnabled;
            if (requestedStartup != startup.IsEnabled || !startup.Succeeded)
                SetStartupStatus(
                    startup.State == "DisabledByUser"
                        ? "Windows 已禁用此应用开机启动，请到「设置 → 应用 → 启动」手动开启。"
                        : "未能更改开机启动状态。请检查 Windows 启动应用与日志。",
                    startup.State == "DisabledByUser"
                        ? "Windows disabled startup for this app. Enable it in Settings > Apps > Startup."
                        : "Could not change startup state. Check Windows Startup Apps and logs.");
            else SetStartupStatus(null, null);
        }
        await SaveSettingsWithHudHotkeyAsync(_settings);
        _persistedRootSettings=_settings;
        RefreshHudHotkeyRegistrationStatus();

        SaveBtn.IsEnabled = false;
        SaveBtn.Content = LocalizationManager.Text("应用中…", "Applying…");
        try
        {
            await _runtime.ApplySavedSettingsAsync(_settings);
            _rootSettings=_settings;
            _settings=string.IsNullOrEmpty(_selectedInstanceId) ? _rootSettings : _rootSettings.HudInstances.FirstOrDefault(i=>i.Id==_selectedInstanceId)?.Settings ?? _rootSettings;
            RefreshInstanceEditor();
            await RefreshEditingHighlightAsync();
        }
        finally
        {
            SaveBtn.IsEnabled = true;
            if(editorTabs is not null) editorTabs.IsEnabled=true;
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
        catch (Exception ex)
        {
            SaveBtn.IsEnabled = true;
            foreach(var tabs in this.GetLogicalDescendants().OfType<TabControl>()) tabs.IsEnabled=true;
            SaveBtn.Content = LocalizationManager.Text("保存失败", "Save failed");
            MaintenanceStatusText.Text = ex.Message;
            _ = SaveErrorDialog.Show(this,ex);
            EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to apply settings.", ex);
        }
        finally {LanguageButton.IsEnabled=true;}
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
            _ = SaveErrorDialog.Show(this,ex);
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
            StartupApplyResult importedStartup = await StartupManager.ApplyAsync(imported.StartWithWindows);
            imported = imported with { StartWithWindows = importedStartup.IsEnabled };
            SaveSettingsWithHudHotkey(imported);
            if (!importedStartup.Succeeded)
                SetStartupStatus("导入的开机启动设置未能应用，请检查 Windows 启动应用。",
                    "Imported startup preference could not be applied. Check Windows Startup Apps.");
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
                    (System.FormattableString)$"Config imported and applied; previous config backed up as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to import settings from the settings UI.", ex);
            _ = SaveErrorDialog.Show(this,ex);
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
                    (System.FormattableString)$"Config backed up: {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to create a manual settings backup.", ex);
            _ = SaveErrorDialog.Show(this,ex);
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
        => typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        SetAboutActionLabel(CheckUpdateBtn,"refresh-cw",LocalizationManager.Text("检查中…", "Checking…"));
        UpdateStatusText.Text = LocalizationManager.Text("状态：正在连接 GitHub 检查更新…", "Status: checking GitHub…");

        UpdateCheckResult result = await CheckForUpdatesAsync();
        ApplyUpdateCheckResult(result);

        SetAboutActionLabel(CheckUpdateBtn,"refresh-cw",LocalizationManager.Text("检查更新", "Check Updates"));
        CheckUpdateBtn.IsEnabled = true;
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        string currentText = GetCurrentVersionText();

        try
        {
            string? latestTag = await TryGetLatestReleaseTagAsync();

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
        UpdateCurrentVersionText.Text = LocalizationManager.Formatted($"当前版本：v{result.CurrentVersion}", (System.FormattableString)$"Current: v{result.CurrentVersion}");
        UpdateLatestVersionText.Text = result.LatestVersionText;
        UpdateStatusText.Text = result.StatusText;
    }

    private static Task<string?> TryGetLatestReleaseTagAsync()
        => GitHubReleaseUpdate.FetchTagAsync(UpdateHttpClient);

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
        try
        {
            Directory.CreateDirectory(SettingsManager.SettingsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsManager.SettingsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { AppLog.Error("Unable to open the configuration directory.", ex); }
    }

    private static int PositionToIndex(HudPosition p) => Math.Clamp((int)p,0,17);

    private static HudPosition IndexToPosition(int index) => index is >=0 and <=17 ? (HudPosition)index : HudPosition.TopCenter;
}
