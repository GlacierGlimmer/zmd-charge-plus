using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    internal static readonly (string Platform, string Repository)[] PlatformRepositories =
    {
        ("Windows", "zmd-charge-plus"), ("Linux", "zmd-charge-plus-for-linux"),
        ("macOS", "zmd-charge-plus-for-macos"), ("Android", "zmd-charge-plus-for-android")
    };

    private void InitializeRepositoryLinks()
    {
        foreach (var (platform, repository) in PlatformRepositories)
        {
            RepositoryRows.Children.Add(CreateRepositoryRow(platform,repository,false));
            AboutRepositoryRows.Children.Add(CreateRepositoryRow(platform,repository,true));
        }
        OpenPlatformRepositoriesBtn.Click += (_,_) => ShowRepositoryLinks();
        RepositoryCloseButton.Click += (_,_) => HideRepositoryLinks();
        KeyboardNavigation.SetTabNavigation(RepositoriesOverlay,KeyboardNavigationMode.Cycle);
        RepositoriesOverlay.KeyDown += (_,e) => { if(e.Key==Key.Escape) {HideRepositoryLinks();e.Handled=true;} };
        RefreshRepositoryLinks();
    }

    private static Border CreateRepositoryRow(string platform,string repository,bool showAddress)
    {
        var row=new Grid{ColumnDefinitions=new("30,*,Auto,Auto")};
        row.Children.Add(CreatePlatformLogo(platform));
        var label=new TextBlock{Text=platform+":",FontSize=14,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0)};
        if(showAddress)
        {
            label.Margin=new(0);
            var names=new StackPanel{Spacing=4,Margin=new(10,0,12,0),Children={label,new TextBlock{Text="github.com/GlacierGlimmer/"+repository,FontSize=12,Foreground=Brush.Parse("#A1A1A1"),TextWrapping=TextWrapping.Wrap}}};
            Grid.SetColumn(names,1);row.Children.Add(names);
        }
        else{Grid.SetColumn(label,1);row.Children.Add(label);}
        for(int i=0;i<2;i++)
        {
            string url="https://github.com/GlacierGlimmer/"+repository+(i==1?"/releases":"");
            var button=new Button{Content=i==0?"GitHub":"Releases",Tag=url,MinHeight=36,MinWidth=78,Padding=new(10,7),Margin=new(8,0,0,0),HorizontalContentAlignment=HorizontalAlignment.Center};
            if(i==0){button.Background=Brush.Parse("#FFFA00");button.Foreground=Brush.Parse("#141414");}
            AutomationProperties.SetName(button,platform+" "+button.Content);ToolTip.SetTip(button,DisplayAddress(url));button.Click+=(_,_)=>OpenUrl(url);
            Grid.SetColumn(button,i+2);row.Children.Add(button);
        }
        return new Border{Child=row,Classes={"ecp-native"},Background=Brush.Parse("#171717"),BorderBrush=Brush.Parse("#3B3B3B"),BorderThickness=new(1),CornerRadius=new(4),Padding=new(8)};
    }

    private void RefreshRepositoryLinks()
    {
        RefreshAboutDetails();
        SetAboutActionLabel(CheckUpdateBtn,"refresh-cw",LocalizationManager.Text("检查更新","Check Updates"));
        string close=LocalizationManager.Text("关闭窗口","Close window");
        ToolTip.SetTip(RepositoryCloseButton,close);AutomationProperties.SetName(RepositoryCloseButton,close);
    }

    private void ShowRepositoryLinks()
    {
        var scales=GlobalAppearance.CurrentScales;
        // The dialog stays centred and its close control remains reachable at the supported limits.
        RepositoryScaleHost.LayoutTransform=new ScaleTransform(scales.Ui,scales.Ui);
        foreach(var row in RepositoryRows.Children.OfType<Border>())
            foreach(var text in ((Grid)row.Child!).Children.OfType<TextBlock>())text.FontSize=14*scales.Font;
        SettingsBodyHost.IsEnabled=false;SettingsTitleBar.IsEnabled=false;
        RepositoriesOverlay.IsVisible=true;WorkspaceMotion.Reveal(RepositoryDialog);RepositoryCloseButton.Focus();
    }

    private void HideRepositoryLinks()
    {
        RepositoriesOverlay.IsVisible=false;SettingsBodyHost.IsEnabled=true;SettingsTitleBar.IsEnabled=true;
        OpenPlatformRepositoriesBtn.Focus();
    }

    private static Control CreatePlatformLogo(string platform)
    {
        var icon=new Avalonia.Svg.Skia.Svg(new Uri("avares://EndfieldChargePlus/")) {
            Path="avares://EndfieldChargePlus/Assets/Platforms/"+platform.ToLowerInvariant()+".svg",
            Width=26, Height=26, Stretch=Stretch.Uniform, VerticalAlignment=VerticalAlignment.Center };
        AutomationProperties.SetName(icon,platform);return icon;
    }
}
