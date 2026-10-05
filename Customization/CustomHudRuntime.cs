using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Customization;

public sealed class CustomHudRuntime : IDisposable
{
    private readonly HudWindow _hud;
    private readonly VariableHub _variables = new();
    private readonly DispatcherTimer _timer;

    private AppSettings _appSettings = new();
    private CustomHudSettings _settings = CustomHudSettings.CreateDefault();

    private DateTime _nextPersistentRefresh = DateTime.MinValue;
    private DateTime _nextPersistentCycle = DateTime.MinValue;
    private DateTime _nextPowerPoll = DateTime.MinValue;
    private long _lastRenderedLocalSecond = -1;
    private int _profileIndex;
    private int _busy;
    private int _settingsTransitionBusy;
    private bool _persistentShown;
    private string _persistentProfileId = "";

    private bool _hotZoneLatched;
    private bool? _lastAcOnline;
    private bool? _candidateAcOnline;
    private DateTime _candidateAcSince = DateTime.MinValue;
    private bool _pendingPowerEvent;
    private bool _pendingAcOnline;

    private DateTime _nextDeepSeekPeriodPoll = DateTime.MinValue;
    private string? _lastDeepSeekPeriod;
    private bool _pendingDeepSeekPeriodTransition;

    // ---- 事件触发（settings.CustomHud.Triggers）----
    // 每次规则命中都会唤出 HUD 并展示轮播队列的下一项，所以这里维护一个独立的游标。
    private int _transientCycleIndex = -1;
    private DateTime _nextTriggerPoll = DateTime.MinValue;
    private bool _triggerBaselineCaptured;
    private readonly Dictionary<string, object?> _triggerLastValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _triggerNextAllowed = new(StringComparer.OrdinalIgnoreCase);

    public CustomHudRuntime(HudWindow hud)
    {
        _hud = hud;
        // 100 ms：用于屏幕顶边热点检测和墙钟秒边界同步。重型指标仍按需采样。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public void ApplySettings(AppSettings settings)
    {
        _appSettings = settings ?? new AppSettings();
        _settings = HudSettingsNormalizer.Normalize(_appSettings.CustomHud ?? CustomHudSettings.CreateDefault());
        _profileIndex = _settings.AutoCycle ? 0 : ResolveActiveProfileIndex();
        _nextPersistentRefresh = DateTime.MinValue;
        _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
        _lastRenderedLocalSecond = -1;
        _hotZoneLatched = false;
        _persistentProfileId = "";
        _nextDeepSeekPeriodPoll = DateTime.MinValue;
        _pendingDeepSeekPeriodTransition = false;

        // 触发监听重新建立基线，避免切方案/改设置后立刻误触发一次。
        _transientCycleIndex = -1;
        _nextTriggerPoll = DateTime.MinValue;
        _triggerBaselineCaptured = false;
        _triggerLastValues.Clear();
        _triggerNextAllowed.Clear();

        var activeProfile = _appSettings.AlwaysVisible && _settings.AutoCycle
            ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
            : ResolveActiveProfile();
        _lastDeepSeekPeriod = activeProfile is not null && IsDeepSeekProfile(activeProfile)
            ? VariableHub.GetDeepSeekPeriodNameZh(_settings)
            : null;

        if (!_appSettings.AlwaysVisible || !_appSettings.HudEnabled)
            _persistentShown = false;
    }

    public async Task ApplySettingsWithTransitionAsync(AppSettings settings)
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            // Every applied profile/parameter change follows the same full lifecycle:
            // retract old HUD -> apply settings -> summon the new HUD. Never mutate a visible HUD in place.
            if (_hud.IsVisible)
                await _hud.HideAnimatedAsync();

            _persistentShown = false;
            _hud.ApplySettings(settings);
            ApplySettings(settings);

            if (!settings.HudEnabled)
                return;

            var profile = settings.AlwaysVisible && _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (settings.AlwaysVisible && _settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort);
            var data = HudProfileRenderer.Render(profile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            if (settings.AlwaysVisible)
            {
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
                _persistentProfileId = profile.Id;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
            }
            else
            {
                await _hud.ShowCustomAsync(data, refresh);
            }
        }
        finally
        {
            _timer.Start();
            Interlocked.Exchange(ref _settingsTransitionBusy, 0);
        }
    }

    // Kept for compatibility with older callers.
    public void ApplySettings(CustomHudSettings settings)
    {
        ApplySettings(_appSettings with { CustomHud = settings ?? CustomHudSettings.CreateDefault() });
    }

