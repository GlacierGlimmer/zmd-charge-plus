using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace EndfieldChargePlus.Settings;
public sealed class AboutSvg : ContentControl
{
    public static readonly StyledProperty<string> IconProperty=AvaloniaProperty.Register<AboutSvg,string>(nameof(Icon),"globe");
    public string Icon{get=>GetValue(IconProperty);set=>SetValue(IconProperty,value);}
    private readonly Avalonia.Svg.Skia.Svg _svg=new(new Uri("avares://EndfieldChargePlus/")){Stretch=Stretch.Uniform};
    public AboutSvg(){Width=Height=18;VerticalAlignment=VerticalAlignment.Center;Content=_svg;SetPath();}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change){base.OnPropertyChanged(change);if(change.Property==IconProperty)SetPath();}
    private void SetPath(){if(_svg is not null)_svg.Path="avares://EndfieldChargePlus/Assets/About/"+Icon+".svg";}
}

public partial class SettingsWindow
{
    internal static readonly (string Zh,string En,string Icon,string Url)[] AuthorChannels={
        ("作者BiliBili频道","Author's BiliBili channel","bilibili","https://space.bilibili.com/354548348"),
        ("作者YouTube频道","Author's YouTube channel","youtube","https://www.youtube.com/@glacierglimmer925")};
    private readonly List<(TextBlock Text,string Zh,string En)> _aboutLinkLabels=new();
    private void InitializeAboutDetails()
    {
        foreach(var item in AuthorChannels)
        {
            var grid=new Grid{ColumnDefinitions=new("30,*,Auto")};grid.Children.Add(new AboutSvg{Icon=item.Icon,Width=26,Height=26});
            var label=new TextBlock{Text=LocalizationManager.Text(item.Zh,item.En),FontSize=14,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};_aboutLinkLabels.Add((label,item.Zh,item.En));
            var text=new StackPanel{Spacing=5,Margin=new(10,0,14,0),Children={label,new TextBlock{Text=DisplayAddress(item.Url),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#A1A1A1")}}};Grid.SetColumn(text,1);grid.Children.Add(text);
            var open=new TextBlock{Text=LocalizationManager.Text("打开","Open")};_aboutLinkLabels.Add((open,"打开","Open"));
            var button=new Button{Content=open,Tag=item.Url,MinHeight=36,MinWidth=78,HorizontalContentAlignment=HorizontalAlignment.Center};button.Click+=(_,_)=>OpenUrl(item.Url);ToolTip.SetTip(button,DisplayAddress(item.Url));Grid.SetColumn(button,2);grid.Children.Add(button);
            AboutAuthorLinks.Children.Add(new Border{Classes={"ecp-native"},Child=grid,Padding=new(10),Background=Brush.Parse("#171717"),BorderBrush=Brush.Parse("#3B3B3B"),BorderThickness=new(1),CornerRadius=new(4)});
        }
    }
    private void RefreshAboutDetails(){foreach(var (text,zh,en) in _aboutLinkLabels)text.Text=LocalizationManager.Text(zh,en);}
    private static string DisplayAddress(string url)=>url.StartsWith("https://",StringComparison.OrdinalIgnoreCase)?url[8..]:url.StartsWith("http://",StringComparison.OrdinalIgnoreCase)?url[7..]:url;
    private static void SetAboutActionLabel(Button button,string icon,string text)=>button.Content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,Children={new AboutSvg{Icon=icon,Width=16,Height=16},new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center}}};
}
