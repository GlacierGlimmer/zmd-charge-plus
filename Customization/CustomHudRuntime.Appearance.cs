namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    public async Task ApplyGlobalAppearanceAsync(double font,double ui)
    {
        if(_disposed)return;
        font=Settings.GlobalAppearance.NormalizeFontScale(font);ui=Settings.GlobalAppearance.NormalizeUiScale(ui);
        var next=_appSettings with {GlobalFontScale=font,GlobalUiScale=ui,
            HudInstances=_appSettings.HudInstances.Select(i=>i with {Settings=i.Settings with {GlobalFontScale=font,GlobalUiScale=ui}}).ToList()};
        // Use applied settings here; unsaved editor fields must never reach a live HUD.
        await ApplySettingsWithTransitionAsync(next);
    }
}
