using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Customization;

public partial class HudCustomizerView : UserControl
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private const string HttpJsonCommentExampleZh = """
// HTTP / JSON 数据源示例（本示例全部为注释，不会发起任何网络请求）
// 使用方法：复制下面示例，去掉每行开头的 //，再按实际接口修改。
//
// 字段说明：
// name            数据源名称；变量前缀为 custom.<name>.*
// enabled         是否启用该数据源
// url             HTTP/HTTPS GET 接口地址，接口需要返回 JSON
// refreshSeconds  刷新/缓存间隔（秒）
// headers         可选请求头；敏感值建议使用 ${env:环境变量名}
// fields.variable 自定义变量名
// fields.jsonPath JSON 字段路径，例如 data.cpu、players[0].name
//
// [
//   {
//     "name": "myserver",
//     "enabled": true,
//     "url": "https://example.com/status",
//     "refreshSeconds": 10,
//     "headers": {
//       "Authorization": "Bearer ${env:MY_API_KEY}",
//       "Accept": "application/json"
//     },
//     "fields": [
//       {
//         "variable": "cpu",
//         "jsonPath": "data.cpu"
//       },
//       {
//         "variable": "players",
//         "jsonPath": "data.players.online"
//       },
//       {
//         "variable": "online",
//         "jsonPath": "data.server.online"
//       }
//     ]
//   }
// ]
//
// 上述示例启用后会生成：
// custom.myserver.cpu
// custom.myserver.players
// custom.myserver.online
""";

    private const string HttpJsonCommentExampleEn = """
// HTTP / JSON data source example (comment-only; no network request is made)
// Usage: copy the example below, remove the leading // on each line, then edit it for your API.
//
// Fields:
// name            Data source name; variable prefix is custom.<name>.*
// enabled         Enable or disable this source
// url             HTTP/HTTPS GET endpoint that returns JSON
// refreshSeconds  Refresh/cache interval in seconds
// headers         Optional request headers; secrets may use ${env:VARIABLE_NAME}
// fields.variable Custom variable name
// fields.jsonPath JSON path such as data.cpu or players[0].name
//
// [
//   {
//     "name": "myserver",
//     "enabled": true,
//     "url": "https://example.com/status",
//     "refreshSeconds": 10,
//     "headers": {
//       "Authorization": "Bearer ${env:MY_API_KEY}",
//       "Accept": "application/json"
//     },
//     "fields": [
//       {
//         "variable": "cpu",
//         "jsonPath": "data.cpu"
//       },
//       {
//         "variable": "players",
//         "jsonPath": "data.players.online"
//       },
//       {
//         "variable": "online",
//         "jsonPath": "data.server.online"
//       }
//     ]
//   }
// ]
//
// Once enabled, the example creates:
// custom.myserver.cpu
// custom.myserver.players
// custom.myserver.online
""";

    private static string HttpJsonCommentExample =>
        LocalizationManager.IsEnglish ? HttpJsonCommentExampleEn : HttpJsonCommentExampleZh;
    private List<HudProfile> _profiles = new();
    private List<CustomHttpSource> _httpSources = new();
    private List<string> _cycleProfileIds = new();
    private List<string> _cycleSourceProfileIds = new();
    private IReadOnlyList<GpuAdapterInfo> _gpuAdapters = Array.Empty<GpuAdapterInfo>();
    private HudWindow? _hud;
    private CustomHudSettings _loaded = CustomHudSettings.CreateDefault();
    private int _selectedIndex = -1;
    private bool _loading;
    private readonly VariableHub _previewVariables = new();

    public HudCustomizerView()
    {
        InitializeComponent();

        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();
        ProbeProtocolCombo.SelectionChanged += (_, _) => UpdateContextOptionsUi();

        foreach (var icon in IconCatalog.Names)
        {
            LeftIconCombo.Items.Add(icon);
            RightIconCombo.Items.Add(icon);
        }

        ProfileCombo.SelectionChanged += OnProfileChanged;
        AutoCycleSwitch.IsCheckedChanged += (_, _) => UpdateCycleControls();
        CycleProfileList.SelectionChanged += (_, _) => UpdateCycleButtons();
        CycleAddBtn.Click += OnCycleAdd;
        CycleRemoveBtn.Click += OnCycleRemove;
        CycleMoveUpBtn.Click += OnCycleMoveUp;
        CycleMoveDownBtn.Click += OnCycleMoveDown;
        TimeTargetSwitch.IsCheckedChanged += (_, _) => UpdateContextOptionsUi();
        AddProfileBtn.Click += OnAddProfile;
        SaveProfileBtn.Click += OnSaveProfile;
        DeleteProfileBtn.Click += OnDeleteProfile;
        PreviewBtn.Click += OnPreview;

        VariableSearchBox.TextChanged += (_, _) => RefreshVariableList();
        VariableCategoryCombo.SelectionChanged += (_, _) => RefreshVariableList();
        VariableList.SelectionChanged += OnVariableSelected;
        CopyVariableKeyBtn.Click += OnCopyVariableKey;
        CopyVariableTemplateBtn.Click += OnCopyVariableTemplate;

        RefreshGpuAdapters();
        RebuildVariableCategories();
        ClearVariableDetail();
        UpdateContextOptionsUi();
    }

    private void RebuildLocalizedChoiceItems()
    {
        int animation = AnimationModeCombo.SelectedIndex;
        int cycleAnimation = CycleAnimationModeCombo.SelectedIndex;
        int displayUnit = NetworkDisplayUnitCombo.SelectedIndex;
        int percentMode = NetworkPercentModeCombo.SelectedIndex;
        int referenceUnit = NetworkReferenceUnitCombo.SelectedIndex;
        int protocol = ProbeProtocolCombo.SelectedIndex;

        AnimationModeCombo.Items.Clear();
        AnimationModeCombo.Items.Add(LocalizationManager.Text("简洁", "Simple"));
        AnimationModeCombo.Items.Add(LocalizationManager.Text("完整", "Full"));

        CycleAnimationModeCombo.Items.Clear();
        CycleAnimationModeCombo.Items.Add(LocalizationManager.Text("简洁", "Simple"));
        CycleAnimationModeCombo.Items.Add(LocalizationManager.Text("完整", "Full"));

        NetworkDisplayUnitCombo.Items.Clear();
        NetworkDisplayUnitCombo.Items.Add("Mbps");
        NetworkDisplayUnitCombo.Items.Add(LocalizationManager.Text("KB/s / MB/s（自动）", "KB/s / MB/s (Auto)"));

        NetworkPercentModeCombo.Items.Clear();
        NetworkPercentModeCombo.Items.Add(LocalizationManager.Text("总吞吐量（下载 + 上传）", "Total (Down + Up)"));
        NetworkPercentModeCombo.Items.Add(LocalizationManager.Text("仅下载", "Download"));
        NetworkPercentModeCombo.Items.Add(LocalizationManager.Text("仅上传", "Upload"));
        NetworkPercentModeCombo.Items.Add(LocalizationManager.Text("取下载 / 上传较大值", "Larger of Down / Up"));

        NetworkReferenceUnitCombo.Items.Clear();
        NetworkReferenceUnitCombo.Items.Add("Mbps");
        NetworkReferenceUnitCombo.Items.Add("KB/s");
        NetworkReferenceUnitCombo.Items.Add("MB/s");

        ProbeProtocolCombo.Items.Clear();
        ProbeProtocolCombo.Items.Add("ICMP");
        ProbeProtocolCombo.Items.Add("TCP");
        ProbeProtocolCombo.Items.Add("UDP");

        if (animation >= 0) AnimationModeCombo.SelectedIndex = Math.Min(animation, AnimationModeCombo.Items.Count - 1);
        if (cycleAnimation >= 0) CycleAnimationModeCombo.SelectedIndex = Math.Min(cycleAnimation, CycleAnimationModeCombo.Items.Count - 1);
        if (displayUnit >= 0) NetworkDisplayUnitCombo.SelectedIndex = Math.Min(displayUnit, NetworkDisplayUnitCombo.Items.Count - 1);
        if (percentMode >= 0) NetworkPercentModeCombo.SelectedIndex = Math.Min(percentMode, NetworkPercentModeCombo.Items.Count - 1);
        if (referenceUnit >= 0) NetworkReferenceUnitCombo.SelectedIndex = Math.Min(referenceUnit, NetworkReferenceUnitCombo.Items.Count - 1);
        if (protocol >= 0) ProbeProtocolCombo.SelectedIndex = Math.Min(protocol, ProbeProtocolCombo.Items.Count - 1);
    }

    public void ApplyLocalization()
    {
        string? selectedId = _selectedIndex >= 0 && _selectedIndex < _profiles.Count
            ? _profiles[_selectedIndex].Id
            : null;
        bool previousLoading = _loading;
        _loading = true;

        try
        {
            LocalizationManager.ApplyStaticText(this);
            RebuildLocalizedChoiceItems();

            if (_httpSources.Count == 0 && string.IsNullOrWhiteSpace(StripHttpJsonCommentLines(HttpSourcesJsonBox.Text)))
                HttpSourcesJsonBox.Text = HttpJsonCommentExample;

            // Rebuilding the ComboBox raises SelectionChanged. Suppress profile reload here so
            // a language switch never discards unsaved editor text.
            RebuildProfileCombo(selectedId);
            _selectedIndex = ProfileCombo.SelectedIndex;

            if (_selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            {
                var profile = _profiles[_selectedIndex];
                LocalizeBuiltInEditorText(profile);
                ProfileKindText.Text = profile.IsBuiltIn
                    ? LocalizationManager.Text("内置方案 · 保存修改时创建副本", "Built-in · saving changes creates a copy")
                    : LocalizationManager.Text("自定义方案", "Custom Profile");
            }

            RefreshCycleEditor();
            RebuildVariableCategories();
            RefreshVariableList();
            if (VariableList.SelectedItem is null)
                ClearVariableDetail();
            UpdateContextOptionsUi();
            UpdateCycleControls();
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    private void LocalizeBuiltInEditorText(HudProfile profile)
    {
        if (!(profile.IsBuiltIn || !string.IsNullOrWhiteSpace(profile.BuiltInKey)))
            return;

        var zh = BuiltInProfileLocalization.ForLanguage(profile, AppLanguage.SimplifiedChinese);
        var en = BuiltInProfileLocalization.ForLanguage(profile, AppLanguage.English);
        var desired = BuiltInProfileLocalization.ForCurrentLanguage(profile);

        string zhName = $"{zh.Category} - {zh.Name}";
        string enName = $"{en.Category} - {en.Name}";
        string desiredName = $"{desired.Category} - {desired.Name}";
        string currentName = ProfileNameBox.Text ?? string.Empty;
        if (string.Equals(currentName, zhName, StringComparison.Ordinal)
            || string.Equals(currentName, enName, StringComparison.Ordinal))
            ProfileNameBox.Text = desiredName;

        TranslateCanonicalText(TaglineBox, zh.TaglineTemplate, en.TaglineTemplate, desired.TaglineTemplate);
        TranslateCanonicalText(TitleBox, zh.TitleTemplate, en.TitleTemplate, desired.TitleTemplate);
        TranslateCanonicalText(PrimaryBox, zh.PrimaryTemplate, en.PrimaryTemplate, desired.PrimaryTemplate);
        TranslateCanonicalText(SecondaryBox, zh.SecondaryTemplate, en.SecondaryTemplate, desired.SecondaryTemplate);
        TranslateCanonicalText(RightBox, zh.RightTemplate, en.RightTemplate, desired.RightTemplate);
        TranslateCanonicalText(RightSuffixBox, zh.RightSuffix, en.RightSuffix, desired.RightSuffix);
        TranslateCanonicalText(ProgressVariableBox, zh.ProgressVariable, en.ProgressVariable, desired.ProgressVariable);
    }

    private static void TranslateCanonicalText(TextBox box, string zh, string en, string desired)
    {
        string current = box.Text ?? string.Empty;
        if (string.Equals(current, zh, StringComparison.Ordinal) || string.Equals(current, en, StringComparison.Ordinal))
            box.Text = desired;
    }

    public void Load(CustomHudSettings? settings, HudWindow hud)
    {
        _loading = true;
        _hud = hud;
        _loaded = HudSettingsNormalizer.Normalize(settings ?? CustomHudSettings.CreateDefault());
        AutoCycleSwitch.IsChecked = _loaded.AutoCycle;
        CycleSecondsBox.Value = _loaded.CycleSeconds;
        CycleAnimationModeCombo.SelectedIndex = string.Equals(_loaded.CycleAnimationMode, "Simple", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        DeepSeekKeyBox.Text = SecretStore.Unprotect(_loaded.DeepSeekApiKeyProtected);
        DeepSeekWindowsBox.Text = _loaded.DeepSeekPeakWindows;

        _profiles = (_loaded.Profiles.Count == 0 ? CustomHudSettings.CreateDefault().Profiles : _loaded.Profiles)
            .Select(CloneProfile)
            .Select(HudSettingsNormalizer.NormalizeProfile)
            .ToList();

        var validProfileIds = _profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cycleProfileIds = (_loaded.CycleProfileIds ?? new List<string>())
            .Where(id => validProfileIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _httpSources = _loaded.HttpSources.Select(CloneHttp).ToList();
        HttpSourcesJsonBox.Text = _httpSources.Count == 0
            ? HttpJsonCommentExample
            : JsonSerializer.Serialize(_httpSources, _json);

        RefreshGpuAdapters();
        RebuildProfileCombo(_loaded.ActiveProfileId);
        _selectedIndex = ProfileCombo.SelectedIndex;
        LoadCurrentProfile();
        RefreshCycleEditor();

        RebuildVariableCategories();
        RefreshVariableList();
        _loading = false;
        UpdateContextOptionsUi();
        UpdateCycleControls();
    }

    public CustomHudSettings ExportSettings()
    {
        SaveCurrentProfile();
        TryParseHttpJson(showErrors: true);
        return new CustomHudSettings
        {
            AutoCycle = AutoCycleSwitch.IsChecked == true,
            CycleSeconds = (int)Math.Clamp(CycleSecondsBox.Value ?? 10, 3, 3600),
            CycleProfileIds = GetSanitizedCycleProfileIds(),
            CycleAnimationMode = CycleAnimationModeCombo.SelectedIndex == 0 ? "Simple" : "Full",
            ActiveProfileId = _selectedIndex >= 0 && _selectedIndex < _profiles.Count
                ? _profiles[_selectedIndex].Id
                : (_profiles.FirstOrDefault(p => string.Equals(p.Id, _loaded.ActiveProfileId, StringComparison.OrdinalIgnoreCase))?.Id
                   ?? _profiles.FirstOrDefault()?.Id
                   ?? ""),
            DeepSeekApiKeyProtected = SecretStore.Protect(DeepSeekKeyBox.Text),
            DeepSeekPeakWindows = string.IsNullOrWhiteSpace(DeepSeekWindowsBox.Text) ? "09:00-12:00;14:00-18:00" : DeepSeekWindowsBox.Text!.Trim(),
            Profiles = _profiles.Select(CloneProfile).ToList(),
            HttpSources = _httpSources.Select(CloneHttp).ToList()
        };
    }

    private void RebuildProfileCombo(string? preferredProfileId = null)
    {
        string? keepId = preferredProfileId;
        if (string.IsNullOrWhiteSpace(keepId) && _selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            keepId = _profiles[_selectedIndex].Id;

        ProfileCombo.Items.Clear();
        foreach (var profile in _profiles)
            ProfileCombo.Items.Add(ProfileDisplayName(profile));

        int selected = -1;
        if (!string.IsNullOrWhiteSpace(keepId))
            selected = _profiles.FindIndex(p => string.Equals(p.Id, keepId, StringComparison.OrdinalIgnoreCase));
        if (selected < 0 && _profiles.Count > 0)
            selected = 0;
        ProfileCombo.SelectedIndex = selected;
    }

    private static string ProfileDisplayName(HudProfile profile) =>
        BuiltInProfileLocalization.DisplayName(profile);

    private void SelectProfile(string profileId)
    {
        bool previousLoading = _loading;
        _loading = true;
        RebuildProfileCombo(profileId);
        _selectedIndex = ProfileCombo.SelectedIndex;
        LoadCurrentProfile();
        _loading = previousLoading;
        UpdateContextOptionsUi();
        RefreshCycleEditor();
    }

    private void OnProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _selectedIndex = ProfileCombo.SelectedIndex;
        LoadCurrentProfile();
        UpdateContextOptionsUi();
    }

    private void LoadCurrentProfile()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            ProfileNameBox.Text = "";
            ProfileStatusText.Text = "";
            ProfileKindText.Text = "";
            SetProfileEditorEnabled(false, false);
            UpdateContextOptionsUi();
            return;
        }

        bool previousLoading = _loading;
        _loading = true;
        var rawProfile = _profiles[_selectedIndex];
        var p = BuiltInProfileLocalization.ForCurrentLanguage(rawProfile);
        ProfileNameBox.Text = ProfileDisplayName(rawProfile);
        ProfileKindText.Text = rawProfile.IsBuiltIn
            ? LocalizationManager.Text("内置方案 · 保存修改时创建副本", "Built-in · saving changes creates a copy")
            : LocalizationManager.Text("自定义方案", "Custom Profile");
        AnimationModeCombo.SelectedIndex = string.Equals(p.AnimationMode, "Full", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TaglineBox.Text = p.TaglineTemplate;
        TitleBox.Text = p.TitleTemplate;
        PrimaryBox.Text = p.PrimaryTemplate;
        SecondaryBox.Text = p.SecondaryTemplate;
        RightBox.Text = p.RightTemplate;
        RightSuffixBox.Text = p.RightSuffix;
        ProgressVariableBox.Text = p.ProgressVariable;
        ProgressMinBox.Value = (decimal)p.ProgressMin;
        ProgressMaxBox.Value = (decimal)p.ProgressMax;
        LeftIconCombo.SelectedItem = p.LeftIcon;
        RightIconCombo.SelectedItem = p.RightIcon;
        AccentColorBox.Text = p.AccentColor;
        ColorRulesBox.Text = string.Join(Environment.NewLine,
            p.ColorRules.Select(r => $"{r.Variable} {r.Operator} {r.Value.ToString(CultureInfo.InvariantCulture)} => {r.Color}"));
        TimeTargetSwitch.IsChecked = p.TimeTargetEnabled;
        TimeTargetBox.Text = p.TimeTarget;
        SelectGpuAdapter(p.GpuAdapterId);
        NetworkDisplayUnitCombo.SelectedIndex = string.Equals(p.NetworkDisplayUnit, "Mbps", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        NetworkPercentModeCombo.SelectedIndex = p.NetworkPercentMode?.Trim().ToLowerInvariant() switch
        {
            "download" => 1,
            "upload" => 2,
            "max" => 3,
            _ => 0,
        };
        NetworkReferenceValueBox.Value = (decimal)Math.Max(0.001d, p.NetworkReferenceValue);
        NetworkReferenceUnitCombo.SelectedIndex = p.NetworkReferenceUnit switch
        {
            "Mbps" => 0,
            "KB/s" => 1,
            _ => 2,
        };
        PingTargetBox.Text = string.IsNullOrWhiteSpace(p.PingTarget) ? "1.1.1.1" : p.PingTarget;
        ProbeProtocolCombo.SelectedIndex = (p.ProbeProtocol ?? "ICMP").Trim().ToUpperInvariant() switch
        {
            "TCP" => 1,
            "UDP" => 2,
            _ => 0,
        };
        ProbePortBox.Value = Math.Clamp(p.ProbePort <= 0 ? 443 : p.ProbePort, 1, 65535);
        ProfileStatusText.Text = "";
        SetProfileEditorEnabled(true, !rawProfile.IsBuiltIn);
        _loading = previousLoading;
        UpdateContextOptionsUi();
    }

    private bool SaveCurrentProfile()
    {
        if (_loading || _selectedIndex < 0 || _selectedIndex >= _profiles.Count) return false;

        var old = _profiles[_selectedIndex];
        var editorBase = (old.IsBuiltIn || !string.IsNullOrWhiteSpace(old.BuiltInKey))
            ? BuiltInProfileLocalization.ForCurrentLanguage(old)
            : old;
        var edited = BuildEditedProfile(editorBase);

        if (old.IsBuiltIn || !string.IsNullOrWhiteSpace(old.BuiltInKey))
        {
            string enteredName = ProfileNameBox.Text?.Trim() ?? "";
            bool renamed = enteredName.Length > 0
                           && !string.Equals(enteredName, ProfileDisplayName(old), StringComparison.Ordinal);
            bool changed = renamed || !ProfilesFunctionallyEqual(editorBase, edited);
            if (!changed) return false;

            string desiredName = renamed
                ? enteredName
                : LocalizationManager.Text($"{ProfileDisplayName(old)}（已更改）", $"{ProfileDisplayName(old)} (Modified)");
            desiredName = MakeUniqueCustomName(desiredName);

            var custom = HudSettingsNormalizer.NormalizeProfile(edited with
            {
                Id = Guid.NewGuid().ToString("N"),
                IsBuiltIn = false,
                BuiltInKey = "",
                Category = "自定义",
                Name = desiredName
            });

            _profiles.Add(custom);
            SelectProfile(custom.Id);
            return true;
        }

        var normalized = HudSettingsNormalizer.NormalizeProfile(edited with
        {
            IsBuiltIn = false,
            BuiltInKey = "",
            Category = "自定义",
            Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? old.Name : ProfileNameBox.Text!.Trim()
        });
        bool customChanged = !ProfilesFunctionallyEqual(old, normalized)
                             || !string.Equals(old.Name, normalized.Name, StringComparison.Ordinal);
        _profiles[_selectedIndex] = normalized;

        // ComboBox items are display-name strings, not data-bound profile objects. Rebuild the
        // list immediately after a custom scheme is renamed/saved so the new name is visible
        // without switching pages or restarting the settings window.
        if (customChanged)
            SelectProfile(normalized.Id);

        return customChanged;
    }

    private HudProfile BuildEditedProfile(HudProfile old)
    {
        bool isGpu = ProfileNeedsGpu(old);
        bool isTime = ProfileNeedsTime(old);
        bool isNetwork = ProfileNeedsNetwork(old);
        bool isPing = ProfileNeedsPing(old);

        string gpuAdapterId = isGpu ? GetSelectedGpuAdapterId(old.GpuAdapterId) : old.GpuAdapterId;
        string networkDisplayUnit = isNetwork
            ? (NetworkDisplayUnitCombo.SelectedIndex == 0 ? "Mbps" : "AutoBytes")
            : old.NetworkDisplayUnit;
        string networkPercentMode = isNetwork
            ? NetworkPercentModeCombo.SelectedIndex switch
            {
                1 => "Download",
                2 => "Upload",
                3 => "Max",
                _ => "Total",
            }
            : old.NetworkPercentMode;
        double networkReferenceValue = isNetwork
            ? Math.Max(0.001d, (double)(NetworkReferenceValueBox.Value ?? 100m))
            : old.NetworkReferenceValue;
        string networkReferenceUnit = isNetwork
            ? NetworkReferenceUnitCombo.SelectedIndex switch
            {
                0 => "Mbps",
                1 => "KB/s",
                _ => "MB/s",
            }
            : old.NetworkReferenceUnit;
        string probeProtocol = isPing
            ? ProbeProtocolCombo.SelectedIndex switch
            {
                1 => "TCP",
                2 => "UDP",
                _ => "ICMP",
            }
            : old.ProbeProtocol;
        int probePort = isPing
            ? Math.Clamp((int)(ProbePortBox.Value ?? 443m), 1, 65535)
            : old.ProbePort;

        return old with
        {
            AnimationMode = AnimationModeCombo.SelectedIndex == 1 ? "Full" : "Simple",
            TaglineTemplate = TaglineBox.Text ?? "",
            TitleTemplate = TitleBox.Text ?? "",
            PrimaryTemplate = PrimaryBox.Text ?? "",
            SecondaryTemplate = SecondaryBox.Text ?? "",
            RightTemplate = RightBox.Text ?? "",
            RightSuffix = RightSuffixBox.Text ?? "",
            ProgressVariable = ProgressVariableBox.Text?.Trim() ?? "",
            ProgressMin = (double)(ProgressMinBox.Value ?? 0),
            ProgressMax = (double)(ProgressMaxBox.Value ?? 100),
            LeftIcon = LeftIconCombo.SelectedItem?.ToString() ?? "bolt",
            RightIcon = RightIconCombo.SelectedItem?.ToString() ?? "battery",
            AccentColor = string.IsNullOrWhiteSpace(AccentColorBox.Text) ? "#C6CA4C" : AccentColorBox.Text!.Trim(),
            ColorRules = ParseColorRules(ColorRulesBox.Text),
            TimeTargetEnabled = isTime ? TimeTargetSwitch.IsChecked == true : old.TimeTargetEnabled,
            TimeTarget = isTime ? NormalizeTimeTarget(TimeTargetBox.Text, old.TimeTarget) : old.TimeTarget,
            GpuAdapterId = gpuAdapterId,
            NetworkDisplayUnit = networkDisplayUnit,
            NetworkPercentMode = networkPercentMode,
            NetworkReferenceValue = networkReferenceValue,
            NetworkReferenceUnit = networkReferenceUnit,
            PingTarget = isPing ? NormalizePingTarget(PingTargetBox.Text, old.PingTarget) : old.PingTarget,
            ProbeProtocol = probeProtocol,
            ProbePort = probePort
        };
    }

    private static bool ProfilesFunctionallyEqual(HudProfile a, HudProfile b)
    {
        if (!string.Equals(a.AnimationMode, b.AnimationMode, StringComparison.Ordinal)
            || !string.Equals(a.TaglineTemplate, b.TaglineTemplate, StringComparison.Ordinal)
            || !string.Equals(a.TitleTemplate, b.TitleTemplate, StringComparison.Ordinal)
            || !string.Equals(a.PrimaryTemplate, b.PrimaryTemplate, StringComparison.Ordinal)
            || !string.Equals(a.SecondaryTemplate, b.SecondaryTemplate, StringComparison.Ordinal)
            || !string.Equals(a.RightTemplate, b.RightTemplate, StringComparison.Ordinal)
            || !string.Equals(a.RightSuffix, b.RightSuffix, StringComparison.Ordinal)
            || !string.Equals(a.ProgressVariable, b.ProgressVariable, StringComparison.Ordinal)
            || Math.Abs(a.ProgressMin - b.ProgressMin) > 0.000001
            || Math.Abs(a.ProgressMax - b.ProgressMax) > 0.000001
            || !string.Equals(a.LeftIcon, b.LeftIcon, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.RightIcon, b.RightIcon, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.AccentColor, b.AccentColor, StringComparison.OrdinalIgnoreCase)
            || a.TimeTargetEnabled != b.TimeTargetEnabled
            || !string.Equals(a.TimeTarget, b.TimeTarget, StringComparison.Ordinal)
            || !string.Equals(a.GpuAdapterId, b.GpuAdapterId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.NetworkDisplayUnit, b.NetworkDisplayUnit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.NetworkPercentMode, b.NetworkPercentMode, StringComparison.OrdinalIgnoreCase)
            || Math.Abs(a.NetworkReferenceValue - b.NetworkReferenceValue) > 0.000001
            || !string.Equals(a.NetworkReferenceUnit, b.NetworkReferenceUnit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.PingTarget, b.PingTarget, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.ProbeProtocol, b.ProbeProtocol, StringComparison.OrdinalIgnoreCase)
            || a.ProbePort != b.ProbePort
            || a.ColorRules.Count != b.ColorRules.Count)
            return false;

        for (int i = 0; i < a.ColorRules.Count; i++)
        {
            var x = a.ColorRules[i];
            var y = b.ColorRules[i];
            if (!string.Equals(x.Variable, y.Variable, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(x.Operator, y.Operator, StringComparison.Ordinal)
                || Math.Abs(x.Value - y.Value) > 0.000001
                || !string.Equals(x.Color, y.Color, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private string MakeUniqueCustomName(string desired)
    {
        string baseName = string.IsNullOrWhiteSpace(desired) ? LocalizationManager.Text("自定义方案", "Custom Profile") : desired.Trim();
        if (_profiles.All(p => !string.Equals(ProfileDisplayName(p), baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (_profiles.All(p => !string.Equals(ProfileDisplayName(p), candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return $"{baseName} {Guid.NewGuid():N}"[..Math.Min(baseName.Length + 9, baseName.Length + 32)];
    }

    private void SetProfileEditorEnabled(bool editable, bool canDelete)
    {
        ProfileNameBox.IsEnabled = editable;
        AnimationModeCombo.IsEnabled = editable;
        TaglineBox.IsEnabled = editable;
        TitleBox.IsEnabled = editable;
        PrimaryBox.IsEnabled = editable;
        SecondaryBox.IsEnabled = editable;
        RightBox.IsEnabled = editable;
        RightSuffixBox.IsEnabled = editable;
        ProgressVariableBox.IsEnabled = editable;
        ProgressMinBox.IsEnabled = editable;
        ProgressMaxBox.IsEnabled = editable;
        LeftIconCombo.IsEnabled = editable;
        RightIconCombo.IsEnabled = editable;
        AccentColorBox.IsEnabled = editable;
        ColorRulesBox.IsEnabled = editable;
        NetworkDisplayUnitCombo.IsEnabled = editable;
        NetworkPercentModeCombo.IsEnabled = editable;
        NetworkReferenceValueBox.IsEnabled = editable;
        NetworkReferenceUnitCombo.IsEnabled = editable;
        PingTargetBox.IsEnabled = editable;
        ProbeProtocolCombo.IsEnabled = editable;
        ProbePortBox.IsEnabled = editable;
        SaveProfileBtn.IsEnabled = editable;
        DeleteProfileBtn.IsEnabled = canDelete;
    }

    private void RefreshGpuAdapters()
    {
        try { _gpuAdapters = GpuAdapterCatalog.GetAdapters(); }
        catch { _gpuAdapters = Array.Empty<GpuAdapterInfo>(); }

        if (GpuAdapterCombo is null) return;
        var currentId = GetSelectedGpuAdapterId("");
        GpuAdapterCombo.Items.Clear();
        for (int i = 0; i < _gpuAdapters.Count; i++)
        {
            var gpu = _gpuAdapters[i];
            string memory = gpu.DedicatedMemoryBytes > 0
                ? $" · {gpu.DedicatedMemoryBytes / 1024d / 1024d / 1024d:0.#} GB"
                : "";
            GpuAdapterCombo.Items.Add($"GPU {i} · {gpu.Name}{memory}");
        }
        if (_gpuAdapters.Count > 0)
        {
            int index = _gpuAdapters.ToList().FindIndex(x => x.MatchesId(currentId));
            GpuAdapterCombo.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void SelectGpuAdapter(string? adapterId)
    {
        if (_gpuAdapters.Count == 0)
        {
            GpuAdapterCombo.SelectedIndex = -1;
            return;
        }
        int index = !string.IsNullOrWhiteSpace(adapterId)
            ? _gpuAdapters.ToList().FindIndex(x => x.MatchesId(adapterId))
            : -1;
        GpuAdapterCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    private string GetSelectedGpuAdapterId(string fallback)
    {
        int index = GpuAdapterCombo?.SelectedIndex ?? -1;
        return index >= 0 && index < _gpuAdapters.Count ? _gpuAdapters[index].Id : fallback;
    }

    private static bool ProfileNeedsGpu(HudProfile profile) =>
        HudProfileRenderer.GetRequiredVariables(profile).Any(x => x.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase));

    private static bool ProfileNeedsTime(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
        || HudProfileRenderer.GetRequiredVariables(profile).Any(x => x.StartsWith("time.", StringComparison.OrdinalIgnoreCase));

    private static bool ProfileNeedsNetwork(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "system.network", StringComparison.OrdinalIgnoreCase)
        || HudProfileRenderer.GetRequiredVariables(profile).Any(x => x.StartsWith("network.", StringComparison.OrdinalIgnoreCase));

    private static bool ProfileNeedsDeepSeek(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "deepseek.balance-period", StringComparison.OrdinalIgnoreCase)
        || HudProfileRenderer.GetRequiredVariables(profile).Any(x => x.StartsWith("deepseek.", StringComparison.OrdinalIgnoreCase));

    private static bool ProfileNeedsPing(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "network.ping", StringComparison.OrdinalIgnoreCase)
        || HudProfileRenderer.GetRequiredVariables(profile).Any(x =>
            x.StartsWith("ping.", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("probe.", StringComparison.OrdinalIgnoreCase));

    private void UpdateContextOptionsUi()
    {
        if (TimeOptionsPanel is null || GpuOptionsPanel is null || NetworkOptionsPanel is null || PingOptionsPanel is null || DeepSeekOptionsHintPanel is null) return;
        HudProfile? profile = _selectedIndex >= 0 && _selectedIndex < _profiles.Count ? _profiles[_selectedIndex] : null;
        bool isTime = profile is not null && ProfileNeedsTime(profile);
        bool isGpu = profile is not null && ProfileNeedsGpu(profile);
        bool isNetwork = profile is not null && ProfileNeedsNetwork(profile);
        bool isPing = profile is not null && ProfileNeedsPing(profile);
        bool isDeepSeek = profile is not null && ProfileNeedsDeepSeek(profile);

        DeepSeekOptionsHintPanel.IsVisible = isDeepSeek;

        TimeOptionsPanel.IsVisible = isTime;
        TimeTargetSwitch.IsEnabled = isTime;
        TimeTargetBox.IsEnabled = isTime && TimeTargetSwitch.IsChecked == true;
        TimeTargetBox.Opacity = TimeTargetBox.IsEnabled ? 1.0 : 0.45;

        bool showGpuChooser = isGpu && _gpuAdapters.Count > 1;
        GpuOptionsPanel.IsVisible = showGpuChooser;
        GpuAdapterCombo.IsEnabled = showGpuChooser;
        if (isGpu)
        {
            GpuAdapterStatusText.Text = _gpuAdapters.Count switch
            {
                0 => LocalizationManager.Text("未检测到可用 GPU", "No GPU detected"),
                1 => LocalizationManager.Text($"已检测到 1 个 GPU：{_gpuAdapters[0].Name}", $"1 GPU detected: {_gpuAdapters[0].Name}"),
                _ => LocalizationManager.Text($"已检测到 {_gpuAdapters.Count} 个 GPU", $"{_gpuAdapters.Count} GPUs detected")
            };
        }

        NetworkOptionsPanel.IsVisible = isNetwork;
        NetworkDisplayUnitCombo.IsEnabled = isNetwork;
        NetworkPercentModeCombo.IsEnabled = isNetwork;
        NetworkReferenceValueBox.IsEnabled = isNetwork;
        NetworkReferenceUnitCombo.IsEnabled = isNetwork;

        PingOptionsPanel.IsVisible = isPing;
        PingTargetBox.IsEnabled = isPing;
        ProbeProtocolCombo.IsEnabled = isPing;
        bool usesPort = isPing && ProbeProtocolCombo.SelectedIndex is 1 or 2;
        ProbePortBox.IsEnabled = usesPort;
        ProbePortBox.Opacity = usesPort ? 1.0 : 0.45;
    }

    private List<string> GetSanitizedCycleProfileIds()
    {
        var validIds = _profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cycleProfileIds = _cycleProfileIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && validIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _cycleProfileIds.ToList();
    }

    private void RefreshCycleEditor(int preferredSelectedIndex = -1)
    {
        if (CycleProfileList is null || CycleProfileSourceCombo is null) return;

        GetSanitizedCycleProfileIds();
        int oldSelected = preferredSelectedIndex >= 0 ? preferredSelectedIndex : CycleProfileList.SelectedIndex;

        CycleProfileList.Items.Clear();
        foreach (var id in _cycleProfileIds)
        {
            var profile = _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
                CycleProfileList.Items.Add(ProfileDisplayName(profile));
        }

        if (_cycleProfileIds.Count > 0)
            CycleProfileList.SelectedIndex = Math.Clamp(oldSelected, 0, _cycleProfileIds.Count - 1);
        else
            CycleProfileList.SelectedIndex = -1;

        _cycleSourceProfileIds = _profiles
            .Where(p => !_cycleProfileIds.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .ToList();

        CycleProfileSourceCombo.Items.Clear();
        foreach (var id in _cycleSourceProfileIds)
        {
            var profile = _profiles.First(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            CycleProfileSourceCombo.Items.Add(ProfileDisplayName(profile));
        }
        CycleProfileSourceCombo.SelectedIndex = _cycleSourceProfileIds.Count > 0 ? 0 : -1;

        bool enabled = AutoCycleSwitch.IsChecked == true;
        CycleQueueStatusText.Text = _cycleProfileIds.Count switch
        {
            0 => enabled
                ? LocalizationManager.Text("自动轮播已开启，但轮播队列为空；请至少添加一个方案。", "Auto cycle is on, but the queue is empty. Add at least one profile.")
                : LocalizationManager.Text("轮播队列为空。开启自动轮播前请添加要轮播的方案。", "The cycle queue is empty. Add profiles before enabling auto cycle."),
            1 => enabled
                ? LocalizationManager.Text("队列中有 1 个方案；会保持该方案，不发生方案间切换。", "The queue has one profile; it will stay on that profile.")
                : LocalizationManager.Text("已配置 1 个轮播方案。", "1 profile is in the cycle queue."),
            _ => enabled
                ? LocalizationManager.Text($"已启用自动轮播，将按上方顺序循环 {_cycleProfileIds.Count} 个方案。", $"Auto cycle is on; {_cycleProfileIds.Count} profiles will rotate in order.")
                : LocalizationManager.Text($"已配置 {_cycleProfileIds.Count} 个轮播方案；开启自动轮播后生效。", $"{_cycleProfileIds.Count} profiles are queued; enable auto cycle to use them.")
        };
        UpdateCycleButtons();
    }

    private void UpdateCycleControls()
    {
        if (CycleOptionsPanel is null) return;
        // Queue editing remains available while cycling is disabled so users can prepare the
        // order first. Only the runtime behaviour is controlled by the switch.
        CycleOptionsPanel.Opacity = AutoCycleSwitch.IsChecked == true ? 1.0 : 0.88;
        RefreshCycleEditor();
    }

    private void UpdateCycleButtons()
    {
        if (CycleProfileList is null) return;
        int index = CycleProfileList.SelectedIndex;
        CycleAddBtn.IsEnabled = CycleProfileSourceCombo.SelectedIndex >= 0 && _cycleSourceProfileIds.Count > 0;
        CycleRemoveBtn.IsEnabled = index >= 0 && index < _cycleProfileIds.Count;
        CycleMoveUpBtn.IsEnabled = index > 0 && index < _cycleProfileIds.Count;
        CycleMoveDownBtn.IsEnabled = index >= 0 && index < _cycleProfileIds.Count - 1;
    }

    private void OnCycleAdd(object? sender, RoutedEventArgs e)
    {
        int sourceIndex = CycleProfileSourceCombo.SelectedIndex;
        if (sourceIndex < 0 || sourceIndex >= _cycleSourceProfileIds.Count) return;
        string id = _cycleSourceProfileIds[sourceIndex];
        if (_cycleProfileIds.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
        _cycleProfileIds.Add(id);
        RefreshCycleEditor(_cycleProfileIds.Count - 1);
    }

    private void OnCycleRemove(object? sender, RoutedEventArgs e)
    {
        int index = CycleProfileList.SelectedIndex;
        if (index < 0 || index >= _cycleProfileIds.Count) return;
        _cycleProfileIds.RemoveAt(index);
        RefreshCycleEditor(Math.Min(index, _cycleProfileIds.Count - 1));
    }

    private void OnCycleMoveUp(object? sender, RoutedEventArgs e)
    {
        int index = CycleProfileList.SelectedIndex;
        if (index <= 0 || index >= _cycleProfileIds.Count) return;
        (_cycleProfileIds[index - 1], _cycleProfileIds[index]) = (_cycleProfileIds[index], _cycleProfileIds[index - 1]);
        RefreshCycleEditor(index - 1);
    }

    private void OnCycleMoveDown(object? sender, RoutedEventArgs e)
    {
        int index = CycleProfileList.SelectedIndex;
        if (index < 0 || index >= _cycleProfileIds.Count - 1) return;
        (_cycleProfileIds[index + 1], _cycleProfileIds[index]) = (_cycleProfileIds[index], _cycleProfileIds[index + 1]);
        RefreshCycleEditor(index + 1);
    }

    private void OnAddProfile(object? sender, RoutedEventArgs e)
    {
        int number = _profiles.Count(p => !p.IsBuiltIn) + 1;
        var created = new HudProfile
        {
            IsBuiltIn = false,
            BuiltInKey = "",
            Category = "自定义",
            Name = LocalizationManager.Text($"自定义方案 {number}", $"Custom Profile {number}"),
            TitleTemplate = LocalizationManager.Text("系统状态", "System Status"),
            AnimationMode = "Full",
            LeftIcon = "clock",
            RightIcon = "clock"
        };
        _profiles.Add(created);
        SelectProfile(created.Id);
    }

    private void OnSaveProfile(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            OnAddProfile(sender, e);
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
        }

        bool wasBuiltIn = _profiles[_selectedIndex].IsBuiltIn;
        bool changed = SaveCurrentProfile();
        if (wasBuiltIn && changed)
            ProfileStatusText.Text = LocalizationManager.Text("已基于内置方案创建修改副本，原预设保持不变", "Modified copy created; the built-in preset is unchanged.");
        else
            ProfileStatusText.Text = changed ? LocalizationManager.Text("方案已保存", "Profile saved") : LocalizationManager.Text("方案没有需要保存的更改", "No changes to save");
    }

    private void OnDeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
        if (_profiles[_selectedIndex].IsBuiltIn)
        {
            ProfileStatusText.Text = LocalizationManager.Text("内置方案不可删除", "Built-in profiles cannot be deleted");
            return;
        }

        int oldIndex = _selectedIndex;
        string removedId = _profiles[oldIndex].Id;
        _profiles.RemoveAt(oldIndex);
        _cycleProfileIds.RemoveAll(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase));
        if (_profiles.Count == 0)
        {
            RefreshCycleEditor();
            return;
        }
        int nextIndex = Math.Clamp(oldIndex, 0, _profiles.Count - 1);
        SelectProfile(_profiles[nextIndex].Id);
    }

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (_hud is null) return;
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;

        PreviewBtn.IsEnabled = false;
        ProfileStatusText.Text = LocalizationManager.Text("正在读取数据…", "Reading data…");
        try
        {
            TryParseHttpJson(showErrors: true);
            var settings = new CustomHudSettings
            {
                AutoCycle = AutoCycleSwitch.IsChecked == true,
                CycleSeconds = (int)Math.Clamp(CycleSecondsBox.Value ?? 10, 3, 3600),
                CycleProfileIds = GetSanitizedCycleProfileIds(),
                CycleAnimationMode = CycleAnimationModeCombo.SelectedIndex == 0 ? "Simple" : "Full",
                ActiveProfileId = _profiles[_selectedIndex].Id,
                DeepSeekApiKeyProtected = SecretStore.Protect(DeepSeekKeyBox.Text),
                DeepSeekPeakWindows = string.IsNullOrWhiteSpace(DeepSeekWindowsBox.Text) ? "09:00-12:00;14:00-18:00" : DeepSeekWindowsBox.Text!.Trim(),
                Profiles = _profiles.Select(CloneProfile).ToList(),
                HttpSources = _httpSources.Select(CloneHttp).ToList()
            };
            var profile = BuildEditedProfile(_profiles[_selectedIndex]);
            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await _previewVariables.SnapshotAsync(settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort);
            var data = HudProfileRenderer.Render(profile, vars);
            ProfileStatusText.Text = "";

            Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await _previewVariables.SnapshotAsync(settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            await _hud.ShowCustomAsync(data, refresh);
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = LocalizationManager.IsEnglish ? "Preview failed. See logs." : "显示失败：" + ex.Message;
        }
        finally
        {
            PreviewBtn.IsEnabled = true;
        }
    }

    private IReadOnlyList<VariableDefinition> GetAllVariableDefinitions()
    {
        var list = VariableCatalog.AllBuiltIns.Select(VariableLocalization.Localize).ToList();
        foreach (var s in _httpSources)
        {
            foreach (var f in s.Fields)
            {
                var key = $"custom.{Sanitize(s.Name)}.{Sanitize(f.Variable)}";
                if (list.All(x => !string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
                    list.Add(VariableLocalization.Localize(VariableCatalog.CreateCustom(key)));
            }
        }
        return list;
    }

    private void RebuildVariableCategories()
    {
        var previous = VariableCategoryCombo.SelectedItem?.ToString() ?? LocalizationManager.Text("全部分类", "All Categories");
        VariableCategoryCombo.Items.Clear();
        VariableCategoryCombo.Items.Add(LocalizationManager.Text("全部分类", "All Categories"));
        foreach (var category in GetAllVariableDefinitions().Select(x => x.Category).Distinct())
            VariableCategoryCombo.Items.Add(category);

        int selected = 0;
        for (int i = 0; i < VariableCategoryCombo.Items.Count; i++)
        {
            if (string.Equals(VariableCategoryCombo.Items[i]?.ToString(), previous, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
                break;
            }
        }
        VariableCategoryCombo.SelectedIndex = selected;
    }

    private void RefreshVariableList()
    {
        if (VariableList is null) return;

        var selectedKey = (VariableList.SelectedItem as VariableDefinition)?.Key;
        string q = VariableSearchBox.Text?.Trim() ?? "";
        string category = VariableCategoryCombo.SelectedItem?.ToString() ?? LocalizationManager.Text("全部分类", "All Categories");

        var filtered = GetAllVariableDefinitions()
            .Where(x => category == LocalizationManager.Text("全部分类", "All Categories") || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(x => q.Length == 0
                        || x.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || x.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || x.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            // Keep VariableCatalog's semantic category + variable ordering.
            // Do not re-sort localized names alphabetically: that made Chinese/English lists inconsistent.
            .ToList();

        VariableList.Items.Clear();
        foreach (var item in filtered)
            VariableList.Items.Add(item);

        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            var match = filtered.FirstOrDefault(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                VariableList.SelectedItem = match;
        }
    }

    private void OnVariableSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (VariableList.SelectedItem is not VariableDefinition item)
        {
            ClearVariableDetail();
            return;
        }

        VariableDetailName.Text = item.Name;
        VariableDetailCategory.Text = item.Category.ToUpperInvariant();
        VariableDetailDescription.Text = item.Description;
        VariableKeyBox.Text = item.Key;
        VariableTemplateBox.Text = item.TemplateToken;
        VariableTypeText.Text = item.ValueType;
        VariableUnitText.Text = string.IsNullOrWhiteSpace(item.Unit) ? LocalizationManager.Text("无固定单位", "No fixed unit") : item.Unit;
        VariableUseText.Text = item.RecommendedUse;
        VariableFormatsText.Text = string.IsNullOrWhiteSpace(item.RecommendedFormats) ? LocalizationManager.Text("无需格式化", "None") : item.RecommendedFormats;
        CopyVariableKeyBtn.IsEnabled = true;
        CopyVariableTemplateBtn.IsEnabled = true;
    }

    private void ClearVariableDetail()
    {
        if (VariableDetailName is null) return;
        VariableDetailName.Text = LocalizationManager.Text("选择一个变量", "Select a variable");
        VariableDetailCategory.Text = "";
        VariableDetailDescription.Text = LocalizationManager.Text("从左侧列表选择变量后，这里会显示变量含义、单位和推荐用途。", "Select a variable on the left to view its meaning, unit, and recommended use.");
        VariableKeyBox.Text = "";
        VariableTemplateBox.Text = "";
        VariableTypeText.Text = "";
        VariableUnitText.Text = "";
        VariableUseText.Text = "";
        VariableFormatsText.Text = "";
        CopyVariableKeyBtn.IsEnabled = false;
        CopyVariableTemplateBtn.IsEnabled = false;
    }

    private async void OnCopyVariableKey(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VariableKeyBox.Text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(VariableKeyBox.Text);
    }

    private async void OnCopyVariableTemplate(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VariableTemplateBox.Text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(VariableTemplateBox.Text);
    }

    private bool TryParseHttpJson(bool showErrors)
    {
        try
        {
            string sourceText = StripHttpJsonCommentLines(HttpSourcesJsonBox.Text);
            var parsed = string.IsNullOrWhiteSpace(sourceText)
                ? new List<CustomHttpSource>()
                : JsonSerializer.Deserialize<List<CustomHttpSource>>(sourceText) ?? new();
            _httpSources = parsed;
            HttpStatusText.Text = LocalizationManager.Text($"已载入 {_httpSources.Count} 个 HTTP 数据源", $"Loaded {_httpSources.Count} HTTP data source(s)");
            RebuildVariableCategories();
            RefreshVariableList();
            return true;
        }
        catch (Exception ex)
        {
            if (showErrors)
                HttpStatusText.Text = LocalizationManager.IsEnglish ? "Invalid JSON; keeping the last valid config." : "JSON 无效，将保留上次有效配置：" + ex.Message;
            return false;
        }
    }

    private static string StripHttpJsonCommentLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // The built-in sample is deliberately comment-only. We only remove full-line
        // // comments so URLs such as https://example.com remain untouched in real JSON.
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static List<HudColorRule> ParseColorRules(string? text)
    {
        var result = new List<HudColorRule>();
        var rx = new Regex(@"^\s*(?<var>[A-Za-z0-9_.-]+)\s*(?<op>>=|<=|==|!=|>|<)\s*(?<value>-?[0-9]+(?:\.[0-9]+)?)\s*=>\s*(?<color>#[0-9A-Fa-f]{6,8})\s*$");
        foreach (var line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = rx.Match(line);
            if (!m.Success) continue;
            if (!double.TryParse(m.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) continue;
            result.Add(new HudColorRule
            {
                Variable = m.Groups["var"].Value,
                Operator = m.Groups["op"].Value,
                Value = d,
                Color = m.Groups["color"].Value
            });
        }
        return result;
    }

    private static string NormalizeTimeTarget(string? text, string fallback)
    {
        if (TimeSpan.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out var value)
            && value >= TimeSpan.Zero
            && value < TimeSpan.FromDays(1))
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

        if (TimeSpan.TryParse(fallback, CultureInfo.InvariantCulture, out value))
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

        return "10:00:00";
    }

    private static string NormalizePingTarget(string? text, string fallback)
    {
        string value = (text ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(value)) return value;
        value = (fallback ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "1.1.1.1" : value;
    }

    private static HudProfile CloneProfile(HudProfile p) =>
        p with { ColorRules = p.ColorRules.Select(x => x with { }).ToList() };

    private static CustomHttpSource CloneHttp(CustomHttpSource s) => s with
    {
        Headers = new Dictionary<string, string>(s.Headers, StringComparer.OrdinalIgnoreCase),
        Fields = s.Fields.Select(x => x with { }).ToList()
    };

    private static string Sanitize(string s) =>
        new((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
}
