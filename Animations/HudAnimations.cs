using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Animations;

public sealed record AnimationOptions
{
    public double DurationSeconds { get; init; } = 6.0;
    public double BounceStrength { get; init; } = 0.275;
    public double RippleIntensity { get; init; } = 1.0;
    public double RippleSpread { get; init; } = 1.0;

    public static AnimationOptions Default { get; } = new();
    public static AnimationOptions FromSettings(AppSettings s) => new()
    {
        DurationSeconds = Math.Clamp(s.DisplayDurationSeconds, 3d, 10d),
        BounceStrength = Math.Clamp(s.BounceStrength, 0d, 0.5d),
        RippleIntensity = Math.Clamp(s.RippleIntensity, 0d, 2d),
        RippleSpread = Math.Clamp(s.RippleSpread, 0.5d, 1.5d),
    };
}

// Animation timeline is kept aligned with QinAnze/zmd-charge's original HUD.
internal static class HudAnimations
{
    private const double BaselineSeconds = 6.0;
    private const double IntroEndCue = 0.42;
    private const double TStart = 0.04;
    private const double TAppear = 0.07;
    private const double TPillOut = 0.09;
    private const double TBoltPop = 0.10;
    private const double TExpand = 0.12;
    private const double TMove = 0.20;
    private const double TTitle = 0.25;
    private const double THoldB = 0.30;
    private const double TContract = 0.36;
    private const double THoldC = 0.86;
    private const double TClose = 0.89;
    private const double TNumIn = 0.38;
    private const double TNumReady = 0.42;
    private const double PillRadiusA = 30d;
    private const double PillRadiusB = 18d;
    private const double PillHeightA = 60d;
    private const double PillHeightB = 90d;
    private const double IconOffsetB = -179d;
    private const double IconOffsetC = -245d;

    private static readonly KeySpline KS_In = new(0.42, 0, 1, 1);
    private static readonly KeySpline KS_Out = new(0, 0, 0.58, 1);
    private static readonly KeySpline KS_InOut = new(0.42, 0, 0.58, 1);
    private static readonly KeySpline KS_Smooth = new(0.65, 0, 0.35, 1);

    private static KeySpline BackOut(AnimationOptions o) =>
        new(0.175, 0.885, 0.32, 1d + o.BounceStrength);

    private static double MapCue(AnimationOptions o, double cue)
    {
        double d = Math.Clamp(o.DurationSeconds, 3d, 10d);
        double introFrac = IntroEndCue * BaselineSeconds / d;
        if (cue <= IntroEndCue)
            return cue / IntroEndCue * introFrac;
        return introFrac + (cue - IntroEndCue) / (1 - IntroEndCue) * (1 - introFrac);
    }

