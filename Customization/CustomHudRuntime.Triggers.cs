namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private readonly HudTriggerEvaluator _triggers=new();
    private long _nextTriggerPoll,_triggerRetryAfter;
    // Coalesce repeated samples to one pending presentation per rule.
    private readonly Dictionary<string,HudTriggerRule> _pendingTriggerRules=new(StringComparer.OrdinalIgnoreCase);
    private int _triggerObservationBusy,_triggerGeneration;
    private int _ruleCycleIndex=-1,_hotZoneCycleIndex=-1;
    private bool _triggerMonitoring,_lastTransientWasRule;
    private bool CanMonitorTriggers=>!_disposed && !IsUserHidden && _pendingSettings is null && _appSettings.HudEnabled && !_appSettings.AlwaysVisible && !_editingPreview;
    private void ResetTriggerRules()
    {
        _triggerGeneration++;_triggers.Reset();_pendingTriggerRules.Clear();_nextTriggerPoll=0;_triggerRetryAfter=0;
        _ruleCycleIndex=-1;_hotZoneCycleIndex=-1;_triggerMonitoring=false;_lastTransientWasRule=false;
        var queue=ResolveCycleProfiles();
        for(int i=0;i<queue.Count;i++)if(string.Equals(queue[i].Id,_settings.ActiveProfileId,StringComparison.OrdinalIgnoreCase)){_hotZoneCycleIndex=i;break;}
    }

    internal (CustomHudSettings Settings,HudProfile? Active,HudProfile? Probe) GetTriggerSamplingContext()
    {
        var active=ResolveActiveProfile();
        var probe=ResolveCycleProfiles().FirstOrDefault(p=>p.BuiltInKey=="network.ping") ?? active;
        return (active is null ? _settings : _settings with {ActiveProfileId=active.Id},active,probe);
    }

    // Observation runs outside the presentation semaphore, including during
    // show/hold/hide. Each runtime still has at most one sampling request in flight.
    private async Task ObserveTriggerRulesAsync()
    {
        if(!CanMonitorTriggers)
        {
            if(_triggerMonitoring)ResetTriggerRules();
            return;
        }
        _triggerMonitoring=true;
        long now=Environment.TickCount64;
        if(now<_nextTriggerPoll || !_settings.Triggers.Any(r=>r.Enabled)
            || Interlocked.CompareExchange(ref _triggerObservationBusy,1,0)!=0)return;
        int generation=_triggerGeneration;
        var settings=_settings;var token=_revision.Token;
        double interval=double.IsFinite(_appSettings.SamplingIntervalSeconds) ? Math.Clamp(_appSettings.SamplingIntervalSeconds,.1,60) : 1;
        _nextTriggerPoll=now+(long)(interval*1000);
        try
        {
            var keys=settings.Triggers.Where(r=>r.Enabled && !string.IsNullOrWhiteSpace(r.Variable)).Select(r=>r.Variable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(keys.Length==0)return;
            var context=GetTriggerSamplingContext();
            var snapshot=await _variables.SnapshotAsync(context.Settings,keys,context.Active?.GpuAdapterId,context.Probe?.PingTarget,context.Probe?.ProbeProtocol,context.Probe?.ProbePort ?? 443,token);
            if(token.IsCancellationRequested || generation!=_triggerGeneration || !CanMonitorTriggers)return;
            foreach(var rule in _triggers.Evaluate(settings.Triggers,snapshot,Environment.TickCount64))
                _pendingTriggerRules.TryAdd(rule.Id,rule);
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(!_disposed && !token.IsCancellationRequested)
                EndfieldChargePlus.Diagnostics.AppLog.Error("Trigger sampling failed.",ex);
        }
        finally{Interlocked.Exchange(ref _triggerObservationBusy,0);}
    }

    private async Task<bool> TickTriggerRulesAsync()
    {
        if(!CanMonitorTriggers || _pendingTriggerRules.Count==0 || Environment.TickCount64<_triggerRetryAfter || _hud.IsHudBusy || Volatile.Read(ref _busy)!=0)return false;
        var matches=_pendingTriggerRules.Values.ToArray();
        var queue=ResolveCycleProfiles();
        int next=queue.Count==0 ? -1 : (_ruleCycleIndex+1)%queue.Count;
        var profile=next<0 ? ResolveActiveProfile() : ApplyCycleAnimationMode(queue[next]);
        if(profile is null) return false;
        int generation=_triggerGeneration;
        foreach(var rule in matches)_pendingTriggerRules.Remove(rule.Id);
        _triggers.Presented(matches,Environment.TickCount64);
        bool shown=await TriggerTransientProfileAsync(profile);
        if(generation!=_triggerGeneration)return shown;
        if(shown){_ruleCycleIndex=next;_lastTransientWasRule=true;}
        else
        {
            _triggerRetryAfter=Environment.TickCount64+1000;
            foreach(var rule in matches)_pendingTriggerRules.TryAdd(rule.Id,rule);
        }
        return shown;
    }

    private async Task<bool> TryShowHotZoneAsync()
    {
        if(!_hotZone.Ready(Environment.TickCount64,_appSettings.HotZoneDwellMilliseconds) || _hud.IsHudBusy || Volatile.Read(ref _busy)!=0)return false;
        var queue=ResolveCycleProfiles();
        int next=_appSettings.HotZoneAdvanceQueue && queue.Count>0 ? (_hotZoneCycleIndex+1)%queue.Count : -1;
        var profile=next<0 ? ResolveActiveProfile() : ApplyCycleAnimationMode(queue[next]);
        if(profile is null)return false;
        int generation=_triggerGeneration;
        _hotZone.Presented();
        bool shown=await TriggerTransientProfileAsync(profile);
        if(generation!=_triggerGeneration)return shown;
        if(shown){if(next>=0)_hotZoneCycleIndex=next;}
        else _hotZone.Reset();
        return shown;
    }
}
