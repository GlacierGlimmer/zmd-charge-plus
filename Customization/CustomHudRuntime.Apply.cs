using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private bool _instantSettingsRefresh;
    // Applying preferences is an acknowledgement, not a replay of the full intro.
    // Sampling/rendering continues through the same serialized runtime afterwards.
    public async Task ApplySavedSettingsAsync(AppSettings settings)
    {
        if(_disposed)return;
        _pendingSettings=null;CancelRevision();
        await RunSerializedAsync(async()=>
        {
            _hud.CancelPresentation();
            _hud.ApplySettings(settings);ConfigureSettings(settings);
            _instantSettingsRefresh=true;
            foreach(var child in _instances.Values)child.Runtime._instantSettingsRefresh=true;
            if(IsUserHidden || !settings.HudEnabled || (!settings.AlwaysVisible && !_editingPreview))
                await _hud.HideAnimatedAsync();
            else if(_hud.IsVisible)_hud.CompletePersistentPresentation();
        },wait:true);
        // Request the first fresh frame immediately, without waiting for the next timer tick.
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(!_disposed)_=RunSerializedAsync(TickAsync,wait:true);},Avalonia.Threading.DispatcherPriority.Background);
    }
}
