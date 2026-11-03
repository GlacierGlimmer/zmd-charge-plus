// Animation equations and stagger timing adapted from KnifeLemon/Filee DonutMenu.
// Copyright (c) 2026 Myeonggu Kim and Filee contributors. MIT; Licenses/Filee-MIT.txt.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Customization;

internal sealed class ProfileRingControl : Control
{
    private IReadOnlyList<HudProfile> _profiles = Array.Empty<HudProfile>();
    internal IReadOnlyList<HudProfile> Profiles { get => _profiles; set { _profiles=value; Page=Math.Min(Page,PageCount-1); SelectedIndex=-1; Rebuild(); } }
    internal WorkspacePreferences Preferences { get; set; } = new();
    internal int Page { get; private set; }
    internal int SelectedIndex { get; private set; } = -1;
    internal const int PageSize=12;
    internal string? SelectedId => SelectedIndex>=0 ? Profiles.ElementAtOrDefault(Page*PageSize+SelectedIndex)?.Id : null;
    internal int PageCount=>Math.Max(1,(Profiles.Count+PageSize-1)/PageSize);
    internal bool Interactive { get; set; }
    internal event Action? SelectionChanged;
    internal event Action<string>? ItemPressed;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly System.Diagnostics.Stopwatch _clock=new();
    private double _lastTime,_openTime=100;
    private double[] _hover=Array.Empty<double>(),_velocity=Array.Empty<double>();
    private Geometry[] _wedges=Array.Empty<Geometry>();
    private Size _geometrySize;
    private bool _closing;
    private Action? _closed;
    internal bool AnimationRunning=>_timer.IsEnabled;
    internal double AnimationProgress=>Math.Clamp(_openTime/.728,0,1);
    public ProfileRingControl()
    {
        _timer.Tick+=(_,_)=>{double now=_clock.Elapsed.TotalSeconds;bool active=Advance(Math.Clamp(now-_lastTime,0,.05));_lastTime=now;InvalidateVisual();if(!active)_timer.Stop();};
        AttachedToVisualTree+=(_,_)=>PlayOpen();
        DetachedFromVisualTree+=(_,_)=>{_timer.Stop();_closed=null;};
        PointerMoved+=(_,e)=>{if(Interactive)PointAt(e.GetPosition(this));};
        PointerExited+=(_,_)=>{if(Interactive)SetSelected(-1);};
        PointerPressed+=(_,e)=>{if(Interactive && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){PointAt(e.GetPosition(this));if(SelectedId is {} id)ItemPressed?.Invoke(id);e.Handled=true;}};
        PointerWheelChanged+=(_,e)=>{if(Interactive){ChangePage(e.Delta.Y>0 ? -1:1);e.Handled=true;}};
    }
    private int Count=>Math.Min(PageSize,Math.Max(0,Profiles.Count-Page*PageSize));
    private void Rebuild(){_hover=new double[Count];_velocity=new double[Count];_wedges=Array.Empty<Geometry>();InvalidateVisual();}
    internal void ChangePage(int delta){Page=(Page+delta%PageCount+PageCount)%PageCount;SelectedIndex=-1;Rebuild();PlayOpen();SelectionChanged?.Invoke();}
    internal void Highlight(string? id){int index=id is null ? -1 : Profiles.ToList().FindIndex(p=>p.Id==id);if(index>=0 && index/PageSize!=Page){Page=index/PageSize;Rebuild();PlayOpen();}SetSelected(index<0 ? -1 : index%PageSize);}
    private void SetSelected(int index){if(SelectedIndex==index)return;SelectedIndex=index;Start();SelectionChanged?.Invoke();}
    internal void PointAt(Point point)
    {
        double outer=Math.Min(Bounds.Width,Bounds.Height)*.43;
        var hit=DonutGeometry.HitTest(new Point(Bounds.Width/2,Bounds.Height/2),point,outer*.47,outer+7,Count);
        SetSelected(hit.Kind==DonutHitKind.Segment ? hit.Index : -1);
    }
    private void Start(){if(_timer.IsEnabled)return;_clock.Restart();_lastTime=0;_timer.Start();}
    internal void PlayOpen(){_closing=false;_closed=null;_openTime=0;Start();}
    internal void PlayClose(Action closed){_closing=true;_closed=closed;_openTime=0;Start();}
    internal bool Advance(double dt)
    {
        _openTime+=dt;bool active=_openTime<(_closing ? .16+Count*.01 : .42+Count*.028);
        // Filee's selected slice is spring-driven; deselection is critically damped.
        for(int i=0;i<Count;i++){
            double target=i==SelectedIndex ? 1:0,damping=target==1 ? 24:2*Math.Sqrt(320);
            int steps=Math.Max(1,(int)Math.Ceiling(dt/.008));double step=dt/steps;
            for(int n=0;n<steps;n++){_velocity[i]+=(320*(target-_hover[i])-damping*_velocity[i])*step;_hover[i]+=_velocity[i]*step;}
            active|=Math.Abs(_hover[i]-target)>.001 || Math.Abs(_velocity[i])>.001;
        }
        if(_closing && _openTime>=.16+Count*.01){var done=_closed;_closed=null;_closing=false;done?.Invoke();return false;}
        return active;
    }
    private static double EaseOutBack(double t)=>1+2.55*Math.Pow(t-1,3)+1.55*Math.Pow(t-1,2);
    private void EnsureGeometry(Point center,double inner,double outer)
    {
        if(_wedges.Length==Count && _geometrySize==Bounds.Size)return;
        _geometrySize=Bounds.Size;_wedges=new Geometry[Count];
        for(int i=0;i<Count;i++){
            var geometry=new StreamGeometry();using var path=geometry.Open();
            if(Count==1){path.SetFillRule(FillRule.EvenOdd);foreach(double radius in new[]{outer,inner}){path.BeginFigure(new Point(center.X,center.Y-radius),true);path.ArcTo(new Point(center.X,center.Y+radius),new Size(radius,radius),0,false,SweepDirection.Clockwise);path.ArcTo(new Point(center.X,center.Y-radius),new Size(radius,radius),0,false,SweepDirection.Clockwise);path.EndFigure(true);}}
            else {var outline=DonutGeometry.Wedge(center,inner,outer,DonutGeometry.MidAngle(i,Count),DonutGeometry.Span(Count),4,6);path.BeginFigure(outline.Start,true);foreach(var s in outline.Steps)if(s.IsArc)path.ArcTo(s.To,new Size(s.ArcRadius,s.ArcRadius),0,s.IsLargeArc,s.Clockwise?SweepDirection.Clockwise:SweepDirection.CounterClockwise);else path.LineTo(s.To);path.EndFigure(true);}
            _wedges[i]=geometry;
        }
    }
    public override void Render(DrawingContext context)
    {
        // A detached/hidden preview can receive a final compositor render with
        // zero bounds while a popup or settings window is being destroyed.
        // Do not build negative radii or zero-height FormattedText at that point.
        if (!double.IsFinite(Bounds.Width) || !double.IsFinite(Bounds.Height) ||
            Bounds.Width < 32 || Bounds.Height < 32) return;
        var c=WorkspaceTheme.Colours(Preferences);var center=new Point(Bounds.Width/2,Bounds.Height/2);
        double outer=Math.Min(Bounds.Width,Bounds.Height)*.43,inner=outer*.47;
        EnsureGeometry(center,inner,outer);
        // Instrument ticks and the three Endfield signal colours belong to the HUD, not a borrowed theme.
        for(int i=0;i<60;i++){double a=i*Math.PI/30;context.DrawLine(new Pen(Brush.Parse(i%5==0?"#66665A":"#35352F"),1),DonutGeometry.PointAt(center,outer+11,a),DonutGeometry.PointAt(center,outer+(i%5==0?16:13),a));}
        for(int i=0;i<Count;i++){
            double t=Math.Clamp((_openTime-i*(_closing?.01:.028))/(_closing?.16:.42),0,1),open=_closing?1-t*t*t:EaseOutBack(t);
            double angle=DonutGeometry.MidAngle(i,Count),hover=Math.Clamp(_hover[i],0,1.1),scale=.55+.45*open;
            var shift=DonutGeometry.PointAt(default,hover*7,angle);
            var matrix=Matrix.CreateTranslation(-center.X,-center.Y)*Matrix.CreateRotation((1-Math.Min(open,1))*-.35)*Matrix.CreateScale(scale*(1+hover*.025),scale*(1+hover*.025))*Matrix.CreateTranslation(center.X+shift.X,center.Y+shift.Y);
            using(context.PushTransform(matrix))using(context.PushOpacity(Math.Clamp(open,0,1))){
                context.DrawGeometry(c.Brush(c.Card),new Pen(c.Brush(c.Border),1),_wedges[i]);
                if(hover>.001)using(context.PushOpacity(Math.Clamp(hover,0,1)))context.DrawGeometry(c.Brush(c.Accent),null,_wedges[i]);
                bool hot=i==SelectedIndex;var ink=c.Brush(hot?c.OnAccent:c.Text);var point=DonutGeometry.PointAt(center,(inner+outer)/2,angle);
                double iconSize=Math.Clamp(outer*.15,19,28);
                var icon=IconCatalog.GetGeometry(Profiles[Page*PageSize+i].LeftIcon);var b=icon.Bounds;
                using(context.PushTransform(Matrix.CreateTranslation(-b.X,-b.Y)*Matrix.CreateScale(iconSize/Math.Max(b.Width,1),iconSize/Math.Max(b.Height,1))*Matrix.CreateTranslation(point.X-iconSize/2,point.Y-iconSize/2-5)))context.DrawGeometry(null,new Pen(ink,1.45*24/iconSize),icon);
                DrawText(context,(Page*PageSize+i+1).ToString("00"),point+new Vector(0,iconSize*.65+4),32,12,hot?c.OnAccent:c.Muted,16);
            }
        }
        context.DrawEllipse(c.Brush(c.Input),new Pen(Brush.Parse("#57574A"),1),center,inner-5,inner-5);
        double barY=center.Y-inner*.5;
        var colours=new[]{"#FFFA00","#01FFA2","#FF1AAC"};for(int i=0;i<3;i++)context.DrawRectangle(Brush.Parse(colours[i]),null,new Rect(center.X-12+i*9,barY,6,2));
        var title=SelectedId is null?WorkspaceStrings.Get("Profile switching"):BuiltInProfileLocalization.ForCurrentLanguage(Profiles[Page*PageSize+SelectedIndex]).Name;
        DrawText(context,title,center+new Vector(0,2),inner*1.55,13,c.Text,inner*.8);
        DrawText(context,$"{Page+1:00} / {PageCount:00}",center+new Vector(0,inner*.58),inner*1.4,11,c.Muted,16);
    }
    private static void DrawText(DrawingContext context,string value,Point center,double width,double size,Color color,double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            !double.IsFinite(size) || size <= 0) return;
        var text=new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily.Default,FontStyle.Normal,FontWeight.SemiBold),size,new SolidColorBrush(color)){MaxTextWidth=width,MaxTextHeight=height,Trimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center};
        context.DrawText(text,center-new Vector(width/2,text.Height/2));
    }
}
