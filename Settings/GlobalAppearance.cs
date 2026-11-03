using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace EndfieldChargePlus.Settings;

/// <summary>Scales from original sizes, never from the previous scaled result.</summary>
internal static class GlobalAppearance
{
    private sealed class Original(double font,double height,double minHeight,bool localFont=false)
    {
        public double Font=font, Height=height, MinHeight=minHeight;
        public bool LocalFont=localFont;
    }
    private sealed class WindowState
    {
        public required LayoutTransformControl Transform;
        public required ScrollViewer Viewport;
        public required Control Root;
        public double FontScale=1,UiScale=1,MinWidth,MinHeight;
        public bool Applying,Scheduled;
    }
    private static readonly ConditionalWeakTable<Control,Original> Originals=new();
    private sealed class OriginalColumn(GridLength width) {public GridLength Width=width;}
    private static readonly ConditionalWeakTable<ColumnDefinition,OriginalColumn> Columns=new();
    private static readonly ConditionalWeakTable<Window,WindowState> Windows=new();
    private static double _font=1,_ui=1;
    internal static (double Font,double Ui) CurrentScales => (_font,_ui);
    private static readonly List<WeakReference<Window>> RegisteredWindows=new();
    public static double NormalizeFontScale(double value)=>double.IsFinite(value) ? Math.Clamp(value,.8,1.25) : 1;
    public static double NormalizeUiScale(double value)=>double.IsFinite(value) ? Math.Clamp(value,.8,1.5) : 1;

    public static void Apply(Window window,double fontScale,double uiScale)
    {
        fontScale=NormalizeFontScale(fontScale);uiScale=NormalizeUiScale(uiScale);
        if(window is SettingsWindow)SetPreferences(fontScale,uiScale);
        bool created=false;
        if(!Windows.TryGetValue(window,out var state))
        {
            // Keep window controls and the rounded frame outside the scalable viewport.
            var bodyHost=window is SettingsWindow ? window.FindControl<ContentControl>("SettingsBodyHost") : null;
            var content=(bodyHost?.Content ?? window.Content) as Control ?? throw new InvalidOperationException("Window content is missing.");
            if(bodyHost is not null)bodyHost.Content=null;else window.Content=null;
            var transform=new LayoutTransformControl { Child=content };
            // At large scales, both axes remain reachable on a small display.
            var viewport=new ScrollViewer { Content=transform,Padding=new Avalonia.Thickness(0),
                HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            if(bodyHost is not null)bodyHost.Content=viewport;else window.Content=viewport;
            state=new WindowState { Transform=transform,Viewport=viewport,Root=content,
                MinWidth=Math.Max(content.MinWidth,window.MinWidth-32-content.Margin.Left-content.Margin.Right),
                MinHeight=Math.Max(content.MinHeight,window.MinHeight-32-(bodyHost is not null ? 92 : 0)-content.Margin.Top-content.Margin.Bottom) };
            Windows.Add(window,state);created=true;RegisteredWindows.Add(new(window));
            window.LayoutUpdated+=(_,_)=>ScheduleFonts(window,state);
        }
        if(!created && state.FontScale==fontScale && state.UiScale==uiScale)return;
        state.FontScale=fontScale;state.UiScale=uiScale;
        state.Transform.LayoutTransform=new ScaleTransform(uiScale,uiScale);
        ApplyFonts(window,state);
    }

    private static void ScheduleFonts(Window window,WindowState state)
    {
        if(state.Applying || state.Scheduled)return;
        state.Scheduled=true;
        // Never change text metrics or grid definitions inside an arrange pass.
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>{
            state.Scheduled=false;
            if(window.IsVisible)ApplyFonts(window,state);
        },Avalonia.Threading.DispatcherPriority.Background);
    }

    internal static void SetPreferences(double font,double ui)
    {
        font=NormalizeFontScale(font);ui=NormalizeUiScale(ui);
        if(_font==font && _ui==ui)return;
        _font=font;_ui=ui;
        // Scale only when preferences change. Dynamic pages are handled by the
        // existing coalesced LayoutUpdated pass; no idle polling or colour writes.
        for(int i=RegisteredWindows.Count-1;i>=0;i--)
            if(!RegisteredWindows[i].TryGetTarget(out var window))RegisteredWindows.RemoveAt(i);
            else if(window.IsVisible)Apply(window,font,ui);
    }