    public void Start() => _ = RunStartupPresentationAsync();
    public void Stop() => _timer.Stop();

    private async Task RunStartupPresentationAsync()
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            if (!_appSettings.HudEnabled)
                return;

            // Every process start presents the currently effective scheme once through
            // its normal summon animation. If AlwaysVisible is enabled, the same animation
            // lands in the persistent C-state and live monitoring continues from there.
            var profile = _appSettings.AlwaysVisible && _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (_appSettings.AlwaysVisible && _settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await _variables.SnapshotAsync(
                _settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort);
            var data = HudProfileRenderer.Render(profile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await _variables.SnapshotAsync(
                    _settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            if (_appSettings.AlwaysVisible)
            {
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
                _persistentProfileId = profile.Id;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
                _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
            }
            else
            {
                await _hud.ShowCustomAsync(data, refresh);
                _persistentShown = false;
                _persistentProfileId = string.Empty;
            }
        }
        catch
        {
            // Startup presentation must never prevent the tray/settings application from running.
        }
        finally
        {
            Interlocked.Exchange(ref _settingsTransitionBusy, 0);
            _timer.Start();
        }
    }

    public async Task PreviewAsync(HudProfile profile)
    {
        await TriggerTransientProfileAsync(profile);
    }

    public async Task PreviewActiveAsync()
    {
        var profile = ResolveActiveProfile();
        if (profile is not null)
            await TriggerTransientProfileAsync(profile);
    }

    private async Task TickAsync()
    {
        if (Volatile.Read(ref _settingsTransitionBusy) != 0) return;

        if (!_appSettings.HudEnabled)
        {
            if (_persistentShown)
            {
                await _hud.HidePersistentAsync();
                _persistentShown = false;
                _persistentProfileId = "";
            }
            return;
        }

        if (_appSettings.AlwaysVisible)
        {
            _hotZoneLatched = false;
            await TickPersistentAsync();
            return;
        }

        if (_persistentShown)
        {
            await _hud.HidePersistentAsync();
            _persistentShown = false;
        }

        await TickTransientTriggersAsync();
    }

    private async Task TickTransientTriggersAsync()
    {
        PollPowerSource();

        // 电源插拔优先：不受当前选中方案影响，沿用“插电完整 / 断电简洁”动画。
        if (_pendingPowerEvent && !_hud.IsHudBusy && Volatile.Read(ref _busy) == 0)
        {
            bool acOnline = _pendingAcOnline;
            _pendingPowerEvent = false;
            var battery = FindBatteryProfile() with { AnimationMode = acOnline ? "Full" : "Simple" };
            await TriggerTransientProfileAsync(battery);
            return;
        }

        PollDeepSeekPeriodTransition();
        if (_pendingDeepSeekPeriodTransition && !_hud.IsHudBusy && Volatile.Read(ref _busy) == 0)
        {
            var activeDeepSeek = ResolveActiveProfile();
            if (activeDeepSeek is not null && IsDeepSeekProfile(activeDeepSeek))
            {
                _pendingDeepSeekPeriodTransition = false;
                await TriggerTransientProfileAsync(activeDeepSeek with { AnimationMode = "Full" });
                return;
            }

            _pendingDeepSeekPeriodTransition = false;
        }

        // 用户配置的事件触发（换歌 / 丢包 / 延迟 ...）。
        // 命中后唤出的是轮播队列的下一项，所以每次事件都会把轮播往前推一格。
        if (await TickTriggerRulesAsync())
            return;

        if (!WindowsSystemProbe.TryGetCursorPosition(out var pointer))
            return;

        bool inHotZone = _hud.IsPointInTopCenterHotZone(pointer);
        if (!inHotZone)
        {
            _hotZoneLatched = false;
            return;
        }

        if (_hotZoneLatched || _hud.IsHudBusy || Volatile.Read(ref _busy) != 0)
            return;

        // 热点唤出同样推进轮播："每次呼出都是轮播的下一个"。
        var next = NextTransientCycleProfile();
        if (next is null || IsBatteryProfile(next))
        {
            _hotZoneLatched = true;
            return;
        }

        // 只有真正开始唤出后才锁住热点；鼠标离开顶边后允许下一次唤出。
        _hotZoneLatched = true;
        await TriggerTransientProfileAsync(next);
    }

    private void PollPowerSource()
    {
        var now = DateTime.UtcNow;
        if (now < _nextPowerPoll) return;
        _nextPowerPoll = now.AddMilliseconds(150);

        if (!WindowsSystemProbe.TryGetAcOnline(out bool acOnline)) return;
        if (_lastAcOnline is null)
        {
            _lastAcOnline = acOnline;
            _candidateAcOnline = null;
            return;
        }

        if (_lastAcOnline.Value == acOnline)
        {
            _candidateAcOnline = null;
            return;
        }

        if (_candidateAcOnline != acOnline)
        {
            _candidateAcOnline = acOnline;
            _candidateAcSince = now;
            return;
        }

        // 与上游项目一致采用双向稳定确认，过滤 Windows 电源状态的瞬时抖动。
        if ((now - _candidateAcSince).TotalMilliseconds < 400) return;

        _lastAcOnline = acOnline;
        _candidateAcOnline = null;
        _pendingAcOnline = acOnline;
        _pendingPowerEvent = true;
    }

    private void PollDeepSeekPeriodTransition()
    {
        var now = DateTime.UtcNow;
        if (now < _nextDeepSeekPeriodPoll) return;
        _nextDeepSeekPeriodPoll = now.AddMilliseconds(250);

        var active = ResolveActiveProfile();
        if (active is null || !IsDeepSeekProfile(active))
        {
            _lastDeepSeekPeriod = null;
            _pendingDeepSeekPeriodTransition = false;
            return;
        }

        string current = VariableHub.GetDeepSeekPeriodNameZh(_settings);
        if (string.IsNullOrWhiteSpace(_lastDeepSeekPeriod))
        {
            _lastDeepSeekPeriod = current;
            return;
        }

        if (string.Equals(_lastDeepSeekPeriod, current, StringComparison.Ordinal))
            return;

        _lastDeepSeekPeriod = current;
        _pendingDeepSeekPeriodTransition = true;
    }

    private async Task<bool> TriggerTransientProfileAsync(HudProfile profile)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        try
        {
            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort);
            var data = HudProfileRenderer.Render(profile, vars);

            // Transient HUD data keeps updating while the original show/hold/hide animation runs.
            // Sampling stays off the UI thread and VariableHub only reads variables required by this scheme.
            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            await _hud.ShowCustomAsync(data, refresh);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task TickPersistentAsync()
    {
        var profiles = _settings.AutoCycle
            ? ResolveCycleProfiles().ToList()
            : _settings.Profiles.ToList();

        // An explicit empty queue is valid configuration. Keep the currently selected
        // scheme visible rather than inventing queue entries that the user did not add.
        if (_settings.AutoCycle && profiles.Count == 0)
        {
            var active = ResolveActiveProfile();
            if (active is not null)
                profiles.Add(active);
        }

        if (profiles.Count == 0)
        {
            if (_persistentShown)
            {
                await _hud.HidePersistentAsync();
                _persistentShown = false;
                _persistentProfileId = "";
            }
            return;
        }

        var nowUtc = DateTime.UtcNow;

        if (_settings.AutoCycle && nowUtc >= _nextPersistentCycle)
        {
            if (_persistentShown)
                _profileIndex = (_profileIndex + 1) % profiles.Count;
            else if (_profileIndex >= profiles.Count)
                _profileIndex = 0;

            _nextPersistentCycle = nowUtc.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
            _nextPersistentRefresh = DateTime.MinValue;
            _lastRenderedLocalSecond = -1;
        }
        else if (!_settings.AutoCycle)
        {
            int resolved = ResolveActiveProfileIndex(profiles);
            if (resolved != _profileIndex)
            {
                _profileIndex = resolved;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
            }
        }

        if (_profileIndex >= profiles.Count) _profileIndex = 0;
        var profile = profiles[_profileIndex];
        bool clockAccurate = HudProfileRenderer.NeedsSecondAccurateClock(profile);

        if (clockAccurate)
        {
            long localSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
            if (localSecond == _lastRenderedLocalSecond || _hud.IsHudBusy) return;
            _lastRenderedLocalSecond = localSecond;
        }
        else
        {
            if (nowUtc < _nextPersistentRefresh || _hud.IsHudBusy) return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0) return;

        try
        {
            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort);

            bool profileChanged = _persistentShown
                                  && _hud.IsVisible
                                  && !string.Equals(_persistentProfileId, profile.Id, StringComparison.OrdinalIgnoreCase);

            bool deepSeekPeriodChanged = false;
            if (IsDeepSeekProfile(profile)
                && vars.TryGetValue("deepseek.period.name_zh", out var periodValue)
                && periodValue is not null)
            {
                string currentPeriod = Convert.ToString(periodValue) ?? "";
                if (!string.IsNullOrWhiteSpace(_lastDeepSeekPeriod)
                    && !string.Equals(_lastDeepSeekPeriod, currentPeriod, StringComparison.Ordinal))
                {
                    deepSeekPeriodChanged = true;
                }
                _lastDeepSeekPeriod = currentPeriod;
            }

            // A peak/off-peak boundary is itself an event and always keeps its required
            // full animation. Ordinary automatic-cycle transitions use the dedicated cycle
            // animation mode and ignore each scheme's own AnimationMode.
            var renderProfile = deepSeekPeriodChanged
                ? profile with { AnimationMode = "Full" }
                : (_settings.AutoCycle ? ApplyCycleAnimationMode(profile) : profile);
            var data = HudProfileRenderer.Render(renderProfile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await _variables.SnapshotAsync(_settings, required, profile.GpuAdapterId, profile.PingTarget, profile.ProbeProtocol, profile.ProbePort, ct);
                return HudProfileRenderer.Render(renderProfile, latest);
            };

            if (profileChanged || deepSeekPeriodChanged)
            {
                await _hud.HideAnimatedAsync();
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
            }
            else if (!_persistentShown || !_hud.IsVisible)
            {
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
            }
            else
            {
                _hud.UpdatePersistent(data);
            }

            _persistentProfileId = profile.Id;
            _nextPersistentRefresh = clockAccurate ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(1);
            if (clockAccurate)
                _lastRenderedLocalSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        }
        catch
        {
            _nextPersistentRefresh = DateTime.UtcNow.AddSeconds(2);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private IReadOnlyList<HudProfile> ResolveCycleProfiles()
    {
        var ids = _settings.CycleProfileIds ?? new List<string>();
        if (ids.Count == 0 || _settings.Profiles.Count == 0)
            return Array.Empty<HudProfile>();

        var byId = _settings.Profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<HudProfile>(ids.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            if (byId.TryGetValue(id, out var profile))
                result.Add(profile);
        }
        return result;
    }

    private HudProfile ApplyCycleAnimationMode(HudProfile profile)
    {
        string mode = string.Equals(_settings.CycleAnimationMode, "Simple", StringComparison.OrdinalIgnoreCase)
            ? "Simple"
            : "Full";
        return profile with { AnimationMode = mode };
    }

    private HudProfile? ResolveActiveProfile()
    {
        var profiles = _settings.Profiles.ToList();
        if (profiles.Count == 0) return null;
        int index = ResolveActiveProfileIndex(profiles);
        return index >= 0 && index < profiles.Count ? profiles[index] : profiles[0];
    }

    private HudProfile FindBatteryProfile()
    {
        var existing = _settings.Profiles.FirstOrDefault(IsBatteryProfile);
        if (existing is not null) return existing;

        return CustomHudSettings.CreateDefaultProfiles().First(IsBatteryProfile);
    }

    private static bool IsBatteryProfile(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "system.battery", StringComparison.OrdinalIgnoreCase)
        || ((string.Equals(profile.Category, "系统", StringComparison.OrdinalIgnoreCase)
             || string.Equals(profile.Category, "System", StringComparison.OrdinalIgnoreCase))
            && (string.Equals(profile.Name, "电池", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.Name, "Battery", StringComparison.OrdinalIgnoreCase)));

    private static bool IsDeepSeekProfile(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "deepseek.balance-period", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(profile.Category, "DeepSeek API", StringComparison.OrdinalIgnoreCase)
            && (profile.Name.Contains("余额", StringComparison.OrdinalIgnoreCase)
                || profile.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase)));

    private int ResolveActiveProfileIndex()
    {
        var profiles = _settings.Profiles.ToList();
        return ResolveActiveProfileIndex(profiles);
    }

    private int ResolveActiveProfileIndex(IReadOnlyList<HudProfile> profiles)
    {
        if (profiles.Count == 0) return 0;
        if (!string.IsNullOrWhiteSpace(_settings.ActiveProfileId))
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].Id, _settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return 0;
    }

