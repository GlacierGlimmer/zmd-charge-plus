using Avalonia.Controls;
using Avalonia.Media;
namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private CheckBox _transientHoverHold=null!;
    private void InitializeHotZoneEditor()
    {
        _transientHoverHold=new CheckBox{Content=new TextBlock{Text=LocalizationManager.Text("鼠标悬停在 HUD 或热区时保持显示","Keep HUD visible while hovering over it or the hot zone"),TextWrapping=TextWrapping.Wrap}};
        if(HotZonePanel.Parent is Panel host)host.Children.Insert(host.Children.IndexOf(HotZonePanel),Card(_transientHoverHold));
        AlwaysVisibleSwitch.IsCheckedChanged+=(_,_)=>_transientHoverHold.IsEnabled=AlwaysVisibleSwitch.IsChecked!=true;
        HotZoneDwellSlider.ValueChanged+=(_,_)=>UpdateHotZoneDwellLabel();
        RebuildHotZoneChoices();
    }
    private void RebuildHotZoneChoices()
    {
        if(_transientHoverHold?.Content is TextBlock hover)hover.Text=LocalizationManager.Text("鼠标悬停在 HUD 或热区时保持显示","Keep HUD visible while hovering over it or the hot zone");
        int selected=HotZonePositionCombo.SelectedIndex;HotZonePositionCombo.Items.Clear();
        string[] zh={"左上","顶部居中","右上","左侧居中","居中","右侧居中","左下","底部居中","右下"};
        string[] en={"Top left","Top center","Top right","Center left","Center","Center right","Bottom left","Bottom center","Bottom right"};
        for(int i=0;i<9;i++)HotZonePositionCombo.Items.Add(LocalizationManager.Text(zh[i],en[i]));
        HotZonePositionCombo.SelectedIndex=selected>=0 ? selected : 1;UpdateHotZoneDwellLabel();
    }
    private void LoadHotZoneControls(AppSettings settings)
    {
        _transientHoverHold.IsChecked=settings.TransientHoverHold;
        _transientHoverHold.IsEnabled=!settings.AlwaysVisible;
        HotZoneEnabledCheck.IsChecked=settings.HotZoneEnabled;HotZonePositionCombo.SelectedIndex=Math.Clamp((int)settings.HotZonePosition,0,8);
        HotZoneWidthBox.Value=settings.HotZoneWidth;HotZoneHeightBox.Value=settings.HotZoneHeight;
        HotZoneOffsetXBox.Value=settings.HotZoneOffsetX;HotZoneOffsetYBox.Value=settings.HotZoneOffsetY;
        HotZoneDwellSlider.Value=settings.HotZoneDwellMilliseconds;HotZoneAdvanceCheck.IsChecked=settings.HotZoneAdvanceQueue;
    }
    private void UpdateHotZoneDwellLabel()=>HotZoneDwellText.Text=HotZoneDwellSlider.Value<1
        ? LocalizationManager.Text("立即呼出","Immediate") : $"{HotZoneDwellSlider.Value:0} ms";
}
