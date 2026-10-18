using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace EndfieldChargePlus.Settings;

/// <summary>Native names stay readable independently of the active interface language.</summary>
internal sealed class LanguageSelectionWindow : Window
{
    private sealed record Choice(string Code,string Label,string Search,bool RightToLeft);
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _choose;
    private readonly Func<double,double,Task> _scale;
    private readonly TextBox _search=new() {Name="LanguageSearch",MinHeight=38,FlowDirection=FlowDirection.LeftToRight};
    private readonly ListBox _languages=new() {Name="LanguageList"};
    private readonly TextBlock _heading=new() {FontSize=22,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly TextBlock _translationNotice=new() {Name="LanguageTranslationNotice",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch,Foreground=Brush.Parse("#A4A4AA"),Margin=new Thickness(0,14,0,0)};
    private readonly Button _appearance=new() {Name="GlobalAppearanceButton",MinHeight=42,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};
    private readonly Button _done=new() {Name="LanguageDoneButton",MinHeight=42,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};
    private Choice[] _choices=Array.Empty<Choice>();
    private bool _refreshing;

    public LanguageSelectionWindow(Func<AppSettings> settings,Action<string> choose,Func<double,double,Task> scale)
    {
        _settings=settings;_choose=choose;_scale=scale;
        Width=900;Height=720;MinWidth=580;MinHeight=440;Background=Brush.Parse("#19191B");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid {RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto,Auto"),Margin=new Thickness(28,20),MinWidth=520,MinHeight=380};
        root.Children.Add(_heading);
        _search.MaxWidth=660;_search.HorizontalAlignment=HorizontalAlignment.Stretch;_search.Margin=new Thickness(0,18,0,14);Grid.SetRow(_search,1);root.Children.Add(_search);
        var frame=new Border {MaxWidth=560,HorizontalAlignment=HorizontalAlignment.Stretch,BorderBrush=Brush.Parse("#454549"),BorderThickness=new Thickness(1),Child=_languages};Grid.SetRow(frame,2);root.Children.Add(frame);
        _languages.ItemTemplate=new FuncDataTemplate<Choice>((c,_)=>c is null ? new TextBlock() : new TextBlock {
            Text=c.Label,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,5),FontSize=16,
            FlowDirection=c.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight });
        _languages.Styles.Add(new Avalonia.Styling.Style(s=>s.OfType<ListBoxItem>()) {Setters={
            new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch),
            new Setter(ListBoxItem.VerticalContentAlignmentProperty,VerticalAlignment.Center),
            new Setter(ListBoxItem.MinHeightProperty,36d),
            new Setter(ListBoxItem.PaddingProperty,new Thickness(0)),
            new Setter(ListBoxItem.MarginProperty,new Thickness(0)),
            new Setter(ListBoxItem.BorderThicknessProperty,new Thickness(1)),
            new Setter(ListBoxItem.BorderBrushProperty,Brushes.Transparent)}});
        _languages.Styles.Add(new Avalonia.Styling.Style(s=>s.OfType<ListBoxItem>().Class(":selected")) {Setters={
            new Setter(ListBoxItem.BorderBrushProperty,Brushes.White),
            new Setter(ListBoxItem.BackgroundProperty,Brushes.Transparent)}});
        var footer=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),Margin=new Thickness(0,18,0,0)};
        _appearance.Margin=new Thickness(0,0,7,0);_done.Margin=new Thickness(7,0,0,0);
        Grid.SetRow(_translationNotice,3);root.Children.Add(_translationNotice);
        footer.Children.Add(_appearance);Grid.SetColumn(_done,1);footer.Children.Add(_done);Grid.SetRow(footer,4);root.Children.Add(footer);Content=root;
        _search.TextChanged+=(_,_)=>Filter();
        _languages.SelectionChanged+=(_,_)=>{
            if(_refreshing || _languages.SelectedItem is not Choice item)return;
            _choose(item.Code);Refresh();
        };
        _done.Click+=(_,_)=>Close();
        _appearance.Click+=async (_,_)=>await OpenAppearanceAsync();
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        Refresh();
    }
    private static string T(string zh,string en)=>LocalizationManager.Text(zh,en);
    private void Refresh()
    {
        Title=_heading.Text="Language / "+LocalizationManager.CurrentLanguage.LanguageWord;
        _search.Watermark=T("搜索语言、地区或语言代码","Search languages, regions or language codes");
        AutomationProperties.SetName(_search,_search.Watermark?.ToString());
        _appearance.Content=new TextBlock {Text=T("全局字体大小和UI缩放","Global font size and UI scale"),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
        _done.Content=T("完成","Done");
        _translationNotice.Text=T("（语言翻译不一定100%准确）","(Language translations may not be 100% accurate)");
        _translationNotice.FlowDirection=LocalizationManager.CurrentLanguage.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _choices=new[]{new Choice("Auto",T("跟随系统","Follow system"),"Auto System 跟随系统",false)}.Concat(
            LocalizationManager.SupportedLanguages.Select(l=>new Choice(l.Code,l.DisplayName,$"{l.DisplayName} {l.Code} {l.EnglishName} {l.MinecraftCode}",l.RightToLeft))).ToArray();
        Filter();
        GlobalAppearance.Apply(this,_settings().GlobalFontScale,_settings().GlobalUiScale);
    }
    private void Filter()
    {
        _refreshing=true;
        try
        {
            string query=_search.Text?.Trim() ?? "";
            var matches=_choices.Where(c=>query.Length==0 || c.Search.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
            _languages.ItemsSource=matches;
            _languages.SelectedItem=matches.FirstOrDefault(c=>c.Code==LocalizationManager.Preference);
            if(_languages.SelectedItem is {} selected)_languages.ScrollIntoView(selected);
        }
        finally {_refreshing=false;}
    }
    private async Task OpenAppearanceAsync()
    {
        var settings=_settings();
        var font=new NumericUpDown {Name="GlobalFontScaleBox",Minimum=75,Maximum=200,Increment=5,Value=(decimal)(100*GlobalAppearance.NormalizeScale(settings.GlobalFontScale)),FormatString="0'%'"};
        var ui=new NumericUpDown {Name="GlobalUiScaleBox",Minimum=75,Maximum=200,Increment=5,Value=(decimal)(100*GlobalAppearance.NormalizeScale(settings.GlobalUiScale)),FormatString="0'%'"};
        var panel=new StackPanel {Spacing=14,Margin=new Thickness(24),MinWidth=400};
        panel.Children.Add(new TextBlock {Text=T("全局字体大小和UI缩放","Global font size and UI scale"),FontSize=20,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text=T("字体缩放","Font scale")});panel.Children.Add(font);
        panel.Children.Add(new TextBlock {Text=T("UI 缩放","UI scale")});panel.Children.Add(ui);
        panel.Children.Add(new TextBlock {Text=T("应用于整个设置界面和所有 HUD。单个 HUD 的大小设置会与全局 UI 缩放相乘。","Applies to all settings and every HUD. Individual HUD size is multiplied by global UI scale."),TextWrapping=TextWrapping.Wrap});
        var apply=new Button {Name="ApplyGlobalAppearanceButton",Content=T("保存并应用","Save & Apply"),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,MinHeight=40};
        var reset=new Button {Content=T("恢复 100%","Reset to 100%"),HorizontalAlignment=HorizontalAlignment.Stretch};
        panel.Children.Add(reset);panel.Children.Add(apply);
        var dialog=new Window {Title=T("全局字体大小和UI缩放","Global font size and UI scale"),Content=panel,Width=610,Height=470,MinWidth=460,MinHeight=370,Background=Background,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        reset.Click+=(_,_)=>{font.Value=100;ui.Value=100;};
        apply.Click+=async (_,_)=>{
            apply.IsEnabled=false;
            try{await _scale((double)(font.Value ?? 100)/100,(double)(ui.Value ?? 100)/100);dialog.Close();Refresh();}
            catch(Exception ex){Diagnostics.AppLog.Error("Unable to save global appearance.",ex);apply.IsEnabled=true;}
        };
        GlobalAppearance.Apply(dialog,settings.GlobalFontScale,settings.GlobalUiScale);
        await dialog.ShowDialog(this);
    }
}
