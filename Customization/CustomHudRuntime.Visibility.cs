namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    // Session-only suppression, independent of enabled instances and saved profiles.
    public bool IsUserHidden { get; private set; }

    public Task ToggleUserVisibilityAsync() => SetUserHiddenAsync(!IsUserHidden);

    private Task SetUserHiddenAsync(bool hidden)
    {
        if (_disposed) return Task.CompletedTask;
        IsUserHidden = hidden;
        CancelRevision();
        // A shortcut may interrupt Save & Apply's retract animation.
        if (_pendingSettings is { } pending)
        {
            _hud.ApplySettings(pending);
            ConfigureSettings(pending);
        }
        _persistentShown = false;
        _persistentProfileId = "";
        _nextPersistentRefresh = DateTime.MinValue;
        _lastRenderedLocalSecond = -1;
        _hotZone.Reset();
        _pendingPowerEvent = false;
        _pendingDeepSeekPeriodTransition = false;
        ResetTriggerRules();
        var children = _instances.Values.Select(i => i.Runtime.SetUserHiddenAsync(hidden)).ToList();
        if (hidden)
        {
            children.Add(RunSerializedAsync(() => _hud.HideAnimatedAsync(force:true), wait:true));
        }
        else if (_running)
        {
            _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
            children.Add(RunSerializedAsync(ShowFromHotkeyAsync, wait: true));
        }
        return Task.WhenAll(children);
    }

    private async Task ShowFromHotkeyAsync()
    {
        if (_disposed || IsUserHidden || (!_appSettings.HudEnabled && !_editingPreview)) return;
        var queue=ResolveCycleProfiles();
        var profile=_settings.Profiles.FirstOrDefault(p=>p.Id==_manualProfileId)
            ?? (_settings.AutoCycle && queue.Count>0 ? queue[Math.Clamp(_profileIndex,0,queue.Count-1)] : ResolveActiveProfile());
        if (profile is null) return;
        profile=profile with { AnimationMode=_appSettings.HudHotkeyShowAnimation };
        if (!_appSettings.AlwaysVisible && !_editingPreview)
        {
            await TriggerTransientProfileAsync(profile);
            return;
        }
        var required=HudProfileRenderer.GetRequiredVariables(profile);
        var values=await SnapshotProfileAsync(profile,required);
        _activeToken.ThrowIfCancellationRequested();
        await _hud.ShowPersistentAsync(HudProfileRenderer.Render(profile,values),async ct =>
            HudProfileRenderer.Render(profile,await SnapshotProfileAsync(profile,required,ct)));
        _activeToken.ThrowIfCancellationRequested();
        _persistentShown=true;
        _persistentProfileId=profile.Id;
        _nextPersistentRefresh=DateTime.MinValue;
    }
}