    private static void ApplyFonts(Window window,WindowState state)
    {
        if(state.Applying)return;
        state.Applying=true;
        try
        {
            if(window.ClientSize.Width>0 && window.ClientSize.Height>0)
            {
                // Use the actual viewport, including scrollbar space when present.
                // Reserving 16 pixels unconditionally centered the settings root
                // inside the client area and introduced an unwanted 8-pixel frame.
                double width=state.Viewport.Viewport.Width,height=state.Viewport.Viewport.Height;
                if(!double.IsFinite(width) || width<=0)width=window.ClientSize.Width;
                if(!double.IsFinite(height) || height<=0)height=window.ClientSize.Height;
                state.Root.Width=Math.Max(state.MinWidth*Math.Max(1,state.FontScale),
                    width/state.UiScale-state.Root.Margin.Left-state.Root.Margin.Right);
                state.Root.Height=state.Root is StackPanel || state.Root is Border {Child:StackPanel}
                    ? double.NaN : Math.Max(state.MinHeight,
                        height/state.UiScale-state.Root.Margin.Top-state.Root.Margin.Bottom);
            }
            foreach(var c in state.Root.GetVisualDescendants().OfType<Control>().Prepend(state.Root))
            {
                if(c is Grid grid && grid.TemplatedParent is null)
                    foreach(var column in grid.ColumnDefinitions)
                    {
                        var originalWidth=Columns.GetValue(column,k=>new OriginalColumn(k.Width)).Width;
                        if(originalWidth.IsAbsolute && originalWidth.Value>=48)
                        {
                            var columnTarget=new GridLength(originalWidth.Value*Math.Max(1,state.FontScale));
                            if(column.Width!=columnTarget)column.Width=columnTarget;
                        }
                    }
                if(c is TextBlock child && c.GetVisualAncestors().Any(a=>a is TextBox or ComboBox))
                {
                    var childOriginal=Originals.GetValue(child,k=>new Original(child.FontSize,child.Height,child.MinHeight,child.IsSet(TextBlock.FontSizeProperty)));
                    if(!childOriginal.LocalFont){child.ClearValue(TextBlock.FontSizeProperty);continue;}
                }
                // Leaf text controls avoid multiplying an inherited font twice.
                double font=c switch {TextBlock b=>b.FontSize,TextBox b=>b.FontSize,ComboBox b=>b.FontSize,_=>double.NaN};
                if(!double.IsFinite(font))
                {
                    if(c is TemplatedControl)
                    {
                        var size=Originals.GetValue(c,_=>new Original(0,c.Height,c.MinHeight));
                        if(double.IsFinite(size.Height) && size.Height<100)c.Height=Math.Max(size.Height,size.Height*state.FontScale);
                        if(size.MinHeight<100)c.MinHeight=Math.Max(size.MinHeight,size.MinHeight*state.FontScale);
                    }
                    continue;
                }
                var original=Originals.GetValue(c,_=>new Original(font,c.Height,c.MinHeight,c.IsSet(TextBlock.FontSizeProperty)));
                double target=original.Font*state.FontScale;
                if(c is TextBlock text && Math.Abs(text.FontSize-target)>.001)
                {
                    text.FontSize=target;
                    if(double.IsFinite(text.LineHeight))text.LineHeight=double.NaN;
                }
                else if(c is TextBox box && Math.Abs(box.FontSize-target)>.001)box.FontSize=target;
                else if(c is ComboBox combo && Math.Abs(combo.FontSize-target)>.001)combo.FontSize=target;
                if(c is TextBox or ComboBox)
                {
                    if(double.IsFinite(original.Height))c.Height=Math.Max(original.Height,original.Height*state.FontScale);
                    c.MinHeight=Math.Max(original.MinHeight,original.MinHeight*state.FontScale);
                }
            }
        }
        finally {state.Applying=false;}
    }
}
