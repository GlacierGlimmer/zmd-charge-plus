using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Settings;
public partial class SettingsWindow
{
    private void InitializeRuntimeSelection()
    {
        _runtime.ProfileSelected+=OnRuntimeProfileSelected;
        Closed+=(_,_)=>_runtime.ProfileSelected-=OnRuntimeProfileSelected;
        OnRuntimeProfileSelected(_selectedInstanceId,_runtime.CurrentProfileId(_selectedInstanceId));
    }
    private void OnRuntimeProfileSelected(string instanceId,string profileId)
    {
        if(instanceId!=_selectedInstanceId || string.IsNullOrEmpty(profileId))return;
        if(!Customizer.SelectRuntimeProfile(profileId))return;
        if(string.IsNullOrEmpty(instanceId))
        {
            _rootSettings=_rootSettings with {CustomHud=_rootSettings.CustomHud with {ActiveProfileId=profileId}};
            _settings=_rootSettings;
        }
        else
            _rootSettings=_rootSettings with {HudInstances=_rootSettings.HudInstances.Select(i=>i.Id!=instanceId?i:i with {
                Settings=i.Settings with {CustomHud=i.Settings.CustomHud with {ActiveProfileId=profileId}}}).ToList()};
    }
}
