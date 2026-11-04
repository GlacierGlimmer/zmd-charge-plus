namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private IReadOnlyList<HudProfile> GetRadialProfiles()=>_settings.RadialProfileIds is null ? _settings.Profiles :
        _settings.RadialProfileIds.Select(id=>_settings.Profiles.FirstOrDefault(p=>p.Id==id)).OfType<HudProfile>().ToList();
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
