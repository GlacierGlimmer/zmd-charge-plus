// Adapted from KnifeLemon/Filee, Copyright (c) 2026 Myeonggu Kim and Filee contributors.
// MIT license; see Licenses/Filee-MIT.txt.
// Pure math for the donut: slice angles, hit testing and slice outlines. Unit tested (DonutGeometryTests).
//
// Angles are measured clockwise from 12 o'clock, in radians. Slice i is centred on i·span,
// so the first preset sits at the top and the rest follow clockwise.

using Avalonia;

namespace EndfieldChargePlus.Customization;

/// <summary>What is under a point of the donut.</summary>
public enum DonutHitKind
{
    /// <summary>Outside the donut.</summary>
    Outside,
    /// <summary>The hole in the middle (cancel / close).</summary>
    Center,
    /// <summary>A slice; <see cref="DonutHit.Index"/> is valid.</summary>
    Segment,
    /// <summary>The ring of a donut without slices (drop target for the first preset).</summary>
    EmptyRing,
    /// <summary>The ✎ edit button of the hovered slice (edit mode only).</summary>
    EditButton,
    /// <summary>The progress ring shown after a drop.</summary>
    ProgressRing,
    /// <summary>The × (cancel) button on the progress ring.</summary>
    ProgressCancel,

    /// <summary>A slice of the small ring a group slice fans out; <see cref="DonutHit.Index"/> is the group, <see cref="DonutHit.Sub"/> the slice.</summary>
    SubSegment,
}

/// <param name="Sub">With <see cref="DonutHitKind.SubSegment"/>: which of the group's presets.</param>
public readonly record struct DonutHit(DonutHitKind Kind, int Index = -1, int Sub = -1);

/// <summary>One step of a slice's outline: a straight line to <paramref name="To"/>, or (<paramref name="ArcRadius"/> &gt; 0) an arc.</summary>
public readonly record struct OutlineStep(Point To, double ArcRadius = 0, bool IsLargeArc = false, bool Clockwise = true)
{
    public bool IsArc => ArcRadius > 0;
}

/// <summary>A closed slice outline, starting at <paramref name="Start"/>; <paramref name="CornerRadius"/> is the rounding used after clamping.</summary>
public sealed record WedgeOutline(Point Start, IReadOnlyList<OutlineStep> Steps, double CornerRadius);

/// <summary>Geometry helpers.</summary>
public static class DonutGeometry
{
    /// <summary>Angle covered by one of <paramref name="count"/> slices.</summary>
    public static double Span(int count) => count <= 0 ? 2 * Math.PI : 2 * Math.PI / count;

    /// <summary>Centre angle of slice <paramref name="index"/>.</summary>
    public static double MidAngle(int index, int count) => index * Span(count);

