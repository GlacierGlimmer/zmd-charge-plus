using Avalonia.Controls;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private void InitializeDataManagement()
    {
        var pages=Customizer.DetachDataPages();
        DataManagementTabs.Items.Insert(0,pages.Variables);DataManagementTabs.Items.Add(pages.Sources);
        DataManagementTabs.SelectedIndex=0;
        Customizer.DiscoverHardware=DiscoverDataVariablesAsync;
        DataManagementPage.PropertyChanged+=async(_,e)=>{
            if(e.Property==TabItem.IsSelectedProperty && DataManagementPage.IsSelected)await DiscoverDataVariablesAsync();
        };
    }
    private async Task DiscoverDataVariablesAsync()
    {
        try {await _runtime.SnapshotHardwareAsync(_hardwareLifetime.Token);if(!_hardwareLifetime.IsCancellationRequested)Customizer.RefreshHardwareVariables();}
        catch(OperationCanceledException){}
        catch(Exception ex){Diagnostics.AppLog.Error("Hardware variable discovery failed.",ex);}
    }
    internal void NavigateToSettingsSection(string target)
    {
        switch(target)
        {
            case "display":MainSettingsTabs.SelectedItem=DisplayPage;break;
            case "hud": case "rules": case "ring": case "cycle":
                MainSettingsTabs.SelectedItem=HudContentPage;Customizer.SelectWorkspaceSection(target);break;
            case "quick":MainSettingsTabs.SelectedItem=QuickActionsPage;break;
            case "variables":MainSettingsTabs.SelectedItem=DataManagementPage;DataManagementTabs.SelectedIndex=0;break;
            case "hardware":MainSettingsTabs.SelectedItem=DataManagementPage;DataManagementTabs.SelectedItem=HardwarePage;break;
            case "sources":MainSettingsTabs.SelectedItem=DataManagementPage;DataManagementTabs.SelectedIndex=2;break;
            case "experimental":MainSettingsTabs.SelectedItem=ExperimentalPage;break;
            case "about":MainSettingsTabs.SelectedItem=AboutPage;break;
            case "manual":MainSettingsTabs.SelectedItem=ManualPage;break;
        }
        if (_workspaceReady) SyncWorkspaceNavigation();
    }
}
