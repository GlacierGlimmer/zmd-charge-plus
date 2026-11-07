namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private object? _radialSourceProfiles,_radialSourceIds;
    private IReadOnlyList<HudProfile> _radialCached=Array.Empty<HudProfile>();
    private IReadOnlyList<HudProfile> GetRadialProfiles()
    {
        if(_settings.RadialProfileIds is null)return _settings.Profiles;
        if(ReferenceEquals(_radialSourceProfiles,_settings.Profiles) && ReferenceEquals(_radialSourceIds,_settings.RadialProfileIds))return _radialCached;
        _radialSourceProfiles=_settings.Profiles;_radialSourceIds=_settings.RadialProfileIds;
        var byId=_settings.Profiles.ToDictionary(p=>p.Id);
        return _radialCached=_settings.RadialProfileIds.Where(byId.ContainsKey).Select(id=>byId[id]).ToArray();
    }
    private void OnRadialOpened()=>CancelRevision();
    private void OnRadialFinished(string? id)
    {
        if(_disposed || IsUserHidden || !_appSettings.HudEnabled)return;
        _instantSettingsRefresh=false;_settingsProfileTransition=false;
        if(id is not null && _settings.Profiles.Any(p=>p.Id==id)) {
            _manualProfileId=id;
            _manualRollDirection=0;NotifyProfileSelected(id);
            int index=ResolveCycleProfiles().ToList().FindIndex(p=>p.Id==id);_profileIndex=index>=0?index:Math.Max(0,ResolveCycleProfiles().Count-1);
        }
        _nextPersistentCycle=DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds,3,3600));
        _nextPersistentRefresh=DateTime.MinValue;_lastRenderedLocalSecond=-1;
        CancelRevision();
        // Both selection and cancellation restore the interrupted transient lifecycle.
        _=RunSerializedAsync(()=>ShowSelectedProfileAsync(false),wait:true);
    }
}
