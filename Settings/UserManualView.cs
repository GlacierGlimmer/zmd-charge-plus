using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace EndfieldChargePlus.Settings;

public sealed class UserManualView : UserControl
{
    internal TextBox SearchBox {get;}=new(){Name="ManualSearch",Margin=new Thickness(0,10,0,12)};
    internal ListBox ChapterList {get;}=new(){Name="ManualChapterList",Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    private readonly TextBlock _navTitle=new(){FontSize=16,FontWeight=FontWeight.SemiBold,Foreground=Brush.Parse("#C6CA4C")};
    private readonly TextBlock _navCount=new(){FontSize=11,Foreground=Brushes.Gray,Margin=new Thickness(0,10,0,0)};
    private readonly TextBlock _title=new(){FontSize=25,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _summary=new(){FontSize=13,LineHeight=20,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#A9A9B0")};
    private readonly TextBlock _location=new(){FontSize=11,Foreground=Brush.Parse("#C6CA4C")};
    private readonly WrapPanel _sectionLinks=new(){Orientation=Orientation.Horizontal};
    private readonly StackPanel _body=new(){Spacing=14};
    private readonly ScrollViewer _scroll=new(){Name="ManualReadingScroll",VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
    private readonly Button _previous=new(),_next=new(),_clearSearch=new();
    private readonly Dictionary<string,Control> _sections=new(StringComparer.Ordinal);
    private string _chapterId="start";
    private bool _rebuilding;
    public event Action<string>? NavigationRequested;
    internal string CurrentChapterId=>_chapterId;
    internal IReadOnlyList<string> SectionIds=>_sections.Keys.ToArray();
    private sealed record ChapterEntry(int Index,ManualChapter Chapter)
    {
        public override string ToString()=>$"{Index+1:00}  {Chapter.Title.Value}";
    }

    public UserManualView()
    {
        ChapterList.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)}});
        ChapterList.ItemTemplate=new FuncDataTemplate<ChapterEntry>((entry,_)=>entry is null ? new TextBlock() : new TextBlock{
            Text=entry.ToString(),FontSize=13,LineHeight=20,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,6)});
        var nav=new Grid{RowDefinitions=new("Auto,Auto,*,Auto")};
        nav.Children.Add(_navTitle);Grid.SetRow(SearchBox,1);nav.Children.Add(SearchBox);Grid.SetRow(ChapterList,2);nav.Children.Add(ChapterList);Grid.SetRow(_navCount,3);nav.Children.Add(_navCount);
        var navBorder=new Border{Background=Brush.Parse("#222224"),BorderBrush=Brush.Parse("#343438"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(14),Child=nav};
        var header=new StackPanel{Spacing=8,Margin=new Thickness(0,2,0,12)};
        header.Children.Add(_location);header.Children.Add(_title);header.Children.Add(_summary);header.Children.Add(_sectionLinks);
        var footer=new Grid{ColumnDefinitions=new("*,Auto,Auto"),Margin=new Thickness(0,12,0,0)};
        _clearSearch.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(_clearSearch);
        Grid.SetColumn(_previous,1);_previous.Margin=new Thickness(8,0,8,0);footer.Children.Add(_previous);Grid.SetColumn(_next,2);footer.Children.Add(_next);
        _scroll.Content=_body;
        var reading=new Grid{RowDefinitions=new("Auto,*,Auto")};reading.Children.Add(header);Grid.SetRow(_scroll,1);reading.Children.Add(_scroll);Grid.SetRow(footer,2);reading.Children.Add(footer);
        var layout=new Grid{ColumnDefinitions=new("226,14,*"),Margin=new Thickness(4,12,4,4)};
        layout.Children.Add(navBorder);Grid.SetColumn(reading,2);layout.Children.Add(reading);Content=layout;
        SearchBox.TextChanged+=(_,_)=>RebuildChapters();
        ChapterList.SelectionChanged+=(_,_)=>{if(!_rebuilding && ChapterList.SelectedItem is ChapterEntry entry){_chapterId=entry.Chapter.Id;RenderChapter(entry.Chapter);}};
        _previous.Click+=(_,_)=>MoveChapter(-1);_next.Click+=(_,_)=>MoveChapter(1);_clearSearch.Click+=(_,_)=>SearchBox.Text="";
        ApplyLocalization();
    }
    internal void ApplyLocalization()
    {
        _navTitle.Text=LocalizationManager.Text("章节目录","Contents");SearchBox.Watermark=LocalizationManager.Text("搜索章节、变量或用法","Search chapters or keys");
        _previous.Content=LocalizationManager.Text("← 上一章","← Previous");_next.Content=LocalizationManager.Text("下一章 →","Next →");_clearSearch.Content=LocalizationManager.Text("清除搜索","Clear search");
        RebuildChapters();
    }
    private void RebuildChapters()
    {
        string query=SearchBox.Text?.Trim() ?? "";
        var entries=UserManualContent.Chapters.Select((c,i)=>new ChapterEntry(i,c)).Where(e=>e.Chapter.Matches(query)).ToArray();
        _rebuilding=true;
        try {ChapterList.ItemsSource=entries;ChapterList.SelectedItem=entries.FirstOrDefault(e=>e.Chapter.Id==_chapterId) ?? entries.FirstOrDefault();}
        finally{_rebuilding=false;}
        _navCount.Text=LocalizationManager.Formatted($"{entries.Length} / {UserManualContent.Chapters.Count} 章 · 离线可用",(System.FormattableString)$"{entries.Length} / {UserManualContent.Chapters.Count} chapters · Offline");
        _clearSearch.IsVisible=query.Length>0;
        if(ChapterList.SelectedItem is ChapterEntry selected){_chapterId=selected.Chapter.Id;RenderChapter(selected.Chapter);}
        else { _title.Text=LocalizationManager.Text("未找到章节","No matching chapters");_summary.Text=LocalizationManager.Text("尝试变量名、功能名称，或清除搜索返回目录。","Try a variable key or feature name, or clear the search.");_location.Text="";_body.Children.Clear();_sectionLinks.Children.Clear();_sections.Clear();_previous.IsEnabled=false;_next.IsEnabled=false; }
    }
    internal void Navigate(string chapterId,string? sectionId=null)
    {
        var entry=ChapterList.Items.OfType<ChapterEntry>().FirstOrDefault(e=>e.Chapter.Id==chapterId);
        if(entry is null){SearchBox.Text="";RebuildChapters();entry=ChapterList.Items.OfType<ChapterEntry>().FirstOrDefault(e=>e.Chapter.Id==chapterId);}
        if(entry is null)return;
        ChapterList.SelectedItem=entry;_chapterId=entry.Chapter.Id;RenderChapter(entry.Chapter);
        if(sectionId is not null)ScrollToSection(sectionId);
    }
    private void MoveChapter(int delta)
    {
        int index=UserManualContent.Chapters.ToList().FindIndex(c=>c.Id==_chapterId)+delta;
        if(index>=0 && index<UserManualContent.Chapters.Count)Navigate(UserManualContent.Chapters[index].Id);
    }
    private void RenderChapter(ManualChapter chapter)
    {
        int index=UserManualContent.Chapters.ToList().FindIndex(c=>c.Id==chapter.Id);
        _location.Text=LocalizationManager.Formatted($"使用手册  /  第 {index+1:00} 章",(System.FormattableString)$"USER MANUAL  /  CHAPTER {index+1:00}");
        _title.Text=chapter.Title.Value;_summary.Text=chapter.Summary.Value;_body.Children.Clear();_sectionLinks.Children.Clear();_sections.Clear();
        _previous.IsEnabled=index>0;_next.IsEnabled=index<UserManualContent.Chapters.Count-1;
        foreach(var block in chapter.Blocks)
        {
            var jump=new Button{Content=block.Title.Value,FontSize=11,Padding=new Thickness(9,5),Margin=new Thickness(0,2,6,2)};
            jump.Click+=(_,_)=>ScrollToSection(block.Id);_sectionLinks.Children.Add(jump);
            var card=new StackPanel{Spacing=11};
            card.Children.Add(new TextBlock{Text=block.Title.Value,FontSize=16,FontWeight=FontWeight.SemiBold,Foreground=Brush.Parse("#E5E5EA"),TextWrapping=TextWrapping.Wrap});
            if(block.Kind=="steps")
            {
                int step=0;
                foreach(var text in block.Body.Value.Split('\n',StringSplitOptions.RemoveEmptyEntries))
                {
                    var row=new Grid{ColumnDefinitions=new("26,*")};
                    row.Children.Add(new TextBlock{Text=(++step).ToString("00"),FontSize=12,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#C6CA4C"),Margin=new Thickness(0,2,6,0)});
                    var paragraph=Paragraph(text);Grid.SetColumn(paragraph,1);row.Children.Add(paragraph);card.Children.Add(row);
                }
            }
            else card.Children.Add(Paragraph(block.Body.Value));
            if(block.Code.Length>0)
            {
                var codePanel=new StackPanel{Spacing=8};
                var copy=new Button{Content=LocalizationManager.Text("复制示例","Copy example"),FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(10,5)};
                copy.Click+=async(_,_)=>{if(TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)await clipboard.SetTextAsync(block.Code);};
                codePanel.FlowDirection=FlowDirection.LeftToRight;
                codePanel.Children.Add(copy);codePanel.Children.Add(new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,Content=new SelectableTextBlock{Text=block.Code,FontFamily=new FontFamily("Consolas, Cascadia Mono, monospace"),FontSize=12,LineHeight=19,TextWrapping=TextWrapping.NoWrap,FlowDirection=FlowDirection.LeftToRight}});
                card.Children.Add(new Border{Background=Brush.Parse("#19191B"),CornerRadius=new CornerRadius(6),Padding=new Thickness(12),Child=codePanel});
            }
            if(block.Target is { } target)
            {
                var action=new Button{Content=LocalizationManager.Text("打开对应设置 →","Open settings →"),Foreground=Brush.Parse("#C6CA4C"),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,7)};
                action.Click+=(_,_)=>NavigationRequested?.Invoke(target);card.Children.Add(action);
            }
            var border=new Border{Background=Brush.Parse("#242426"),BorderBrush=Brush.Parse("#343438"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(18),Child=card};
            _sections[block.Id]=border;_body.Children.Add(border);
        }
        _scroll.Offset=new Vector(0,0);
    }
    private void ScrollToSection(string id)
    {
        if(_sections.TryGetValue(id,out var section))Dispatcher.UIThread.Post(()=>section.BringIntoView(),DispatcherPriority.Loaded);
    }
    private static TextBlock Paragraph(string text)=>new(){Text=text,FontSize=13,LineHeight=22,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#BDBDC4")};
}
