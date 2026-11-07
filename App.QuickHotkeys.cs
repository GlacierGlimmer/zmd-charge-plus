using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus;
public partial class App
{
    private HudQuickHotkeys? _quickHotkeys;
    private void InitializeQuickHotkeys(AppSettings settings,Action openSettings)
    {
        try
        {
            _quickHotkeys=new HudQuickHotkeys(action=>
            {
                if(_exiting)return;
                if(_settingsWindow?.CaptureRegisteredShortcut(_quickHotkeys?.BindingFor(action)??"")==true || _runtime is null)return;
                switch(action)
                {
                    case "toggle":_=_runtime.ToggleUserVisibilityAsync();break;
                    case "previous":_runtime.StepProfiles(-1);break;
                    case "next":_runtime.StepProfiles(1);break;
                    case "pause":_runtime.ToggleAutoCyclePaused();break;
                    case "preview":_=_runtime.PreviewAllAsync();break;
                    case "settings":openSettings();break;
                }
            });
            if(!_quickHotkeys.TryApply(settings,()=>{}))AppLog.Warn("A HUD shortcut could not be registered.");
        }
        catch(Exception ex){AppLog.Error("Unable to initialize HUD shortcuts.",ex);}
    }
}
