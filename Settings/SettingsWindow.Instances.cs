using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Customization;
namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private AppSettings _rootSettings = new();
    private AppSettings _persistedRootSettings = new();
    private string _selectedInstanceId = "";
    private bool _refreshingInstanceChoice, _settingsEditorOpen;
    private ComboBox? _instanceChoice;
    private CheckBox? _multiSwitch;
    private TextBox? _instanceName;
    private TextBlock? _editingLabel;
    private readonly List<(Control Control,string Zh,string En)> _instanceTranslations=new();
    private T InstanceText<T>(T control,string zh,string en) where T : Control
    {
        _instanceTranslations.Add((control,zh,en)); SetInstanceText(control,LocalizationManager.Text(zh,en)); return control;
    }
    private static void SetInstanceText(Control control,string text)
    {
        if(control is TextBlock block) block.Text=text;
        else if(control is TextBox box) box.Watermark=text;
        else if(control is ContentControl content) content.Content=text;
    }
    private void ApplyInstanceEditorLocalization()
    {
        RefreshInstanceEditor();
        _=RefreshEditingHighlightAsync();
        foreach(var (control,zh,en) in _instanceTranslations) SetInstanceText(control,LocalizationManager.Text(zh,en));
        if(_editingLabel is not null) _editingLabel.Text=LocalizationManager.Text("当前编辑：", "Editing: ")+(_rootSettings.HudInstances.FirstOrDefault(i=>i.Id==_selectedInstanceId)?.Name ?? LocalizationManager.Text("主 HUD","Primary HUD"));
        if(HudFontCombo.Items.Count>0) { int font=HudFontCombo.SelectedIndex; HudFontCombo.Items[0]=LocalizationManager.Text("系统默认","System default"); if(font==0) HudFontCombo.SelectedIndex=0; }
    }
    private sealed record InstanceChoice(string Id, string Name) { public override string ToString() => Name; }

    private void InitializeInstanceEditor()
    {
        _multiSwitch = InstanceText(new CheckBox(),"启用多实例 HUD", "Enable multiple HUD instances");
        _instanceChoice = new ComboBox { MinWidth=300 };
        _instanceName = InstanceText(new TextBox(),"实例名称", "Instance name");
        _editingLabel = new TextBlock { Foreground=Brushes.Orange, TextWrapping=TextWrapping.Wrap };
        var editButton = InstanceText(new Button(),"定位并编辑所选 HUD", "Locate and edit selected HUD");
        var add = InstanceText(new Button { Name="AddHudInstanceButton" },"新增 HUD", "Add HUD");
        var remove = InstanceText(new Button(),"删除所选实例", "Remove selected instance");
        var buttons = new StackPanel { Orientation=Orientation.Horizontal, Spacing=10 };
        buttons.Children.Add(add); buttons.Children.Add(remove); buttons.Children.Add(editButton);
        ExperimentalPanel.Children.Add(InstanceText(new TextBlock { FontSize=18 },"多实例 HUD", "Multiple HUD instances"));
        ExperimentalPanel.Children.Add(InstanceText(new TextBlock { TextWrapping=TextWrapping.Wrap },
            "各 HUD 可独立设置方案、轮播、字体、大小、常驻模式和目标显示器。选择实例后即可到前两个选项卡调整；定位按钮可跳转到位置设置。设置窗口打开期间，橙色框和“当前正在设置”标记持续指示当前实例。新增或删除后保存并应用；关闭设置窗口后取消高亮。采样间隔和点击切换开关对全部 HUD 生效，可在编辑任意实例时修改；语言和开机启动统一管理。最多 16 个 HUD。",
            "Each HUD has independent profiles, cycling, font, size, visibility and target display. Select an instance and adjust it in the first two tabs; Locate jumps to position settings. An orange outline and Currently editing label remain on the selected HUD while Settings is open. Save & Apply after adding or removing an instance; closing Settings clears the outline. Sampling interval and click switching are shared by all HUDs and can be changed while editing any instance; language and startup are global. Up to 16 HUDs."));
        ExperimentalPanel.Children.Add(_multiSwitch); ExperimentalPanel.Children.Add(_instanceChoice);
        ExperimentalPanel.Children.Add(_instanceName); ExperimentalPanel.Children.Add(buttons); ExperimentalPanel.Children.Add(_editingLabel);
        add.Click += (_,_) =>
        {
            _rootSettings=CollectSettingsFromUi();
            if (_rootSettings.HudInstances.Count>=15) return;
            var local=HudInstanceSettings.ForNewInstance(CollectEditorSettings(),
                new[] { _rootSettings }.Concat(_rootSettings.HudInstances.Where(i=>i.Enabled).Select(i=>i.Settings)));
            var instance=new HudInstanceSettings { Name=$"HUD {_rootSettings.HudInstances.Count+2}", Settings=local };
            _rootSettings=_rootSettings with { MultiHudEnabled=true, HudInstances=_rootSettings.HudInstances.Append(instance).ToList() };
            _selectedInstanceId=instance.Id; ApplyEditorControls(local); RefreshInstanceEditor();
        };
        remove.Click += (_,_) =>
        {
            string id=(_instanceChoice.SelectedItem as InstanceChoice)?.Id ?? "";
            if (string.IsNullOrEmpty(id)) return;
            _rootSettings=CollectSettingsFromUi();
            _rootSettings=_rootSettings with { HudInstances=_rootSettings.HudInstances.Where(i => i.Id!=id).ToList() };
            if (_selectedInstanceId==id) { _selectedInstanceId=""; ApplyEditorControls(_rootSettings); }
            RefreshInstanceEditor(); _=RefreshEditingHighlightAsync();
        };
        editButton.Click += async (_,_) =>
        {
            if (!editButton.IsEnabled) return;
            editButton.IsEnabled=false;
            try
            {
            _rootSettings=CollectSettingsFromUi();
            if (_instanceChoice.SelectedItem is not InstanceChoice selected) return;
            _selectedInstanceId=selected.Id;
            var settings=string.IsNullOrEmpty(selected.Id) ? _rootSettings : _rootSettings.HudInstances.FirstOrDefault(i => i.Id==selected.Id)?.Settings ?? _rootSettings;
            ApplyEditorControls(settings); RefreshInstanceEditor();
            if (!await _runtime.HighlightInstanceAsync(selected.Id,selected.Name))
            { _editingLabel.Text=LocalizationManager.Text("请先保存并应用以显示新增 HUD，再点击定位。", "Save & Apply to show a new HUD before locating it."); return; }
            if (this.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault() is { } tabs) tabs.SelectedIndex=0;
            }
            catch (Exception ex) { _editingLabel.Text=LocalizationManager.Text("无法定位 HUD：","Unable to locate HUD: ")+ex.Message; }
            finally { editButton.IsEnabled=true; }
        };
        _multiSwitch.IsCheckedChanged+=(_,_)=>UpdateModeUi();
        _instanceChoice.SelectionChanged += (_,_) =>
        {
            if(_refreshingInstanceChoice || _instanceChoice.SelectedItem is not InstanceChoice choice || choice.Id==_selectedInstanceId) return;
            string requested=choice.Id;
            // Let Avalonia finish its selection transaction before loading/replacing editor lists.
            Avalonia.Threading.Dispatcher.UIThread.Post(async ()=> {
                if(_refreshingInstanceChoice || _instanceChoice.SelectedItem is not InstanceChoice selected || selected.Id!=requested) return;
                try {
                    _rootSettings=CollectSettingsFromUi();_selectedInstanceId=requested;
                    var local=string.IsNullOrEmpty(requested) ? _rootSettings : _rootSettings.HudInstances.FirstOrDefault(i=>i.Id==requested)?.Settings ?? _rootSettings;
                    ApplyEditorControls(local);RefreshInstanceEditor();await RefreshEditingHighlightAsync();
                } catch(Exception ex) { EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to select HUD instance.",ex); }
            });
        };
        Opened += async (_,_) => { _settingsEditorOpen=true; await RefreshEditingHighlightAsync(); };
        Closed += (_,_) => { _settingsEditorOpen=false; ProfileDataWarnings.Forget("preview");_runtime.EndInstanceEditing(); };
        RefreshInstanceEditor();
    }

    private async Task RefreshEditingHighlightAsync()
    {
        if (!_settingsEditorOpen) return;
        try {
            string name=_rootSettings.HudInstances.FirstOrDefault(i=>i.Id==_selectedInstanceId)?.Name ?? LocalizationManager.Text("主 HUD","Primary HUD");
            await _runtime.HighlightInstanceAsync(_selectedInstanceId,name);
        } catch(Exception ex) { EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to highlight the edited HUD.",ex); }
    }
    private void RefreshInstanceEditor()
    {
        if (_instanceChoice is null || _multiSwitch is null || _instanceName is null) return;
        _refreshingInstanceChoice=true;
        _multiSwitch.IsChecked=_rootSettings.MultiHudEnabled;
        var choices=new[] { new InstanceChoice("",LocalizationManager.Text("主 HUD","Primary HUD")) }
            .Concat(_rootSettings.HudInstances.Select(i=>new InstanceChoice(i.Id,i.Name))).ToArray();
        _instanceChoice.ItemsSource=choices;
        _instanceChoice.SelectedItem=choices.FirstOrDefault(i=>i.Id==_selectedInstanceId);
        _instanceName.IsEnabled=!string.IsNullOrEmpty(_selectedInstanceId);
        _instanceName.Text=_rootSettings.HudInstances.FirstOrDefault(i => i.Id==_selectedInstanceId)?.Name ?? "";
        if (_editingLabel is not null) _editingLabel.Text=LocalizationManager.Text("当前编辑：", "Editing: ")+_instanceChoice.SelectedItem;
        _refreshingInstanceChoice=false;
        SamplingIntervalBox.IsEnabled=true;
        StartupSwitch.IsEnabled=string.IsNullOrEmpty(_selectedInstanceId);
        Customizer.SetPreviewHud(_runtime.GetInstanceHud(_selectedInstanceId));
        UpdateModeUi();
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        HudHotkeyBox.Text=settings.HudToggleHotkey;
        HudHotkeyAnimationCombo.SelectedIndex=settings.HudHotkeyShowAnimation=="Simple" ? 0 : 1;
        _rootSettings=settings; _persistedRootSettings=settings; _selectedInstanceId=""; ApplyEditorControls(settings); RefreshInstanceEditor(); _=RefreshEditingHighlightAsync();
        GlobalAppearance.Apply(this,settings.GlobalFontScale,settings.GlobalUiScale);
        RefreshHudHotkeyRegistrationStatus();
    }

    private AppSettings CollectSettingsFromUi()
    {
        var editor=CollectEditorSettings() with { MultiHudEnabled=false, HudInstances=new() };
        bool multi=_multiSwitch?.IsChecked ?? _rootSettings.MultiHudEnabled;
        if (string.IsNullOrEmpty(_selectedInstanceId))
            _rootSettings=editor with { MultiHudEnabled=multi, HudInstances=_rootSettings.HudInstances };
        else
            _rootSettings=_rootSettings with { MultiHudEnabled=multi,
                HudInstances=_rootSettings.HudInstances.Select(i => i.Id==_selectedInstanceId ? i with {
                    Settings=editor, Name=string.IsNullOrWhiteSpace(_instanceName?.Text) ? i.Name : _instanceName.Text.Trim() } : i).ToList() };
        _rootSettings=_rootSettings with { HudToggleHotkey=HudHotkeyBox.Text?.Trim() ?? "",
            HudHotkeyShowAnimation=HudHotkeyAnimationCombo.SelectedIndex==0 ? "Simple" : "Full",
            SamplingIntervalSeconds=editor.SamplingIntervalSeconds,ClickToCycle=editor.ClickToCycle,UiLanguage=editor.UiLanguage,
            HudInstances=_rootSettings.HudInstances.Select(i=>i with { Settings=i.Settings with {
                SamplingIntervalSeconds=editor.SamplingIntervalSeconds,ClickToCycle=editor.ClickToCycle,
                GlobalFontScale=_rootSettings.GlobalFontScale,GlobalUiScale=_rootSettings.GlobalUiScale } }).ToList() };
        return _rootSettings;
    }
}
