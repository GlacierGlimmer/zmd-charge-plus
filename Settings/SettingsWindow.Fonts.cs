using Avalonia.Controls;
using Avalonia.Media;

namespace EndfieldChargePlus.Settings;
public partial class SettingsWindow
{
    private bool _systemFontChoicesLoaded;
    private void InitializeFontChoices()
    {
        HudFontCombo.Items.Add(LocalizationManager.Text("系统默认","System default"));
        HudFontCombo.PropertyChanged+=(_,e)=>
        {
            if(e.Property==ComboBox.IsDropDownOpenProperty && HudFontCombo.IsDropDownOpen)LoadSystemFontChoices();
        };
    }
    private void LoadSystemFontChoices()
    {
        if(_systemFontChoicesLoaded)return;
        _systemFontChoicesLoaded=true;
        string selected=HudFontCombo.SelectedIndex>0?HudFontCombo.SelectedItem?.ToString()??"":"";
        foreach(var family in FontManager.Current.SystemFonts.OrderBy(f=>f.Name))
            if(!HudFontCombo.Items.Contains(family.Name))HudFontCombo.Items.Add(family.Name);
        if(!string.IsNullOrEmpty(selected))HudFontCombo.SelectedItem=selected;
    }
}
