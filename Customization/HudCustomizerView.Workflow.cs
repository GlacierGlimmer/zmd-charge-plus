using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Customization;

public partial class HudCustomizerView
{
    private ListBox? _workflowProfiles;
    private TextBox? _workflowSearch;
    private bool _workflowSync;
    private CheckBox? _cycleClickSwitch;
    private Action? _refreshCycleWorkspace;
    private readonly List<(TextBlock Text,string Zh,string En)> _workflowText=new();
    private readonly List<(Expander Section,string Zh,string En)> _workflowHeaders=new();
    private TextBlock WorkflowText(string zh,string en,double size=14)
    {
        var text=new TextBlock{Text=LocalizationManager.Text(zh,en),FontSize=size,TextWrapping=TextWrapping.Wrap};
        _workflowText.Add((text,zh,en));return text;
    }
    private static Border WorkflowCard(Control child)=>new(){Child=child,Background=Brush.Parse("#202020"),BorderBrush=Brush.Parse("#3B3B3B"),BorderThickness=new(1),CornerRadius=new(8),Padding=new(14)};
    private static void RemoveFromParent(Control control)
    {
        if(control.Parent is Panel panel)panel.Children.Remove(control);
        else if(control.Parent is Decorator decorator)decorator.Child=null;
        else if(control.Parent is ContentControl content)content.Content=null;
    }
    internal void InitializeWorkflowEditors()
    {
        if(_workflowProfiles is not null)return;
        var page=(TabItem)CustomizerTabs.Items[0]!;
        var oldScroll=(ScrollViewer)page.Content!;var oldBody=(StackPanel)oldScroll.Content!;
        var cards=oldBody.Children.ToArray();oldBody.Children.Clear();oldScroll.Content=null;
        var manager=(Border)cards[0];var managerBody=(StackPanel)manager.Child!;
        // The selector becomes an icon list. Keep the original model-backed
        // ComboBox as the single selection path for imports, saves and languages.
        ProfileCombo.IsVisible=false;
        var comboRow=(Grid)ProfileCombo.Parent!.Parent!;comboRow.IsVisible=false;
        foreach(var button in new[]{AddProfileBtn,SaveProfileBtn,DeleteProfileBtn,PreviewBtn})RemoveFromParent(button);
        var toolbar=new Grid{ColumnDefinitions=new("*,Auto,Auto")};
        toolbar.Children.Add(WorkflowText("所选方案","Selected profile",16));
        Grid.SetColumn(SaveProfileBtn,1);toolbar.Children.Add(SaveProfileBtn);PreviewBtn.Margin=new(8,0,0,0);Grid.SetColumn(PreviewBtn,2);toolbar.Children.Add(PreviewBtn);
        managerBody.Children.RemoveAt(0);managerBody.Children.Insert(0,toolbar);
        var editor=new StackPanel{Spacing=12,Children={manager}};
        for(int i=1;i<cards.Length;i++)
        {
            if(cards[i] is Border {Child:StackPanel section} && section.Children.Count>0)
            {
                if(section.Children[0] is StackPanel heading && heading.Children.Count>0)heading.Children.RemoveAt(0);
                else if(section.Children[0] is TextBlock)section.Children.RemoveAt(0);
            }
            // Existing template/variable/icon/colour controls keep their handlers.
            // Advanced sections can be folded without losing any editor state.
            var title=i switch{1=>("显示内容","Display Content"),2=>("图标与进度环","Icons & Progress Ring"),_=>("颜色","Colors")};
            var expander=new Expander{Header=LocalizationManager.Text(title.Item1,title.Item2),Content=cards[i],IsExpanded=i==1,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            _workflowHeaders.Add((expander,title.Item1,title.Item2));editor.Children.Add(expander);
        }
        foreach(var grid in editor.GetLogicalDescendants().OfType<Grid>())
            if(grid.ColumnDefinitions.Any(c=>c.Width.IsAbsolute && c.Width.Value>=160))
                grid.ColumnDefinitions=new ColumnDefinitions(string.Join(",",grid.ColumnDefinitions.Select(c=>c.Width.IsAbsolute && c.Width.Value>=160?"*":c.Width.ToString())));
        _workflowSearch=new TextBox{Name="ProfileWorkspaceSearch",Watermark=LocalizationManager.Text("搜索方案","Search profiles")};
        _workflowProfiles=new ListBox{Name="ProfileWorkspaceList",Background=Brushes.Transparent,BorderThickness=new(0),MinHeight=280};
        _workflowProfiles.Classes.Add("workflow-list");
        // The list has already scrolled its selected row. Keep programmatic
        // refreshes from moving the surrounding scaled settings viewport.
        _workflowProfiles.AddHandler(Control.RequestBringIntoViewEvent,(_,e)=>
        {
            if(!_workflowProfiles.IsKeyboardFocusWithin)e.Handled=true;
        },Avalonia.Interactivity.RoutingStrategies.Bubble);
        _workflowProfiles.SelectionChanged+=(_,_)=>{if(!_workflowSync && _workflowProfiles.SelectedItem is ListBoxItem {Tag:string id})ProfileCombo.SelectedIndex=_profiles.FindIndex(p=>p.Id==id);};
        _workflowSearch.TextChanged+=(_,_)=>RefreshWorkflowProfiles();
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,Children={AddProfileBtn,DeleteProfileBtn}};AddProfileBtn.Margin=new(0,0,6,6);
        var library=new Grid{RowDefinitions=new("Auto,Auto,*,Auto")};library.Children.Add(WorkflowText("方案管理","Profile management",16));_workflowSearch.Margin=new(0,12,0,10);Grid.SetRow(_workflowSearch,1);library.Children.Add(_workflowSearch);Grid.SetRow(_workflowProfiles,2);library.Children.Add(_workflowProfiles);Grid.SetRow(actions,3);library.Children.Add(actions);
        var layout=new Grid{Name="ProfileWorkspaceColumns",ColumnDefinitions=new("220,*"),Margin=new(4,12,4,4)};var left=WorkflowCard(library);left.Margin=new(0,0,10,0);layout.Children.Add(left);
        var editorScroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};Grid.SetColumn(editorScroll,1);layout.Children.Add(editorScroll);page.Content=layout;
        // Selection changes must keep the action bar in place instead of scrolling
        // a newly measured lower editor field into view.
        editor.AddHandler(Control.RequestBringIntoViewEvent,(_,e)=>{if(e.Source is not Control {IsKeyboardFocusWithin:true})e.Handled=true;});
        PersistentClickSwitch.Content=WorkflowText("点击或滚轮切换方案","Click or scroll to switch profiles");
        if(PersistentClickSwitch.Parent is StackPanel clickPanel)clickPanel.Children.Add(WorkflowText("向上滚动切换上一项，向下滚动切换下一项。","Scroll up for previous; scroll down for next.",12));
        InitializeCycleWorkspace();RefreshWorkflowProfiles();
    }
    internal bool SelectRuntimeProfile(string id)
    {
        int index=_profiles.FindIndex(p=>p.Id==id);
        if(index<0)return false;
        if(ProfileCombo.SelectedIndex!=index)ProfileCombo.SelectedIndex=index;
        return true;
    }
    private Control WorkflowProfileRow(HudProfile profile,int ordinal=0)
    {
        var row=new Grid{ColumnDefinitions=new("26,*")};
        row.Children.Add(new Avalonia.Controls.Shapes.Path{Data=IconCatalog.GetGeometry(profile.LeftIcon),Stroke=Brush.Parse("#F2F2F2"),StrokeThickness=1.5,Width=18,Height=18,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center});
        var text=new TextBlock{Text=(ordinal>0?ordinal.ToString("00")+"  ":"")+ProfileDisplayName(profile),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=13,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);row.Children.Add(text);ToolTip.SetTip(row,ProfileDisplayName(profile));return row;
    }
    private void RefreshWorkflowProfiles()
    {
        if(_workflowProfiles is null)return;
        _workflowSync=true;
        try
        {
            var selected=ProfileCombo.SelectedIndex>=0?_profiles.ElementAtOrDefault(ProfileCombo.SelectedIndex)?.Id:null;
            _workflowProfiles.Items.Clear();
            foreach(var p in _profiles.Where(p=>ProfileDisplayName(p).Contains(_workflowSearch?.Text??"",StringComparison.CurrentCultureIgnoreCase)))
            {
                var item=new ListBoxItem{Tag=p.Id,Content=WorkflowProfileRow(p),Padding=new(8),MinHeight=40,HorizontalContentAlignment=HorizontalAlignment.Stretch};_workflowProfiles.Items.Add(item);if(p.Id==selected)_workflowProfiles.SelectedItem=item;
            }
            _workflowSearch!.Watermark=LocalizationManager.Text("搜索方案","Search profiles");
            foreach(var entry in _workflowText)entry.Text.Text=LocalizationManager.Text(entry.Zh,entry.En);
            foreach(var entry in _workflowHeaders)entry.Section.Header=LocalizationManager.Text(entry.Zh,entry.En);
            _refreshCycleWorkspace?.Invoke();
        }
        finally{_workflowSync=false;}
    }
    private void InitializeCycleWorkspace()
    {
        var oldScroll=(ScrollViewer)AutomaticCyclePage.Content!;oldScroll.Content=null;
        RemoveFromParent(CycleOptionsPanel);
        foreach(var c in new Control[]{AutoCycleSwitch,CycleSecondsBox,CycleAnimationModeCombo,CycleProfileList,CycleProfileSourceCombo,CycleAddBtn,CycleRemoveBtn,CycleMoveUpBtn,CycleMoveDownBtn,CycleQueueStatusText})RemoveFromParent(c);
        var intro=new Grid{ColumnDefinitions=new("*,Auto")};intro.Children.Add(WorkflowText("自动轮播","Auto Cycle",16));Grid.SetColumn(AutoCycleSwitch,1);intro.Children.Add(AutoCycleSwitch);
        var time=new Grid{ColumnDefinitions=new("*,*")};var interval=new StackPanel{Spacing=6,Margin=new(0,0,8,0),Children={WorkflowText("轮播间隔 / 秒","Cycle Interval / sec"),CycleSecondsBox}};time.Children.Add(interval);var animation=new StackPanel{Spacing=6,Margin=new(8,0,0,0),Children={WorkflowText("轮播动画","Cycle Animation"),CycleAnimationModeCombo}};Grid.SetColumn(animation,1);time.Children.Add(animation);
        _cycleClickSwitch=new CheckBox{Name="CycleClickSwitch",IsChecked=PersistentClickSwitch.IsChecked};
        bool syncing=false;void Sync(CheckBox from,CheckBox to){if(syncing)return;syncing=true;to.IsChecked=from.IsChecked;syncing=false;}
        PersistentClickSwitch.IsCheckedChanged+=(_,_)=>Sync(PersistentClickSwitch,_cycleClickSwitch);_cycleClickSwitch.IsCheckedChanged+=(_,_)=>Sync(_cycleClickSwitch,PersistentClickSwitch);
        _cycleClickSwitch.Bind(IsEnabledProperty,PersistentClickSwitch.GetObservable(IsEnabledProperty));
        _cycleClickSwitch.Content=WorkflowText("点击或滚轮切换方案","Click or scroll to switch profiles");
        var options=new StackPanel{Spacing=14,Children={intro,time,_cycleClickSwitch,WorkflowText("向上滚动切换上一项，向下滚动切换下一项。","Scroll up for previous; scroll down for next.",12)}};
        var search=new TextBox{Name="CycleWorkspaceSearch",Watermark=LocalizationManager.Text("搜索方案","Search profiles")};var available=new StackPanel{Spacing=6};
        var queue=new StackPanel{Spacing=10,Children={WorkflowText("轮播队列","Cycle Queue",16),CycleProfileList,new WrapPanel{Orientation=Orientation.Horizontal,Children={CycleMoveUpBtn,CycleMoveDownBtn,CycleRemoveBtn}},CycleQueueStatusText}};
        foreach(var b in new[]{CycleMoveUpBtn,CycleMoveDownBtn,CycleRemoveBtn})b.Margin=new(0,0,6,4);
        CycleProfileList.MaxHeight=480;CycleProfileList.MinHeight=220;
        CycleProfileList.Classes.Add("workflow-list");
        CycleProfileSourceCombo.IsVisible=false;CycleAddBtn.IsVisible=false;
        var pool=new StackPanel{Spacing=10,Children={WorkflowText("可用方案","Available profiles",16),search,new ScrollViewer{Content=available,MaxHeight=480},CycleProfileSourceCombo,CycleAddBtn}};
        void Populate()
        {
            search.Watermark=LocalizationManager.Text("搜索方案","Search profiles");available.Children.Clear();
            foreach(var p in _profiles.Where(p=>!_cycleProfileIds.Contains(p.Id) && ProfileDisplayName(p).Contains(search.Text??"",StringComparison.CurrentCultureIgnoreCase)))
            {
                var row=new Grid{ColumnDefinitions=new("*,24")};row.Children.Add(WorkflowProfileRow(p));var plus=new TextBlock{Text="+",FontSize=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(plus,1);row.Children.Add(plus);
                var add=new Button{Content=row,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=40,Padding=new(8)};add.Classes.Add("workflow-add");
                ToolTip.SetTip(add,LocalizationManager.Text("添加","Add"));add.Click+=(_,_)=>{if(!_cycleProfileIds.Contains(p.Id)){_cycleProfileIds.Add(p.Id);RefreshCycleEditor(_cycleProfileIds.Count-1);}};available.Children.Add(add);
            }
        }
        search.TextChanged+=(_,_)=>Populate();_refreshCycleWorkspace=Populate;
        var columns=new Grid{Name="CycleWorkspaceColumns",ColumnDefinitions=new("*,*"),Margin=new(0,0,0,4)};var queued=WorkflowCard(queue);queued.Margin=new(0,0,10,0);columns.Children.Add(queued);var source=WorkflowCard(pool);Grid.SetColumn(source,1);columns.Children.Add(source);
        var body=new StackPanel{Spacing=12,Margin=new(4,12,4,4),Children={WorkflowCard(options),columns}};CycleOptionsPanel.Child=body;CycleOptionsPanel.Padding=new(0);CycleOptionsPanel.BorderThickness=new(0);oldScroll.Content=CycleOptionsPanel;
        Populate();
    }
}
