namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private readonly HudTriggerEvaluator _triggers=new();
    private long _nextTriggerPoll;
    private int _eventCycleIndex=-1;
    private void ResetTriggerRules() { _triggers.Reset();_nextTriggerPoll=0;_eventCycleIndex=-1; }

    internal (CustomHudSettings Settings,HudProfile? Active,HudProfile? Probe) GetTriggerSamplingContext()
    {
        var active=ResolveActiveProfile();
        var probe=ResolveCycleProfiles().FirstOrDefault(p=>p.BuiltInKey=="network.ping") ?? active;
        return (active is null ? _settings : _settings with {ActiveProfileId=active.Id},active,probe);
    }

    private async Task<bool> TickTriggerRulesAsync()
    {
        if(_disposed || IsUserHidden || _appSettings.AlwaysVisible || _editingPreview) return false;
        long now=Environment.TickCount64;
        if(now<_nextTriggerPoll || !_settings.Triggers.Any(r=>r.Enabled)) return false;
        _nextTriggerPoll=now+1000;
        var keys=_settings.Triggers.Where(r=>r.Enabled && !string.IsNullOrWhiteSpace(r.Variable)).Select(r=>r.Variable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(keys.Length==0) return false;
        var context=GetTriggerSamplingContext();
        var snapshot=await _variables.SnapshotAsync(context.Settings,keys,context.Active?.GpuAdapterId,context.Probe?.PingTarget,context.Probe?.ProbeProtocol,context.Probe?.ProbePort ?? 443,_activeToken);
        _activeToken.ThrowIfCancellationRequested();
        var matches=_triggers.Evaluate(_settings.Triggers,snapshot,Environment.TickCount64);
        if(matches.Count==0 || _hud.IsHudBusy || Volatile.Read(ref _busy)!=0) return false;
        var queue=ResolveCycleProfiles();
        int next=queue.Count==0 ? -1 : (_eventCycleIndex+1)%queue.Count;
        var profile=next<0 ? ResolveActiveProfile() : ApplyCycleAnimationMode(queue[next]);
        if(profile is null) return false;
        // Capture cooldown at presentation start, even though a transient summon
        // keeps its normal show/hold/hide lifecycle inside this awaited operation.
        long started=Environment.TickCount64;
        if(!await TriggerTransientProfileAsync(profile)) return false;
        _eventCycleIndex=next;
        _triggers.Presented(matches,started);
        return true;
    }
}