    public static Animation PillCorner(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, CR(PillRadiusA)));
        a.Children.Add(KF(MapCue(o, TAppear), KS_In, CR(PillRadiusA)));
        a.Children.Add(KF(MapCue(o, TExpand), KS_InOut, CR(PillRadiusB)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, CR(PillRadiusB)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, CR(PillRadiusA)));
        return a;
    }

    public static Animation PillAppear(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0), SX(0.6), SY(0.6)));
        a.Children.Add(KF(MapCue(o, TStart), KS_In, Op(0), SX(0.6), SY(0.6)));
        a.Children.Add(KF(MapCue(o, TAppear), KS_In, Op(0), SX(0.6), SY(0.6)));
        a.Children.Add(KF(MapCue(o, TPillOut), BackOut(o), Op(1), SX(1d), SY(1d)));
        a.Children.Add(KF(MapCue(o, THoldC), KS_In, Op(1), SX(1d), SY(1d)));
        return a;
    }

    public static Animation PillHeight(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, H(PillHeightA)));
        a.Children.Add(KF(MapCue(o, TAppear), KS_In, H(PillHeightA)));
        a.Children.Add(KF(MapCue(o, TExpand), BackOut(o), H(PillHeightB)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, H(PillHeightB)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, H(PillHeightA)));
        a.Children.Add(KF(MapCue(o, THoldC), KS_In, H(PillHeightA)));
        return a;
    }

    public static Animation ScaleOut(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, SX(1d), SY(1d)));
        a.Children.Add(KF(MapCue(o, THoldC), KS_In, SX(1d), SY(1d)));
        a.Children.Add(KF(MapCue(o, TClose), KS_In, SX(0d), SY(0d)));
        return a;
    }

    public static Animation BoltIcon(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0.00), null, Op(0), SX(0.4), SY(0.4), TX(0)));
        a.Children.Add(KF(MapCue(o, TStart), KS_In, Op(0), SX(0.4), SY(0.4), TX(0)));
        a.Children.Add(KF(MapCue(o, TBoltPop), BackOut(o), Op(1), SX(1.12), SY(1.12), TX(0)));
        a.Children.Add(KF(MapCue(o, TExpand), KS_Out, Op(1), SX(1), SY(1), TX(0)));
        a.Children.Add(KF(MapCue(o, TMove), KS_Smooth, Op(1), SX(1), SY(1), TX(IconOffsetB)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, Op(1), SX(1), SY(1), TX(IconOffsetB)));
        a.Children.Add(KF(MapCue(o, TContract), KS_Smooth, Op(1), SX(1), SY(1), TX(IconOffsetC)));
        a.Children.Add(KF(MapCue(o, THoldC), KS_In, Op(1), SX(1), SY(1), TX(IconOffsetC)));
        return a;
    }

    public static Animation RippleHost(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, TX(0)));
        a.Children.Add(KF(MapCue(o, TExpand), KS_In, TX(0)));
        a.Children.Add(KF(MapCue(o, TMove), KS_Smooth, TX(IconOffsetB)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, TX(IconOffsetB)));
        a.Children.Add(KF(MapCue(o, TContract), KS_Smooth, TX(IconOffsetC)));
        return a;
    }

    public static Animation CircleForm(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0)));
        a.Children.Add(KF(MapCue(o, TStart), KS_In, Op(0)));
        a.Children.Add(KF(MapCue(o, TAppear), KS_Out, Op(1)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, Op(1)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, Op(0)));
        return a;
    }

    public static Animation SquareForm(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, Op(0)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, Op(1)));
        return a;
    }

    public static Animation TitleHost(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0)));
        a.Children.Add(KF(MapCue(o, TMove), KS_In, Op(0)));
        a.Children.Add(KF(MapCue(o, TTitle), KS_Out, Op(1)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_In, Op(1)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, Op(0)));
        return a;
    }

    public static Animation NumHost(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0)));
        a.Children.Add(KF(MapCue(o, TContract), KS_In, Op(0)));
        a.Children.Add(KF(MapCue(o, TNumIn), KS_In, Op(0)));
        a.Children.Add(KF(MapCue(o, TNumReady), KS_Out, Op(1)));
        a.Children.Add(KF(MapCue(o, THoldC), KS_In, Op(1)));
        return a;
    }

    public static Animation RippleRise(AnimationOptions o)
    {
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, TY(16)));
        a.Children.Add(KF(MapCue(o, TExpand), KS_In, TY(16)));
        a.Children.Add(KF(MapCue(o, TExpand + 0.02), KS_Out, TY(16)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_Out, TY(0)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, TY(0)));
        return a;
    }

    public static Animation Ripple(AnimationOptions o, double endScale, double peakOp)
    {
        double spread = Math.Clamp(o.RippleSpread, 0.5d, 1.5d);
        double intensity = Math.Clamp(o.RippleIntensity, 0d, 2d);
        double target = endScale * spread;
        double peak = Math.Min(1d, peakOp * intensity);
        var a = New(o);
        a.Children.Add(KF(MapCue(o, 0d), null, Op(0), SX(0), SY(0)));
        a.Children.Add(KF(MapCue(o, TExpand), KS_In, Op(0), SX(0), SY(0)));
        a.Children.Add(KF(MapCue(o, TExpand + 0.02), KS_Out, Op(peak), SX(0.05), SY(0.05)));
        a.Children.Add(KF(MapCue(o, THoldB), KS_Out, Op(peak), SX(target), SY(target)));
        a.Children.Add(KF(MapCue(o, TContract), KS_InOut, Op(0), SX(target), SY(target)));
        return a;
    }

    private const double SimpleBaselineSeconds = 5.0;
    private const double SimpleIntroEndCue = 0.08;
    private const double TSimpleAppear = 0.05;
    private const double TSimpleHold = 0.75;
    private const double TSimpleClose = 0.80;

    private static double MapCueSimple(AnimationOptions o, double cue)
    {
        double d = Math.Clamp(o.DurationSeconds, 3d, 10d);
        double introFrac = SimpleIntroEndCue * SimpleBaselineSeconds / d;
        if (cue <= SimpleIntroEndCue)
            return cue / SimpleIntroEndCue * introFrac;
        return introFrac + (cue - SimpleIntroEndCue) / (1 - SimpleIntroEndCue) * (1 - introFrac);
    }

    public static Animation SimplePillAppear(AnimationOptions o)
    {
        var a = NewSimple(o);
        a.Children.Add(KF(MapCueSimple(o, 0d), null, Op(0), SX(0.6), SY(0.6)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleAppear), KS_Out, Op(1), SX(1d), SY(1d)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleHold), KS_In, Op(1), SX(1d), SY(1d)));
        return a;
    }

    public static Animation SimpleFadeIn(AnimationOptions o)
    {
        var a = NewSimple(o);
        a.Children.Add(KF(MapCueSimple(o, 0d), null, Op(0)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleAppear), KS_In, Op(0)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleAppear + 0.03), KS_Out, Op(1)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleHold), KS_In, Op(1)));
        return a;
    }

    public static Animation SimpleScaleOut(AnimationOptions o)
    {
        var a = NewSimple(o);
        a.Children.Add(KF(MapCueSimple(o, 0d), null, SX(1d), SY(1d)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleHold), KS_In, SX(1d), SY(1d)));
        a.Children.Add(KF(MapCueSimple(o, TSimpleClose), KS_In, SX(0d), SY(0d)));
        return a;
    }

    /// <summary>
    /// 从最终 C 状态执行与原项目相同方向的整体缩小收回。
    /// 用于常驻模式关闭、手动收回等不在完整时间线末尾发生的场景。
    /// </summary>
    public static Animation CloseFromC()
    {
        var a = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            FillMode = FillMode.Forward,
        };
        a.Children.Add(KF(0d, null, SX(1d), SY(1d)));
        a.Children.Add(KF(1d, KS_In, SX(0d), SY(0d)));
        return a;
    }

    private static Animation New(AnimationOptions o) => new()
    {
        Duration = TimeSpan.FromSeconds(Math.Clamp(o.DurationSeconds, 3d, 10d)),
        FillMode = FillMode.Forward,
    };

    private static Animation NewSimple(AnimationOptions o) => new()
    {
        Duration = TimeSpan.FromSeconds(Math.Clamp(o.DurationSeconds, 3d, 10d)),
        FillMode = FillMode.Forward,
    };

    private static KeyFrame KF(double cue, KeySpline? ks, params Setter[] setters)
    {
        var kf = new KeyFrame { Cue = new Cue(cue) };
        if (ks is not null)
            kf.KeySpline = ks;
        foreach (var s in setters)
            kf.Setters.Add(s);
        return kf;
    }

    private static Setter Op(double v) => Set(Visual.OpacityProperty, v);
    private static Setter TX(double v) => Set(TranslateTransform.XProperty, v);
    private static Setter TY(double v) => Set(TranslateTransform.YProperty, v);
    private static Setter SX(double v) => Set(ScaleTransform.ScaleXProperty, v);
    private static Setter SY(double v) => Set(ScaleTransform.ScaleYProperty, v);
    private static Setter CR(double v) => Set(Border.CornerRadiusProperty, new CornerRadius(v));
    private static Setter H(double v) => Set(Border.HeightProperty, v);
    private static Setter Set(AvaloniaProperty property, object value) => new(property, value);
}
