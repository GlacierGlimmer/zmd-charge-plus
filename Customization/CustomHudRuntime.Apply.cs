using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private bool _instantSettingsRefresh;
    // Ordinary preference edits refresh immediately. A different selected profile
    // retains its configured reveal animation, independently for each HUD.
    // Sampling/rendering continues through the same serialized runtime afterwards.
    public async Task ApplySavedSettingsAsync(AppSettings settings)
    {
        if(_disposed)return;
        _pendingSettings=null;CancelRevision();
        await RunSerializedAsync(async()=>
        {
            var oldProfiles=new Dictionary<CustomHudRuntime,string>{{this,CurrentProfileId()}};
            foreach(var child in _instances.Values)oldProfiles[child.Runtime]=child.Runtime.CurrentProfileId();
            _hud.CancelPresentation();
            _hud.ApplySettings(settings);ConfigureSettings(settings);
            MarkSettingsPresentation(oldProfiles);
            foreach(var child in _instances.Values)child.Runtime.MarkSettingsPresentation(oldProfiles);
            if(IsUserHidden || !settings.HudEnabled || (!settings.AlwaysVisible && !_editingPreview))
                await _hud.HideAnimatedAsync();
            else if(_hud.IsVisible)_hud.CompletePersistentPresentation();
        },wait:true);
        // Request the first fresh frame immediately, without waiting for the next timer tick.
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(!_disposed)_=RunSerializedAsync(TickAsync,wait:true);},Avalonia.Threading.DispatcherPriority.Background);
    }
    private void MarkSettingsPresentation(Dictionary<CustomHudRuntime,string> previous)
    {
        string selected=ResolveActiveProfile()?.Id??"";
        bool changed=!previous.TryGetValue(this,out var old) || old!=selected;
        _instantSettingsRefresh=!changed;
        _settingsProfileTransition=changed;
        if(changed)_manualProfileId=selected;
        if(changed && !string.IsNullOrEmpty(old))_persistentProfileId=old;
    }
    private bool _settingsProfileTransition;
}
