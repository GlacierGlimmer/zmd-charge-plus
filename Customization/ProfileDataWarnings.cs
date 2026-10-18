using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using EndfieldChargePlus.Interop;
namespace EndfieldChargePlus.Customization;

/// <summary>One warning window shared by all HUDs; ignores and acknowledgements are per fault.</summary>
public static class ProfileDataWarnings
{
    private sealed record Warning(string RawKey, string PreferenceKey)
    {
        internal string Text=>HardwareWarning(RawKey);
    }
    private sealed record Incident(string Scope, HudProfile Profile, Warning[] Warnings, long Since)
    {
        internal string Name=>BuiltInProfileLocalization.DisplayName(Profile);
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Incident> Incidents = new();
    private static readonly HashSet<string> Acknowledged = new(StringComparer.OrdinalIgnoreCase);
    private static DataWarningPreferences _preferences = new(Path.Combine(AppPaths.DataDirectory, "ignored-data-warnings.json"));
    private static string[] _displayedPreferenceKeys = Array.Empty<string>();
    private static string[] _displayedFaults = Array.Empty<string>();
    private static Window? _dialog;
    private static TextBlock? _details;
    private static DispatcherTimer? _mergeTimer;

    private static string PreferenceKey(HudProfile profile, string text) =>
        text.StartsWith("表达式 / Expression:", StringComparison.Ordinal) ? $"expression:{profile.Id}:{text}"
        : text == "进度表达式 / Progress expression" ? $"progress-expression:{profile.Id}:{profile.ProgressVariable}" : text;

    public static void Check(HudProfile profile, IReadOnlyDictionary<string, object?> values, string scope = "")
    {
        var missing = HudProfileRenderer.GetUnavailableVariables(profile, values).OrderBy(k => k)
            .Select(k => new Warning(k, PreferenceKey(profile, k))).Where(w => !_preferences.IsIgnored(w.PreferenceKey)).ToArray();
        string key = scope + ":" + profile.Id;
        lock (Gate)
        {
            foreach (var old in Incidents.Where(p => p.Value.Scope == scope && p.Key != key).Select(p => p.Key).ToArray()) Incidents.Remove(old);
            if (missing.Length == 0) Incidents.Remove(key);
            else
            {
                long since = Incidents.TryGetValue(key, out var old) ? old.Since : Environment.TickCount64;
                Incidents[key] = new(scope, profile, missing, since);
            }
            PruneAcknowledged();
        }
        Dispatcher.UIThread.Post(ScheduleRefresh);
    }

    private static string HardwareWarning(string key)
    {
        const string expression="表达式 / Expression:";
        if(key.StartsWith(expression,StringComparison.Ordinal))return LocalizationManager.Text("表达式","Expression")+":"+key[expression.Length..];
        if(key=="进度表达式 / Progress expression")return LocalizationManager.Text("进度表达式","Progress expression");
        if(key.StartsWith("cpu.temperature",StringComparison.OrdinalIgnoreCase) || key=="cpu.core.temperature_avg")
            return key+"\n"+HardwareSensorDriver.Inspect().Description+"\n"+LocalizationManager.Text("请在“设置 → 数据管理 → 硬件数据”检查驱动和传感器。","Check drivers and sensors under Settings → Data Management → Hardware Data.");
        if(key.StartsWith("sensor.",StringComparison.OrdinalIgnoreCase))return key+"\n"+LocalizationManager.Text("请在“设置 → 数据管理 → 硬件数据”检查此传感器或 AIDA64 共享设置。","Check this sensor or the AIDA64 sharing settings under Settings → Data Management → Hardware Data.");
        return key;
    }

