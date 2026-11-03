using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Settings;
using Path=Avalonia.Controls.Shapes.Path;

namespace EndfieldChargePlus.Customization;
public partial class HudCustomizerView
{
    private List<string>? _radialProfileIds;
    internal Func<WorkspacePreferences>? WorkspacePreferences {get;set;}
    internal Action<WorkspacePreferences>? WorkspacePreferencesChanged {get;set;}
    internal IReadOnlyList<HudProfile> RadialProfiles=>_profiles;
    internal string WorkspaceSection=>CustomizerTabs.SelectedItem==TriggerRulesPage?"rules":CustomizerTabs.SelectedItem==AutomaticCyclePage?"cycle":CustomizerTabs.SelectedItem==RadialSwitcherPage?"ring":"hud";
    internal void SelectWorkspaceSection(string id)
    {
        CustomizerTabs.SelectedItem=id switch{"rules"=>TriggerRulesPage,"cycle"=>AutomaticCyclePage,"ring"=>RadialSwitcherPage,_=>CustomizerTabs.Items[0]};
        if(id=="ring")InitializeRadialEditor();
    }
    internal Control DetachClickAction()
    {
        if(PersistentClickSwitch.Parent is StackPanel panel && panel.Parent is Panel host){host.Children.Remove(panel);return panel;}
        if(PersistentClickSwitch.Parent is Panel parent)parent.Children.Remove(PersistentClickSwitch);
        return new StackPanel{Spacing=10,Children={PersistentClickSwitch}};
    }
    private string? _radialEditingId;
    internal void InitializeRadialEditor()
    {
        if(RadialEditorPanel is null)return;
        RadialEditorPanel.Children.Clear();RadialEditorPanel.Classes.Add("ecp-native");
        TextBlock Text(string value,double size=14)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#F2F2F2")};
        Border Card(Control child)=>new(){Child=child,Background=Brush.Parse("#202020"),BorderBrush=Brush.Parse("#3B3B3B"),BorderThickness=new(1),CornerRadius=new(8),Padding=new(12)};
        Button Action(string text,string hint,System.Action click){var b=new Button{Content=text,Padding=new(10,6),MinHeight=32,HorizontalContentAlignment=HorizontalAlignment.Center};ToolTip.SetTip(b,hint);Avalonia.Automation.AutomationProperties.SetName(b,hint);b.Click+=(_,_)=>click();return b;}
        var preferences=WorkspacePreferences?.Invoke()??new();
        var enable=new ToggleSwitch{Name="RadialEnabledToggle",IsChecked=preferences.RadialEnabled,OnContent="",OffContent=""};
        enable.IsCheckedChanged+=(_,_)=>WorkspacePreferencesChanged?.Invoke((WorkspacePreferences?.Invoke()??new()) with{RadialEnabled=enable.IsChecked==true});
        var intro=new Grid{ColumnDefinitions=new("*,Auto")};intro.Children.Add(new StackPanel{Spacing=6,Children={Text(WorkspaceStrings.Get("Enable radial switching"),16),Text(WorkspaceStrings.Get("Hold the middle mouse button on the HUD, drag to a profile, then release to switch."),12)}});Grid.SetColumn(enable,1);intro.Children.Add(enable);RadialEditorPanel.Children.Add(Card(intro));
        var preview=new ProfileRingControl{Name="RadialPreview",Width=360,Height=360,Profiles=ResolveRadialProfiles(),Preferences=preferences,Interactive=true};
        var selectedList=new ListBox{Name="RadialSelectedProfiles",Background=Brushes.Transparent,BorderThickness=new(0),MaxHeight=420};
        var pool=new StackPanel{Spacing=6};var query=new TextBox{Name="RadialProfileSearch",Watermark="⌕",Margin=new(0,0,0,6)};
        var detail=Text(WorkspaceStrings.Get("Preview only"),14);detail.MinHeight=42;
        var pages=Text("",12);pages.HorizontalAlignment=HorizontalAlignment.Center;
        var all=new CheckBox{Name="RadialAllProfiles",Content=WorkspaceStrings.Get("Use all profiles"),IsChecked=_radialProfileIds is null};
        var orderButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        void ExplicitOrder(){_radialProfileIds??=_profiles.Select(p=>p.Id).ToList();}
        Control Row(HudProfile p,int number){var g=new Grid{ColumnDefinitions=new("24,*,Auto")};g.Children.Add(new Path{Data=IconCatalog.GetGeometry(p.LeftIcon),Stroke=Brush.Parse("#F2F2F2"),StrokeThickness=1.5,Width=18,Height=18,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center});var title=Text(ProfileDisplayName(p),12);title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(title,1);g.Children.Add(title);if(number>0){var n=Text(number.ToString("00"),11);n.Margin=new(5,0,0,0);n.Foreground=Brush.Parse("#A1A1A1");Grid.SetColumn(n,2);g.Children.Add(n);}ToolTip.SetTip(g,ProfileDisplayName(p));return g;}
        bool refreshing=false;
        void PopulatePool(){pool.Children.Clear();foreach(var p in _profiles.Where(p=>ProfileDisplayName(p).Contains(query.Text??"",StringComparison.CurrentCultureIgnoreCase))){bool included=ResolveRadialProfiles().Any(x=>x.Id==p.Id);var button=new Button{Content=Row(p,0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=36,Padding=new(8),Opacity=included?.45:1,IsEnabled=!included};ToolTip.SetTip(button,WorkspaceStrings.Get("Add to ring")+": "+ProfileDisplayName(p));button.Click+=(_,_)=>{ExplicitOrder();_radialProfileIds!.Add(p.Id);_radialEditingId=p.Id;Refresh();};pool.Children.Add(button);}}
        void Refresh(){refreshing=true;var profiles=ResolveRadialProfiles();preview.Profiles=profiles;selectedList.Items.Clear();for(int i=0;i<profiles.Count;i++){var p=profiles[i];var item=new ListBoxItem{Tag=p.Id,Content=Row(p,i+1),Padding=new(7),Margin=new(0,2),HorizontalContentAlignment=HorizontalAlignment.Stretch};selectedList.Items.Add(item);if(p.Id==_radialEditingId)selectedList.SelectedItem=item;}all.IsChecked=_radialProfileIds is null;PopulatePool();preview.Highlight(_radialEditingId);pages.Text=string.Format(WorkspaceStrings.Get("Page {0} / {1}"),preview.Page+1,preview.PageCount);refreshing=false;}
        void Move(int delta){if(_radialEditingId is null)return;ExplicitOrder();int index=_radialProfileIds!.IndexOf(_radialEditingId),target=index+delta;if(index<0||target<0||target>=_radialProfileIds.Count)return;(_radialProfileIds[index],_radialProfileIds[target])=(_radialProfileIds[target],_radialProfileIds[index]);Refresh();}
        orderButtons.Children.Add(Action("↑",LocalizationManager.Text("上移","Up"),()=>Move(-1)));orderButtons.Children.Add(Action("↓",LocalizationManager.Text("下移","Down"),()=>Move(1)));orderButtons.Children.Add(Action("−",WorkspaceStrings.Get("Remove from ring"),()=>{if(_radialEditingId is null)return;ExplicitOrder();_radialProfileIds!.Remove(_radialEditingId);_radialEditingId=null;Refresh();}));
        selectedList.SelectionChanged+=(_,_)=>{if(refreshing)return;_radialEditingId=(selectedList.SelectedItem as ListBoxItem)?.Tag as string;preview.Highlight(_radialEditingId);};
        preview.SelectionChanged+=()=>{var p=_profiles.FirstOrDefault(p=>p.Id==preview.SelectedId);detail.Text=p is null?WorkspaceStrings.Get("Release in the centre or outside the ring to cancel."):ProfileDisplayName(p);pages.Text=string.Format(WorkspaceStrings.Get("Page {0} / {1}"),preview.Page+1,preview.PageCount);};
        preview.ItemPressed+=id=>{_radialEditingId=id;selectedList.SelectedItem=selectedList.Items.OfType<ListBoxItem>().FirstOrDefault(i=>Equals(i.Tag,id));};
        all.IsCheckedChanged+=(_,_)=>{if(refreshing)return;_radialProfileIds=all.IsChecked==true?null:_profiles.Select(p=>p.Id).ToList();Refresh();};
        query.TextChanged+=(_,_)=>PopulatePool();
        var pageActions=new Grid{ColumnDefinitions=new("Auto,*,Auto")};var previous=Action("‹",WorkspaceStrings.Get("Previous page"),()=>preview.ChangePage(-1));var next=Action("›",WorkspaceStrings.Get("Next page"),()=>preview.ChangePage(1));pageActions.Children.Add(previous);Grid.SetColumn(pages,1);pageActions.Children.Add(pages);Grid.SetColumn(next,2);pageActions.Children.Add(next);
        var center=new StackPanel{Spacing=12,Children={new Viewbox{Child=preview,Stretch=Stretch.Uniform,MaxHeight=390},pageActions,detail,Action("▶  "+WorkspaceStrings.Get("Preview ring"),WorkspaceStrings.Get("Preview ring"),preview.PlayOpen)}};
        var selected=new StackPanel{Spacing=8,Children={Text(WorkspaceStrings.Get("Profiles in the ring"),14),selectedList,orderButtons}};
        var available=new StackPanel{Spacing=8,Children={Text(WorkspaceStrings.Get("Add to ring"),14),query,new ScrollViewer{Content=pool,MaxHeight=430}}};
        var layout=new Grid{Name="RadialEditorColumns",ColumnDefinitions=new("170,*,180")};layout.Children.Add(Card(selected));var middle=Card(center);middle.Margin=new(10,0);Grid.SetColumn(middle,1);layout.Children.Add(middle);var right=Card(available);Grid.SetColumn(right,2);layout.Children.Add(right);RadialEditorPanel.Children.Add(layout);
        RadialEditorPanel.Children.Add(all);
        RadialEditorPanel.Children.Add(Text(WorkspaceStrings.Get("Your changes apply after Save & Apply."),12));
        Refresh();
    }
    private IReadOnlyList<HudProfile> ResolveRadialProfiles()=>_radialProfileIds is null?_profiles:_radialProfileIds.Select(id=>_profiles.FirstOrDefault(p=>p.Id==id)).OfType<HudProfile>().ToList();
}
