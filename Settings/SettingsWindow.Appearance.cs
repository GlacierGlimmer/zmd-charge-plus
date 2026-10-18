using EndfieldChargePlus.Customization;
namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private async Task ApplyGlobalAppearanceAsync(double font,double ui)
    {
        font=GlobalAppearance.NormalizeScale(font);ui=GlobalAppearance.NormalizeScale(ui);
        static AppSettings Scale(AppSettings value,double font,double ui)=>value with {
            GlobalFontScale=font,GlobalUiScale=ui,
            HudInstances=value.HudInstances.Select(i=>i with {Settings=i.Settings with {GlobalFontScale=font,GlobalUiScale=ui}}).ToList() };
        var persisted=Scale(_persistedRootSettings,font,ui);
        // Persist only these preferences. Pending profile, JSON and credential edits stay pending.
        PersistLanguagePreference(persisted);
        _persistedRootSettings=persisted;
        _rootSettings=Scale(_rootSettings,font,ui);
        _settings=Scale(_settings,font,ui);
        GlobalAppearance.Apply(this,font,ui);
        await _runtime.ApplyGlobalAppearanceAsync(font,ui);
    }
}
