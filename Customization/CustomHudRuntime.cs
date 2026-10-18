using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime : IDisposable
{
    private readonly HudWindow _hud;
    private readonly VariableHub _variables;
    private readonly bool _ownsVariables;
    private readonly string _warningScope=Guid.NewGuid().ToString("N");
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _operation = new(1,1);
    private CancellationTokenSource _revision = new();
    private CancellationToken _activeToken;
    private bool _disposed, _running;
    private Task _lastOperation = Task.CompletedTask;

    private void CancelRevision()
    {
        var previous=_revision;
        _revision=new CancellationTokenSource();
        // An interrupted close/summon no longer represents a completed
        // persistent presentation, even when its old profile ID is unchanged.
        if (_hud.IsHudBusy && !_hud.IsRolling) _persistentShown=false;
        previous.Cancel(); previous.Dispose();
        _hud.CancelPresentation();
    }

    private Task RunSerializedAsync(Func<Task> action, bool wait = false)
    {
        if (_disposed) return Task.CompletedTask;
        var token=_revision.Token;
        var task=Run();
        _lastOperation=task;
        return task;
        async Task Run()
        {
            bool entered=false;
            try
            {
                entered=wait ? await _operation.WaitAsync(Timeout.Infinite,token) : await _operation.WaitAsync(0,token);
                if (!entered || _disposed) return;
                token.ThrowIfCancellationRequested(); _activeToken=token;
                await action();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { EndfieldChargePlus.Diagnostics.AppLog.Error("HUD operation failed.",ex); }
            finally { if (entered) _operation.Release(); }
        }
    }

    private AppSettings _appSettings = new();
    private AppSettings? _pendingSettings;
    private CustomHudSettings _settings = CustomHudSettings.CreateDefault();

    private DateTime _nextPersistentRefresh = DateTime.MinValue;
    private DateTime _nextPersistentCycle = DateTime.MinValue;
    private DateTime _nextPowerPoll = DateTime.MinValue;
    private long _lastRenderedLocalSecond = -1;
    private int _profileIndex;
    private string _manualProfileId = "";
    private int _busy;
    private int _settingsTransitionBusy;
    private bool _persistentShown;
    private string _persistentProfileId = "";

    private readonly HotZoneDwellGate _hotZone=new();
    private bool? _lastAcOnline;
    private bool? _candidateAcOnline;
    private DateTime _candidateAcSince = DateTime.MinValue;
    private bool _pendingPowerEvent;
    private bool _pendingAcOnline;

    private DateTime _nextDeepSeekPeriodPoll = DateTime.MinValue;
    private string? _lastDeepSeekPeriod;
    private bool _pendingDeepSeekPeriodTransition;

    public CustomHudRuntime(HudWindow hud, VariableHub? variables=null)
    {
        _variables=variables ?? new();
        _ownsVariables=variables is null;
        _hud = hud;
        _hud.HudClicked += OnHudClicked;
        // 100 ms：用于屏幕顶边热点检测和墙钟秒边界同步。重型指标仍按需采样。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => { ObserveHotZone();_ = RunSerializedAsync(TickAsync); };
        _hud.Closed += OnHudClosed;
    }

    public void ApplySettings(AppSettings settings)
    {
        if (_disposed) return;
        CancelRevision();
        ConfigureSettings(settings);
    }

    private void ConfigureSettings(AppSettings settings)
    {
        _pendingSettings = null;
        _appSettings = settings ?? new AppSettings();
        SyncInstances(_appSettings);
        _variables.SamplingIntervalSeconds = _appSettings.SamplingIntervalSeconds;
        _settings = HudSettingsNormalizer.Normalize(_appSettings.CustomHud ?? CustomHudSettings.CreateDefault());
        RestoreInstanceHighlight();
        _manualProfileId="";
        _profileIndex = _settings.AutoCycle ? 0 : ResolveActiveProfileIndex();
        _nextPersistentRefresh = DateTime.MinValue;
        _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
        _lastRenderedLocalSecond = -1;
        _hotZone.Reset();
        _persistentProfileId = "";
        _nextDeepSeekPeriodPoll = DateTime.MinValue;
        _pendingDeepSeekPeriodTransition = false;

        ResetTriggerRules();

        var activeProfile = _settings.AutoCycle
            ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
            : ResolveActiveProfile();
        _lastDeepSeekPeriod = activeProfile is not null && IsDeepSeekProfile(activeProfile)
            ? VariableHub.GetDeepSeekPeriodNameZh(_settings)
            : null;

        if (!_appSettings.AlwaysVisible || !_appSettings.HudEnabled)
            _persistentShown = false;
    }

    public Task ApplySettingsWithTransitionAsync(AppSettings settings)
    {
        if (_disposed) return Task.CompletedTask;
        _pendingSettings = settings;
        CancelRevision();
        return RunSerializedAsync(() => ApplyTransitionAsync(settings),wait:true);
    }

    private async Task ApplyTransitionAsync(AppSettings settings)
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            // Every applied profile/parameter change follows the same full lifecycle:
            // retract old HUD -> apply settings -> summon the new HUD. Never mutate a visible HUD in place.
            if (_hud.IsVisible)
                await _hud.HideAnimatedAsync();
                _activeToken.ThrowIfCancellationRequested();

            _persistentShown = false;
            _hud.ApplySettings(settings);
            ConfigureSettings(settings);

            if (IsUserHidden || !settings.HudEnabled)
                return;

            var profile = _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (_settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await SnapshotProfileAsync(profile, required);
            _activeToken.ThrowIfCancellationRequested();
            var data = HudProfileRenderer.Render(profile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await SnapshotProfileAsync(profile, required, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            if (settings.AlwaysVisible)
            {
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
                _activeToken.ThrowIfCancellationRequested();
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
            if (!_disposed && _running) _timer.Start();
            Interlocked.Exchange(ref _settingsTransitionBusy, 0);
        }
    }

    // Kept for compatibility with older callers.
    public void ApplySettings(CustomHudSettings settings)
    {
        ApplySettings(_appSettings with { CustomHud = settings ?? CustomHudSettings.CreateDefault() });
    }

    public void Start()
    {
        if (_disposed || _running) return;
        _running=true;
        RunSerializedAsync(RunStartupPresentationAsync,wait:true);
    }
    public void Stop() { _running=false; _timer.Stop(); CancelRevision(); }

    private async Task RunStartupPresentationAsync()
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            if (IsUserHidden || (!_appSettings.HudEnabled && !_editingPreview))
                return;

            // Every process start presents the currently effective scheme once through
            // its normal summon animation. If AlwaysVisible is enabled, the same animation
            // lands in the persistent C-state and live monitoring continues from there.
            var profile = _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (_settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await SnapshotProfileAsync(profile, required);
            _activeToken.ThrowIfCancellationRequested();
            var data = HudProfileRenderer.Render(profile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await SnapshotProfileAsync(profile, required, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            if (_appSettings.AlwaysVisible || _editingPreview)
            {
                await _hud.ShowPersistentAsync(data, refresh);
                _persistentShown = true;
                _activeToken.ThrowIfCancellationRequested();
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
            if (!_disposed && _running) _timer.Start();
        }
    }

    public async Task PreviewAsync(HudProfile profile)
    {
        if (IsUserHidden) return;
        await RunSerializedAsync(async () => { await TriggerTransientProfileAsync(profile); },wait:true);
    }

    public async Task PreviewActiveAsync()
    {
        var profile = ResolveActiveProfile();
        if (profile is not null)
            await PreviewAsync(profile);
    }

    private async Task TickAsync()
    {
        if (_disposed || IsUserHidden) return;
        if (Volatile.Read(ref _settingsTransitionBusy) != 0) return;
        KeepFrameCaptureAlive();

        if (!_appSettings.HudEnabled && !_editingPreview)
        {
            if (_persistentShown)
            {
                await _hud.HidePersistentAsync();
                _persistentShown = false;
                _persistentProfileId = "";
            }
            return;
        }

        if (_appSettings.AlwaysVisible || _editingPreview)
        {
            _hotZone.Reset();
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

        if(await TickTriggerRulesAsync()) return;

        // Each transient carousel entry follows its normal show/hold/hide lifecycle.
        // A busy animation delays the next entry instead of skipping queued profiles.
        if (_settings.AutoCycle && DateTime.UtcNow >= _nextPersistentCycle
            && !_hud.IsHudBusy && Volatile.Read(ref _busy) == 0)
        {
            var queue = ResolveCycleProfiles();
            if (queue.Count > 0)
            {
                int next = (_profileIndex + 1) % queue.Count;
                _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
                if (await TriggerTransientProfileAsync(ApplyCycleAnimationMode(queue[next]))) _profileIndex = next;
                return;
            }
        }

        if (!_hotZone.Ready(Environment.TickCount64,_appSettings.HotZoneDwellMilliseconds) || _hud.IsHudBusy || Volatile.Read(ref _busy) != 0)
            return;

        var hotQueue=ResolveCycleProfiles();
        int hotNext=_appSettings.HotZoneAdvanceQueue && hotQueue.Count>0 ? (_eventCycleIndex+1)%hotQueue.Count : -1;
        var active = hotNext<0 ? ResolveActiveProfile() : ApplyCycleAnimationMode(hotQueue[hotNext]);
        if (active is null)
            return;

        _hotZone.Presented();
        if (await TriggerTransientProfileAsync(active)) { if(hotNext>=0)_eventCycleIndex=hotNext; }
        else _hotZone.Reset();
    }

    private void ObserveHotZone()
    {
        if(_disposed || IsUserHidden || !_appSettings.HudEnabled || _appSettings.AlwaysVisible || _editingPreview || !_appSettings.HotZoneEnabled)
        { _hotZone.Reset();return; }
        if(WindowsSystemProbe.TryGetCursorPosition(out var pointer))
            _hotZone.Observe(_hud.IsPointInConfiguredHotZone(pointer),Environment.TickCount64);
        else _hotZone.Reset();
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
        if (_disposed || IsUserHidden) return false;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        try
        {
            var required = HudProfileRenderer.GetRequiredVariables(profile);
            var vars = await SnapshotProfileAsync(profile, required);
            _activeToken.ThrowIfCancellationRequested();
            var data = HudProfileRenderer.Render(profile, vars);

            // Transient HUD data keeps updating while the original show/hold/hide animation runs.
            // Sampling stays off the UI thread and VariableHub only reads variables required by this scheme.
            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await SnapshotProfileAsync(profile, required, ct);
                return HudProfileRenderer.Render(profile, latest);
            };

            await _hud.ShowCustomAsync(data, refresh);
            _activeToken.ThrowIfCancellationRequested();
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
        if (_disposed || IsUserHidden) return;
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

        if (_settings.AutoCycle && nowUtc >= _nextPersistentCycle && !_hud.IsHudBusy && Volatile.Read(ref _busy) == 0)
        {
            if (_persistentShown)
            {
                _profileIndex = (_profileIndex + 1) % profiles.Count;
                _manualProfileId="";
            }
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
            var vars = await SnapshotProfileAsync(profile, required);

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
                : (_settings.AutoCycle || !string.IsNullOrWhiteSpace(_manualProfileId) ? ApplyCycleAnimationMode(profile) : profile);
            _activeToken.ThrowIfCancellationRequested();
            var data = HudProfileRenderer.Render(renderProfile, vars);

            Func<CancellationToken, Task<HudRenderData>> refresh = async ct =>
            {
                var latest = await SnapshotProfileAsync(profile, required, ct);
                return HudProfileRenderer.Render(renderProfile, latest);
            };

            if (profileChanged || deepSeekPeriodChanged)
            {
                if (profileChanged && !deepSeekPeriodChanged && _settings.CycleAnimationMode=="Roll")
                    await _hud.RollToPersistentAsync(data,refresh);
                else
                {
                    await _hud.HideAnimatedAsync();
                    _activeToken.ThrowIfCancellationRequested();
                    await _hud.ShowPersistentAsync(data, refresh);
                }
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

            _activeToken.ThrowIfCancellationRequested();
            _persistentProfileId = profile.Id;
            _nextPersistentRefresh = clockAccurate ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(NextSamplingInterval(profile,vars));
            if (clockAccurate)
                _lastRenderedLocalSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        }
        catch (OperationCanceledException) { throw; }
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
        string activeId=string.IsNullOrWhiteSpace(_manualProfileId) ? _settings.ActiveProfileId : _manualProfileId;
        if (!string.IsNullOrWhiteSpace(activeId))
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].Id, activeId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return 0;
    }

    private void OnHudClicked()
    {
        if (_disposed || !_appSettings.AlwaysVisible || !_appSettings.ClickToCycle) return;
        var queue=ResolveCycleProfiles();
        if (queue.Count<2) return;
        string current=string.IsNullOrEmpty(_manualProfileId)
            ? (string.IsNullOrEmpty(_persistentProfileId) ? ResolveActiveProfile()?.Id ?? "" : _persistentProfileId)
            : _manualProfileId;
        int index=queue.ToList().FindIndex(p => string.Equals(p.Id,current,StringComparison.OrdinalIgnoreCase));
        _manualProfileId=queue[(index+1)%queue.Count].Id;
        _profileIndex=_settings.AutoCycle ? (index+1)%queue.Count : ResolveActiveProfileIndex();
        _nextPersistentCycle=DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds,3,3600));
        _nextPersistentRefresh=DateTime.MinValue; _lastRenderedLocalSecond=-1;
        // Interrupt an obsolete sample/animation and retain the latest requested profile.
        CancelRevision();
        RunSerializedAsync(TickAsync,wait:true);
    }

    private async Task<Dictionary<string,object?>> SnapshotProfileAsync(HudProfile profile, IEnumerable<string> required, CancellationToken ct=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_revision.Token);
        linked.Token.ThrowIfCancellationRequested();
        var values=await _variables.SnapshotAsync(_settings with { ActiveProfileId=profile.Id },required,profile.GpuAdapterId,profile.PingTarget,profile.ProbeProtocol,profile.ProbePort,linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        ProfileDataWarnings.Check(profile,values,_warningScope);
        return values;
    }

    private void OnHudClosed(object? sender,EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true; _running=false;
        _timer.Stop(); _revision.Cancel(); _hud.CancelPresentation();
        ProfileDataWarnings.Forget(_warningScope);
        _hud.HudClicked -= OnHudClicked;
        _hud.Closed -= OnHudClosed;
        DisposeInstances();
        if (!_isChild) FrameRateBackend.Stop();
        if (_ownsVariables) _variables.Dispose();
    }
}
