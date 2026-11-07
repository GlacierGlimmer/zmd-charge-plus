using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;
using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private bool _workspaceReady, _navigationSync;
    private string? _animatedSection;
    private WorkspacePreferences _workspaceDraft = new();
    private readonly List<(string Id, string Zh, string En, string Icon)> _workspaceSections = new()
    {
        ("display","显示与位置","Display & Position","M2,3H18V14H2Z M7,18H13 M10,14V18"),
        ("hud","方案管理","Profile management","M2,2H8V8H2Z M12,2H18V8H12Z M2,12H8V18H2Z M12,12H18V18H12Z"),
        ("ring","环形切换栏","Radial switcher","M10,2A8,8 0 1 0 10,18A8,8 0 1 0 10,2 M10,6A4,4 0 1 0 10,14A4,4 0 1 0 10,6"),
        ("cycle","自动轮播","Auto Cycle","M3,7A7,7 0 0 1 16,5 M16,1V5H12 M17,13A7,7 0 0 1 4,15 M4,19V15H8"),
        ("rules","触发规则","Trigger rules","M11,1L3,11H9L8,19L17,8H11Z"),
        ("variables","变量库","Variables","M7,2H5V8L3,10L5,12V18H7 M13,2H15V8L17,10L15,12V18H13"),
        ("hardware","硬件数据","Hardware Data","M5,5H15V15H5Z M1,7H5 M1,13H5 M15,7H19 M15,13H19 M7,1V5 M13,1V5 M7,15V19 M13,15V19"),
        ("sources","数据源配置","Data Sources","M2,5A8,3 0 1 0 18,5A8,3 0 1 0 2,5 M2,5V15A8,3 0 0 0 18,15V5 M2,10A8,3 0 0 0 18,10"),
        ("quick","快捷操作","Quick actions","M2,4H18V16H2Z M5,8H7 M9,8H11 M13,8H15 M5,12H15"),
        ("experimental","实验性功能","Experimental Features","M7,2H13 M8,2V8L3,17H17L12,8V2 M6,12H14"),
        ("manual","使用手册","User Manual","M10,4C7,2 3,2 2,3V17C5,16 8,16 10,18C12,16 15,16 18,17V3C15,2 12,2 10,4V18"),
        ("about","关于","About","M10,2A8,8 0 1 0 10,18A8,8 0 1 0 10,2 M10,9V15 M10,5V6")
    };

    private void InitializeWorkspace()
    {
        _workspaceDraft = WorkspacePreferences.Normalize(_rootSettings.Workspace);
        WorkspaceTheme.Apply(this, _workspaceDraft);
        Customizer.WorkspacePreferences = () => _workspaceDraft;
        Customizer.WorkspacePreferencesChanged = value => { _workspaceDraft=WorkspacePreferences.Normalize(value); RefreshQuickActionsLocalization(); };
        Customizer.InitializeRadialEditor();
        Customizer.InitializeWorkflowEditors();
        InitializeQuickHotkeyEditors();
        RefreshQuickActionsLocalization();
        RefreshWorkspaceLocalization();
        WorkspaceNavigation.SelectionChanged += (_, _) => {
            if (!_navigationSync && WorkspaceNavigation.SelectedItem is ListBoxItem { Tag: string id }) NavigateToSettingsSection(id);
        };
        MainSettingsTabs.SelectionChanged += (_, _) => SyncWorkspaceNavigation();
        _workspaceReady = true;
        SyncWorkspaceNavigation();
    }

    private void RefreshQuickActionsLocalization()
    {
        OpenIssueTrackerBtn.Content=WorkspaceStrings.Get("Issue tracker");
    }

    private static TextBlock Label(string text, bool title = false) {
        var label=new TextBlock {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = title ? 16 : 14,
        FontWeight = title ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = new SolidColorBrush(title ? Color.Parse("#F1F1F5") : Color.Parse("#AAAAB6"))
        };
        label.Bind(TextBlock.ForegroundProperty,label.GetResourceObservable(title ? "EcpText" : "EcpMuted"));return label;
    }
    private static Border Card(Control content) {
        var border=new Border {
        Child = content, Padding = new Thickness(20), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#202020")),
        BorderBrush = new SolidColorBrush(Color.Parse("#3B3B3B"))
        };
        border.Bind(Border.BackgroundProperty,border.GetResourceObservable("EcpCard"));border.Bind(Border.BorderBrushProperty,border.GetResourceObservable("EcpBorder"));border.Bind(Border.CornerRadiusProperty,border.GetResourceObservable("EcpRadius"));return border;
    }

    private void RefreshWorkspaceLocalization()
    {
        if (WorkspaceNavigation is null) return;
        string? selected = (WorkspaceNavigation.SelectedItem as ListBoxItem)?.Tag as string;
        _navigationSync = true;
        WorkspaceNavigation.Items.Clear();
        foreach (var section in _workspaceSections)
        {
            string text = WorkspaceStrings.Has(section.En) ? WorkspaceStrings.Get(section.En) : LocalizationManager.Text(section.Zh, section.En);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
            var icon = new Path { Data = Geometry.Parse(section.Icon), StrokeThickness = 1.5, Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            icon.Bind(Shape.StrokeProperty, this.GetResourceObservable("EcpText"));
            row.Children.Add(icon);
            var label = new TextBlock { Text = text, FontSize = 14, FontWeight=FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var item = new ListBoxItem { Tag = section.Id, Content = row }; ToolTip.SetTip(item, text);
            WorkspaceNavigation.Items.Add(item);
            if (section.Id == selected) WorkspaceNavigation.SelectedItem = item;
        }
        SidebarVersion.Text = "v" + GetCurrentVersionText();
        _navigationSync = false;
        SyncWorkspaceNavigation();
    }

    private void SyncWorkspaceNavigation()
    {
        if (_navigationSync) return;
        string id = MainSettingsTabs.SelectedItem switch {
            var p when p == DisplayPage => "display",
            var p when p == HudContentPage => Customizer.WorkspaceSection,
            var p when p == DataManagementPage => DataManagementTabs.SelectedIndex switch { 0 => "variables", 1 => "hardware", _ => "sources" },
            var p when p == QuickActionsPage => "quick",
            var p when p == ExperimentalPage => "experimental", var p when p == ManualPage => "manual", _ => "about"
        };
        _navigationSync = true;
        WorkspaceNavigation.SelectedItem = WorkspaceNavigation.Items.OfType<ListBoxItem>().FirstOrDefault(i => Equals(i.Tag, id));
        var section = _workspaceSections.First(s => s.Id == id);
        WorkspacePageTitle.Text = WorkspaceStrings.Has(section.En) ? WorkspaceStrings.Get(section.En) : LocalizationManager.Text(section.Zh, section.En);
        _navigationSync = false;
        if(_animatedSection!=id) { _animatedSection=id; if(MainSettingsTabs.SelectedContent is Control content) WorkspaceMotion.Reveal(content); }
        Dispatcher.UIThread.Post(() => WorkspaceTheme.ApplyLegacyColours(this), DispatcherPriority.Background);
    }

}