    // ==================================================================
    //  事件触发
    // ==================================================================

    /// <summary>
    /// 按 <c>settings.CustomHud.Triggers</c> 监视变量，命中就把 HUD 唤出，
    /// 并展示轮播队列里的下一项。返回 true 表示这一 tick 已经处理过呼出。
    /// </summary>
    private async Task<bool> TickTriggerRulesAsync()
    {
        var rules = _settings.Triggers;
        if (rules is null || rules.Count == 0)
        {
            _triggerBaselineCaptured = false;
            return false;
        }

        var now = DateTime.UtcNow;
        if (now < _nextTriggerPoll) return false;
        // 1 秒一次：换歌这类事件足够及时，同时不会把探测变量打成高频采样。
        _nextTriggerPoll = now.AddSeconds(1);

        // 只采集规则真正引用的变量，保持 VariableHub 的按需采集语义。
        var keys = rules
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Variable))
            .Select(r => r.Variable.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0) return false;

        var probeSource = ResolveTriggerProbeSource();
        Dictionary<string, object?> vars;
        try
        {
            vars = await _variables.SnapshotAsync(
                _settings,
                keys,
                probeSource?.GpuAdapterId,
                probeSource?.PingTarget,
                probeSource?.ProbeProtocol ?? "ICMP",
                probeSource?.ProbePort ?? 443).ConfigureAwait(true);
        }
        catch
        {
            return false;
        }

        // 第一次只记录基线，绝不触发 —— 否则启动瞬间就会误报。
        if (!_triggerBaselineCaptured)
        {
            foreach (var key in keys)
                _triggerLastValues[key] = vars.TryGetValue(key, out var baseline) ? baseline : null;
            _triggerBaselineCaptured = true;
            return false;
        }

        HudTriggerRule? fired = null;
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            string key = (rule.Variable ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            vars.TryGetValue(key, out var current);
            _triggerLastValues.TryGetValue(key, out var previous);

            bool hit = rule.Mode switch
            {
                "Change" => previous is not null && current is not null && !ValuesEqual(previous, current),
                "BecomesTrue" => !IsTruthy(previous) && IsTruthy(current),
                _ => MeetsThreshold(current, rule.Operator, rule.Value)
            };

            _triggerLastValues[key] = current;

            if (!hit) continue;
            if (_triggerNextAllowed.TryGetValue(RuleKey(rule), out var allowed) && now < allowed) continue;

            _triggerNextAllowed[RuleKey(rule)] = now.AddSeconds(Math.Max(0, rule.CooldownSeconds));
            fired = rule;
            break;
        }

        if (fired is null) return false;
        if (_hud.IsHudBusy || Volatile.Read(ref _busy) != 0) return false;

        var next = NextTransientCycleProfile();
        if (next is null) return false;

        return await TriggerTransientProfileAsync(next);
    }

    /// <summary>
    /// 取轮播队列里的下一项并推进游标 —— 这就是"每次呼出都是轮播的下一个"。
    /// 队列为空时退回当前激活方案，保证至少能弹出点东西。
    /// </summary>
    private HudProfile? NextTransientCycleProfile()
    {
        var queue = ResolveCycleProfiles();
        if (queue.Count == 0)
            return ResolveActiveProfile();

        _transientCycleIndex = _transientCycleIndex < 0
            ? 0
            : (_transientCycleIndex + 1) % queue.Count;

        return ApplyCycleAnimationMode(queue[_transientCycleIndex]);
    }

    /// <summary>
    /// 探测类变量需要目标/协议/端口。优先用轮播队列里的"网络包探测器"方案，
    /// 这样监听用的目标和用户在该方案里配置的一致。
    /// </summary>
    private HudProfile? ResolveTriggerProbeSource()
    {
        var ping = ResolveCycleProfiles().FirstOrDefault(p =>
            string.Equals(p.BuiltInKey, "network.ping", StringComparison.OrdinalIgnoreCase));
        return ping ?? ResolveActiveProfile();
    }

    private static string RuleKey(HudTriggerRule rule) =>
        string.Join("|", rule.Name ?? "", rule.Variable ?? "", rule.Mode ?? "");

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (TryNumber(a, out double na) && TryNumber(b, out double nb))
            return Math.Abs(na - nb) < 1e-6;
        return string.Equals(
            Convert.ToString(a, CultureInfo.InvariantCulture),
            Convert.ToString(b, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool TryNumber(object? value, out double number)
    {
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number);
        }
        catch
        {
            number = 0d;
            return false;
        }
    }

    private static bool IsTruthy(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        if (TryNumber(value, out double n)) return Math.Abs(n) > double.Epsilon;
        return !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static bool MeetsThreshold(object? value, string op, double target)
    {
        if (value is null || !TryNumber(value, out double n)) return false;
        return op switch
        {
            ">" => n > target,
            "<" => n < target,
            "<=" => n <= target,
            "==" => Math.Abs(n - target) < 1e-6,
            "!=" => Math.Abs(n - target) >= 1e-6,
            _ => n >= target
        };
    }

    public void Dispose()
    {
        _timer.Stop();
        _variables.Dispose();
    }
}
