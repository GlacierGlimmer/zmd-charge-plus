namespace EndfieldChargePlus.Customization;
public sealed partial class CustomHudRuntime
{
    private DateTime _nextFrameHeartbeat=DateTime.MinValue, _framePrimingSince=DateTime.MinValue;
    private string _framePrimingProfile="";
    private void KeepFrameCaptureAlive()
    {
        if ((!_appSettings.HudEnabled && !_editingPreview) || DateTime.UtcNow<_nextFrameHeartbeat) return;
        _nextFrameHeartbeat=DateTime.UtcNow.AddSeconds(1);
        var profile=_settings.Profiles.FirstOrDefault(p=>p.Id==_manualProfileId) ?? (_settings.AutoCycle ? ResolveCycleProfiles().ElementAtOrDefault(_profileIndex) ?? ResolveActiveProfile() : ResolveActiveProfile());
        if(profile is null) return;
        var keys=HudProfileRenderer.GetRequiredVariables(profile);
        foreach(string kind in new[] { "display","window" })
            if(keys.Any(k=>k.StartsWith("frame."+kind+"."))) FrameRateBackend.KeepAlive(kind,kind=="display" ? profile.FrameDisplayId : profile.FrameWindowId);
    }
    private double NextSamplingInterval(HudProfile profile,IReadOnlyDictionary<string,object?> values)
    {
        if(_framePrimingProfile!=profile.Id) { _framePrimingProfile=profile.Id; _framePrimingSince=DateTime.UtcNow; }
        bool warming=values.Any(p=>p.Key.StartsWith("frame.") && p.Key.EndsWith(".fps") && p.Value is null);
        return warming && DateTime.UtcNow-_framePrimingSince<TimeSpan.FromSeconds(5) ? Math.Min(.5,_appSettings.SamplingIntervalSeconds) : _appSettings.SamplingIntervalSeconds;
    }
}
