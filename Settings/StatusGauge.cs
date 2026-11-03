using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace EndfieldChargePlus.Settings;

// Small dashboard gauge; values are real samples, the stroke interpolates only between them.
internal sealed class StatusGauge : Control
{
    internal static readonly StyledProperty<double> ValueProperty=AvaloniaProperty.Register<StatusGauge,double>(nameof(Value),0);
    internal static readonly StyledProperty<double> AmountProperty=AvaloniaProperty.Register<StatusGauge,double>(nameof(Amount),0);
    public double Value{get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public double Amount{get=>GetValue(AmountProperty);set=>SetValue(AmountProperty,value);}
    internal string Label {get;set;}="CPU";
    internal Color Accent {get;set;}=Color.Parse("#FFFA00");
    internal double? Sample {get;private set;}
    internal bool IsByteValue {get;private set;}
    internal double? ByteSample {get;private set;}
    internal bool HasReading=>IsByteValue?ByteSample is not null:Sample is not null;
    internal string DisplayValue => !HasReading?"—":IsByteValue?FormatBytes(Amount):Value.ToString("0.#",CultureInfo.CurrentCulture)+"%";
    static StatusGauge()=>AffectsRender<StatusGauge>(ValueProperty,AmountProperty);
    public StatusGauge()
    {
        Width=86;Height=32;
        Transitions=new Transitions{new DoubleTransition{Property=ValueProperty,Duration=TimeSpan.FromMilliseconds(320),Easing=new CubicEaseOut()},new DoubleTransition{Property=AmountProperty,Duration=TimeSpan.FromMilliseconds(320),Easing=new CubicEaseOut()}};
    }
    internal void Update(double? value,string? display=null)
    {
        IsByteValue=false;ByteSample=null;
        Sample=SettingsStatusSampler.Percent(value);
        Value=Sample??0;InvalidateVisual();
    }
    internal void UpdateBytes(double? bytes,double? capacityPercent)
    {
        IsByteValue=true;ByteSample=bytes is {} n && double.IsFinite(n) && n>=0?n:null;
        Sample=ByteSample is not null?SettingsStatusSampler.Percent(capacityPercent):null;
        Value=Sample??0;Amount=ByteSample??0;InvalidateVisual();
    }
    internal static string FormatBytes(double bytes)
    {
        string[] units={"B","KiB","MiB","GiB","TiB"};int unit=0;
        while(bytes>=1024 && unit<units.Length-1){bytes/=1024;unit++;}
        return bytes.ToString(unit==0?"0":"0.#",CultureInfo.CurrentCulture)+" "+units[unit];
    }
    public override void Render(DrawingContext context)
    {
        // Give the complete gauge (arc, label, value and spacing) a hit region.
        context.DrawRectangle(Brushes.Transparent,null,new Rect(Bounds.Size));
        var center=new Point(15,16);const double radius=11;
        context.DrawEllipse(null,new Pen(Brush.Parse("#393A32"),3),center,radius,radius);
        if(Sample is not null && Value>.01)
        {
            double sweep=Math.Clamp(Value,0,99.999)*Math.PI/50;
            var arc=new StreamGeometry();using(var p=arc.Open()){p.BeginFigure(new Point(center.X,center.Y-radius),false);p.ArcTo(new Point(center.X+Math.Sin(sweep)*radius,center.Y-Math.Cos(sweep)*radius),new Size(radius,radius),0,sweep>Math.PI,SweepDirection.Clockwise);p.EndFigure(false);}
            context.DrawGeometry(null,new Pen(new SolidColorBrush(Accent),3,lineCap:PenLineCap.Round),arc);
        }
        Draw(context,Label,new Point(33,2),11,Color.Parse("#A1A1A1"));Draw(context,DisplayValue,new Point(33,16),12,!HasReading?Color.Parse("#A1A1A1"):Color.Parse("#F2F2F2"));
    }
    private void Draw(DrawingContext c,string value,Point at,double size,Color colour)=>c.DrawText(new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily.Default,FontStyle.Normal,FontWeight.Medium),size,new SolidColorBrush(colour)){MaxTextWidth=Math.Max(1,Width-33),MaxTextHeight=16,Trimming=TextTrimming.CharacterEllipsis},at);
}