    private static IEnumerable<string> Faults() => Incidents.SelectMany(p => p.Value.Warnings.Select(w => p.Key + "\n" + w.PreferenceKey));
    private static void PruneAcknowledged() => Acknowledged.IntersectWith(Faults());
    public static void Forget(string scope)
    {
        lock (Gate)
        {
            foreach (var key in Incidents.Where(p => p.Value.Scope == scope).Select(p => p.Key).ToArray()) Incidents.Remove(key);
            PruneAcknowledged();
        }
        Dispatcher.UIThread.Post(ScheduleRefresh);
    }
    private static void ScheduleRefresh()
    {
        if (_mergeTimer is null)
        {
            _mergeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _mergeTimer.Tick += (_, _) => { _mergeTimer.Stop(); Refresh(); };
        }
        if (!_mergeTimer.IsEnabled) _mergeTimer.Start();
    }
    private static void Refresh()
    {
        Incident[] current; bool ready, acknowledged;
        lock (Gate)
        {
            current = Incidents.Values.ToArray();
            ready = current.Any(i => Environment.TickCount64 - i.Since >= 5000);
            acknowledged = Faults().All(Acknowledged.Contains);
        }
        if (current.Length == 0) { _dialog?.Close(); return; }
        if (_dialog is null && (!ready || acknowledged)) return;
        string body = string.Join("\n\n", current.GroupBy(i => i.Name).OrderBy(g => g.Key)
            .Select(g => g.Key + "\n" + string.Join("\n", g.SelectMany(i => i.Warnings).Select(w => w.Text).Distinct().OrderBy(k => k))));
        _displayedPreferenceKeys = current.SelectMany(i => i.Warnings).Select(w => w.PreferenceKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (Gate) _displayedFaults = Faults().ToArray();
        if (_dialog is not null) { _details!.Text = body; return; }
        var close = new Button { Content = LocalizationManager.Text("知道了", "OK"), HorizontalAlignment = HorizontalAlignment.Right };
        var ignore = new CheckBox { Name = "IgnoreCurrentDataWarningCheckBox", Content = LocalizationManager.Text("此数据不再提示", "Don't warn about this data again"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var error = new TextBlock { IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _details = new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = LocalizationManager.Text("以下 HUD 方案的数据不可用或无效：", "Data is unavailable or invalid for these HUD profiles:"), FontSize = 16, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new ScrollViewer { Content = _details, MaxHeight = 460, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = LocalizationManager.Text("请检查权限、设备支持和数据源。勾选后只永久关闭当前窗口列出的数据提示，其他变量仍会正常提示。", "Check permissions, device support and data sources. The checkbox permanently silences only the data listed in this window; other variables still produce warnings."), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"),FlowDirection=Avalonia.Media.FlowDirection.LeftToRight };
        footer.Children.Add(ignore); Grid.SetColumn(close, 1); footer.Children.Add(close);
        panel.Children.Add(footer); panel.Children.Add(error);
        var dialog = new Window { Title = LocalizationManager.Text("HUD 数据提示", "HUD data warning"), Width = 620, SizeToContent = SizeToContent.Height, MaxHeight = 720, Content = panel, CanResize = true, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        void UpdateLanguage()
        {
            close.Content=LocalizationManager.Text("知道了","OK");
            LocalizationManager.ApplyStaticText(dialog);
            dialog.FlowDirection=LocalizationManager.IsRightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
            if(ReferenceEquals(_dialog,dialog))Refresh();
        }
        UpdateLanguage();LocalizationManager.LanguageChanged+=UpdateLanguage;
        _dialog = dialog; close.Click += (_, _) => dialog.Close();
        ignore.IsCheckedChanged += (_, _) =>
        {
            if (ignore.IsChecked != true) return;
            var selected = _displayedPreferenceKeys.ToArray();
            try
            {
                _preferences.Ignore(selected);
                lock (Gate)
                {
                    foreach (var pair in Incidents.ToArray())
                    {
                        var remaining = pair.Value.Warnings.Where(w => !_preferences.IsIgnored(w.PreferenceKey)).ToArray();
                        if (remaining.Length == 0) Incidents.Remove(pair.Key);
                        else Incidents[pair.Key] = pair.Value with { Warnings = remaining };
                    }
                    PruneAcknowledged();
                }
                // A newly arrived fault may not have been displayed when checked.
                // Close this selection; let remaining faults get a fresh unchecked dialog.
                dialog.Close();
                ScheduleRefresh();
            }
            catch (Exception ex)
            {
                ignore.IsChecked = false; error.IsVisible = true;
                error.Text = LocalizationManager.Text("未能保存不再提示设置，请重试。", "Could not save this preference. Please try again.");
                EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to suppress HUD data warnings.", ex);
            }
        };
        dialog.Closed += (_, _) =>
        {
            LocalizationManager.LanguageChanged-=UpdateLanguage;
            _dialog = null; _details = null;
            lock (Gate) { Acknowledged.UnionWith(_displayedFaults); PruneAcknowledged(); }
            _displayedPreferenceKeys = Array.Empty<string>(); _displayedFaults = Array.Empty<string>();
        };
        dialog.Show();
    }
}