    /// <summary>Converts a clockwise-from-top angle and radius into a point relative to <paramref name="center"/>.</summary>
    public static Point PointAt(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

    /// <summary>Clockwise-from-top angle (0..2π) of <paramref name="p"/> around <paramref name="center"/>.</summary>
    public static double AngleOf(Point center, Point p)
    {
        var a = Math.Atan2(p.X - center.X, center.Y - p.Y);
        return a < 0 ? a + 2 * Math.PI : a;
    }

    /// <summary>Index of the slice containing the given angle.</summary>
    public static int IndexAt(double angle, int count)
    {
        if (count <= 0)
            return -1;
        var span = Span(count);
        return (int)Math.Floor((angle + span / 2) / span) % count;
    }

    /// <summary>How far a group's fan reaches into each neighbouring slice, as a share of a slice.</summary>
    public const double SubOverhang = 0.25;

    /// <summary>Narrowest a slice of a group's ring gets (along its middle), so its label still fits.</summary>
    public const double MinSubWidth = 44;

    /// <summary>
    /// Width of one slice of a group's outer ring: the fan covers the group's slice and a quarter of each neighbour
    /// (<see cref="SubOverhang"/>), split evenly, so it stays close to the slice it belongs to. On a crowded donut it
    /// widens to keep each slice <see cref="MinSubWidth"/> wide at <paramref name="midRadius"/>; it never goes round
    /// more than once (a donut of one slice), where slices would lie on top of each other.
    /// </summary>
    public static double SubSpan(double parentSpan, int count, double midRadius = 0)
    {
        if (count <= 0)
            return 0;
        var span = parentSpan * (1 + 2 * SubOverhang) / count;
        if (midRadius > 0)
            span = Math.Max(span, MinSubWidth / midRadius);
        return Math.Min(span, 2 * Math.PI / count);
    }

    /// <summary>Centre angle of slice <paramref name="sub"/> of a fan of <paramref name="count"/>, centred on <paramref name="parentMid"/>.</summary>
    public static double SubMidAngle(double parentMid, int sub, int count, double subSpan) =>
        parentMid + (sub - (count - 1) / 2.0) * subSpan;

    /// <summary>
    /// Which slice of a group's fan (between <paramref name="innerRadius"/> and <paramref name="outerRadius"/>) holds
    /// <paramref name="p"/>, or -1.
    /// </summary>
    public static int SubIndexAt(Point center, Point p, double innerRadius, double outerRadius, double parentMid, int count, double subSpan)
    {
        var distance = Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
        if (count <= 0 || distance < innerRadius || distance > outerRadius)
            return -1;
        // Signed angle from the fan's centre, wrapped to -π..π so a fan across 12 o'clock works.
        var delta = AngleOf(center, p) - parentMid;
        delta = Math.IEEERemainder(delta, 2 * Math.PI);
        var sub = (int)Math.Floor(delta / subSpan + count / 2.0);
        return sub >= 0 && sub < count ? sub : -1;
    }

    /// <summary>Classifies a point. <paramref name="slack"/> extends the outer radius to include popped-out slices.</summary>
    public static DonutHit HitTest(Point center, Point p, double innerRadius, double outerRadius, int count, double slack = 0)
    {
        var distance = Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
        if (distance < innerRadius)
            return new DonutHit(DonutHitKind.Center);
        if (distance > outerRadius + slack)
            return new DonutHit(DonutHitKind.Outside);
        if (count == 0)
            return new DonutHit(DonutHitKind.EmptyRing);
        return new DonutHit(DonutHitKind.Segment, IndexAt(AngleOf(center, p), count));
    }

    /// <summary>Rounding below this (in pixels) is not visible: the slice stays sharp.</summary>
    public const double MinCornerRadius = 0.25;

    /// <summary>
    /// Outline of a slice of <paramref name="span"/> radians centred on <paramref name="mid"/>, between
    /// <paramref name="innerRadius"/> and <paramref name="outerRadius"/>, with a constant <paramref name="gap"/> to its
    /// neighbours. <paramref name="cornerRadius"/> rounds its four corners (the theme's softness); it shrinks to what fits
    /// a thin or short slice, and 0 gives the sharp slice. The rounding only cuts into the corners, so the slice stays
    /// inside its sharp shape and the gap between slices stays the same. Hit testing keeps the sharp sector: a corner cut
    /// away still belongs to the slice whose angle it is in.
    /// </summary>
    public static WedgeOutline Wedge(Point center, double innerRadius, double outerRadius, double mid, double span, double gap,
        double cornerRadius = 0)
    {
        var outerHalf = Math.Max(0.001, span / 2 - gap / 2 / outerRadius);
        var innerHalf = Math.Max(0.001, span / 2 - gap / 2 / innerRadius);
        var outerStart = PointAt(center, outerRadius, mid - outerHalf);
        var innerStart = PointAt(center, innerRadius, mid - innerHalf);

        var radius = Math.Min(cornerRadius, (outerRadius - innerRadius) / 2);
        if (radius > MinCornerRadius)
        {
            if (Rounded(center, innerRadius, outerRadius, mid, outerHalf, innerHalf, radius) is { } rounded)
                return rounded;
            // Too thin or short for it: the largest rounding that fits, found by halving, so a slice growing in (the
            // edit preview) rounds smoothly instead of in steps.
            WedgeOutline? best = null;
            double low = 0, high = radius;
            for (var step = 0; step < 14; step++)
            {
                var r = (low + high) / 2;
                if (Rounded(center, innerRadius, outerRadius, mid, outerHalf, innerHalf, r) is { } fits)
                    (best, low) = (fits, r);
                else
                    high = r;
            }
            if (best is not null && low > MinCornerRadius)
                return best;
        }

        return new WedgeOutline(outerStart,
        [
            new OutlineStep(PointAt(center, outerRadius, mid + outerHalf), outerRadius, outerHalf * 2 > Math.PI, Clockwise: true),
            new OutlineStep(PointAt(center, innerRadius, mid + innerHalf)),
            new OutlineStep(innerStart, innerRadius, innerHalf * 2 > Math.PI, Clockwise: false),
        ], 0);
    }

    /// <summary>The slice with corners of <paramref name="radius"/>, or null when they don't fit.</summary>
    private static WedgeOutline? Rounded(Point center, double r1, double r2, double mid, double outerHalf, double innerHalf, double radius)
    {
        // The slice is symmetric about its middle: work out the corners on its start side and mirror them.
        var outerSide = PointAt(center, r2, mid - outerHalf);
        var innerSide = PointAt(center, r1, mid - innerHalf);
        if (Corner(center, outerSide, innerSide, r2, radius, outer: true) is not { } o ||
            Corner(center, outerSide, innerSide, r1, radius, outer: false) is not { } i)
            return null;
        // The two corners on a side must not overlap, nor reach past the middle into the other side's.
        if (o.Along >= i.Along || Offset(center, o.Center, mid) >= 0 || Offset(center, i.Center, mid) >= 0)
            return null;

        Point Mirror(Point p) => PointAt(center, Distance(center, p), 2 * mid - AngleOf(center, p));
        var outerArc = -Offset(center, o.Center, mid);
        var innerArc = -Offset(center, i.Center, mid);
        return new WedgeOutline(o.OnArc,
        [
            new OutlineStep(Mirror(o.OnArc), r2, outerArc > Math.PI / 2, Clockwise: true),
            new OutlineStep(Mirror(o.OnLine), radius),
            new OutlineStep(Mirror(i.OnLine)),
            new OutlineStep(Mirror(i.OnArc), radius),
            new OutlineStep(i.OnArc, r1, innerArc > Math.PI / 2, Clockwise: false),
            new OutlineStep(i.OnLine, radius),
            new OutlineStep(o.OnLine),
            new OutlineStep(o.OnArc, radius),
        ], radius);
    }

    /// <summary>
    /// The rounded corner where the slice's start side (the line from <paramref name="outerSide"/> to
    /// <paramref name="innerSide"/>) meets the circle of <paramref name="circle"/>: the centre of its arc, where it touches
    /// the circle and the side, and how far along the side (from <paramref name="outerSide"/>) it touches it.
    /// </summary>
    private static (Point Center, Point OnArc, Point OnLine, double Along)? Corner(Point center, Point outerSide, Point innerSide,
        double circle, double radius, bool outer)
    {
        var length = Distance(outerSide, innerSide);
        if (length <= 0)
            return null;
        var u = new Vector((innerSide.X - outerSide.X) / length, (innerSide.Y - outerSide.Y) / length);
        // Into the slice: the start side's normal on the clockwise (increasing angle) side.
        var tangent = new Vector(Math.Cos(AngleOf(center, outerSide)), Math.Sin(AngleOf(center, outerSide)));
        var n = new Vector(-u.Y, u.X);
        if (n.X * tangent.X + n.Y * tangent.Y < 0)
            n = -n;

        // The arc's centre lies `radius` inside the side, and touches the circle: inside it for the outer edge, outside
        // the hole for the inner one. The nearer of the two crossings is on this side of the middle.
        var distance = outer ? circle - radius : circle + radius;
        var w = outerSide + n * radius - center;
        var b = w.X * u.X + w.Y * u.Y;
        var disc = b * b - (w.X * w.X + w.Y * w.Y - distance * distance);
        if (disc < 0)
            return null;
        var t = -b - Math.Sqrt(disc);
        if (t < 0 || t > length)
            return null;
        var c = outerSide + n * radius + u * t;
        var toCenter = new Vector(c.X - center.X, c.Y - center.Y);
        var onArc = center + toCenter / toCenter.Length * circle;
        return (c, onArc, outerSide + u * t, t);
    }

    /// <summary>Signed angle of <paramref name="p"/> from <paramref name="mid"/>, -π..π (negative before it).</summary>
    private static double Offset(Point center, Point p, double mid) => Math.IEEERemainder(AngleOf(center, p) - mid, 2 * Math.PI);

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
