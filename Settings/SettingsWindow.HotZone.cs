namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private void InitializeHotZoneEditor()
    {
        HotZoneDwellSlider.ValueChanged+=(_,_)=>UpdateHotZoneDwellLabel();
        RebuildHotZoneChoices();
    }
    private void RebuildHotZoneChoices()
    {
        int selected=HotZonePositionCombo.SelectedIndex;HotZonePositionCombo.Items.Clear();
        string[] zh={"左上","顶部居中","右上","左侧居中","居中","右侧居中","左下","底部居中","右下"};
        string[] en={"Top left","Top center","Top right","Center left","Center","Center right","Bottom left","Bottom center","Bottom right"};
        for(int i=0;i<9;i++)HotZonePositionCombo.Items.Add(LocalizationManager.Text(zh[i],en[i]));
        HotZonePositionCombo.SelectedIndex=selected>=0 ? selected : 1;UpdateHotZoneDwellLabel();
    }
    private void LoadHotZoneControls(AppSettings settings)
    {
        HotZoneEnabledCheck.IsChecked=settings.HotZoneEnabled;HotZonePositionCombo.SelectedIndex=Math.Clamp((int)settings.HotZonePosition,0,8);
        HotZoneWidthBox.Value=settings.HotZoneWidth;HotZoneHeightBox.Value=settings.HotZoneHeight;
        HotZoneOffsetXBox.Value=settings.HotZoneOffsetX;HotZoneOffsetYBox.Value=settings.HotZoneOffsetY;
        HotZoneDwellSlider.Value=settings.HotZoneDwellMilliseconds;HotZoneAdvanceCheck.IsChecked=settings.HotZoneAdvanceQueue;
    }
    private void UpdateHotZoneDwellLabel()=>HotZoneDwellText.Text=HotZoneDwellSlider.Value<1
        ? LocalizationManager.Text("立即呼出","Immediate") : $"{HotZoneDwellSlider.Value:0} ms";
}
