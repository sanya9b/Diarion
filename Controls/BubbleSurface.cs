using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The surface of a panel that a button expands into, drawn as one shape: a rounded body with a bump
/// that wraps the button, the two joined by smooth concave fillets — the outline flows from around the
/// button into the panel, so the panel reads as the button grown rather than a card laid next to it.
/// <para>
/// <see cref="Progress"/> runs the growth: at 0 the body is a short base just under the button, at 1 it
/// is the full panel. The bump stays put around the button the whole time. Everything is in the
/// coordinates of the layer this view fills; nothing here takes input.
/// </para>
/// </summary>
public sealed class BubbleSurface : GraphicsView, IDrawable
{
    /// <summary>Corner radius of the body, where there is room for it.</summary>
    private const float CornerRadius = 20f;

    /// <summary>Largest fillet between the bump and the body edge.</summary>
    private const float MaxFillet = 10f;

    /// <summary>Arcs are drawn as short chords; this many degrees per chord is smooth at any size here.</summary>
    private const float ArcStepDegrees = 5f;

    public static readonly BindableProperty SurfaceColorProperty = BindableProperty.Create(
        nameof(SurfaceColor), typeof(Color), typeof(BubbleSurface), Colors.White, propertyChanged: Redraw);

    public static readonly BindableProperty StrokeColorProperty = BindableProperty.Create(
        nameof(StrokeColor), typeof(Color), typeof(BubbleSurface), Colors.LightGray, propertyChanged: Redraw);

    public static readonly BindableProperty ShadowColorProperty = BindableProperty.Create(
        nameof(ShadowColor), typeof(Color), typeof(BubbleSurface), Colors.Black, propertyChanged: Redraw);

    private Rect? _bump;
    private Rect _bodyStart;
    private Rect _bodyFull;
    private bool _opensDown = true;
    private double _progress = 1;

    public BubbleSurface()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
    }

    public Color SurfaceColor
    {
        get => (Color)GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    /// <summary>0: a short base under the button. 1: the full panel.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    /// <summary>
    /// Sets the geometry. <paramref name="bump"/> is the button inflated by its margin — a circle when
    /// square, a capsule when wider — or null for a panel with no button to wrap. The body's edge that
    /// meets the bump (top when opening down, bottom when up) must cross the bump's inner half.
    /// </summary>
    public void SetGeometry(Rect? bump, Rect bodyStart, Rect bodyFull, bool opensDown)
    {
        _bump = bump;
        _bodyStart = bodyStart;
        _bodyFull = bodyFull;
        _opensDown = opensDown;
        Invalidate();
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((BubbleSurface)bindable).Invalidate();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (_bodyFull.Width <= 0 || _bodyFull.Height <= 0) return;

        var t = _progress;
        var body = new Rect(
            Lerp(_bodyStart.X, _bodyFull.X, t),
            Lerp(_bodyStart.Y, _bodyFull.Y, t),
            Math.Max(1, Lerp(_bodyStart.Width, _bodyFull.Width, t)),
            Math.Max(1, Lerp(_bodyStart.Height, _bodyFull.Height, t)));

        var path = _bump is Rect bump ? BuildBubble(body, bump, _opensDown) : BuildRoundedRect(body);

        canvas.SaveState();
        canvas.Antialias = true;

        // The lift: two copies of the shape a few points lower, faint, under the surface — not a blurred
        // canvas shadow. On iPhone CoreGraphics blurs on the CPU, and this view covers the page and
        // redraws every frame of the growth, so a 16-point blur made the opening stutter. It also drew
        // differently there: the panel came out a warm peach in the pink theme instead of the card colour
        // it is on Windows (owner's feedback on build 6, 2026-10-21). Plain fills look the same everywhere.
        canvas.FillColor = ShadowColor.WithAlpha(0.05f);
        canvas.Translate(0, 5);
        canvas.FillPath(path);
        canvas.FillColor = ShadowColor.WithAlpha(0.07f);
        canvas.Translate(0, -3);
        canvas.FillPath(path);
        canvas.Translate(0, -2);

        canvas.FillColor = SurfaceColor;
        canvas.FillPath(path);
        canvas.StrokeColor = StrokeColor;
        canvas.StrokeSize = 1;
        canvas.DrawPath(path);
        canvas.RestoreState();
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static PathF BuildRoundedRect(Rect body)
    {
        var path = new PathF();
        path.AppendRoundedRectangle((float)body.X, (float)body.Y, (float)body.Width, (float)body.Height,
            (float)Math.Min(CornerRadius, Math.Min(body.Width, body.Height) / 2));
        return path;
    }

    /// <summary>
    /// The body with the bump on its joining edge. Built as if the panel opens downward (the bump on the
    /// top edge); for an upward panel the inputs are mirrored and the points mirrored back.
    /// </summary>
    private static PathF BuildBubble(Rect body, Rect bump, bool opensDown)
    {
        // Down frame: y as is. Up frame: y negated, so the bottom edge becomes the top edge.
        double M(double y) => opensDown ? y : -y;

        var left = body.Left;
        var right = body.Right;
        var top = opensDown ? body.Top : -body.Bottom;
        var bottom = opensDown ? body.Bottom : -body.Top;

        var rr = bump.Height / 2;
        var cy = M(bump.Center.Y);
        var lx = bump.Left + rr;
        var rx = Math.Max(lx, bump.Right - rr);
        var d = top - cy; // how far below the bump's centre the edge runs; 0 < d < rr

        if (d <= 0 || d >= rr)
        {
            return BuildRoundedRect(body);
        }

        var corner = Math.Min(CornerRadius, Math.Min(body.Width, bottom - top) / 2);

        var leftJoin = Join(lx, cy, rr, top, d, left, towardLeft: true, corner);
        var rightJoin = Join(rx, cy, rr, top, d, right, towardLeft: false, corner);

        var points = new List<Point>();

        // Along the joining edge to the left fillet.
        points.Add(new Point(left + leftJoin.Corner, top));
        points.Add(new Point(leftJoin.EdgeX, top));

        // Left fillet: from straight below its centre round to where it meets the bump.
        if (leftJoin.Fillet > 0)
        {
            Arc(points, leftJoin.FilletCenter, leftJoin.Fillet, 90, leftJoin.FilletEndAngle);
        }

        // Over the bump: left cap to its top, straight across, right cap down.
        Arc(points, new Point(lx, cy), rr, leftJoin.BumpAngle, 270);
        if (rx > lx) points.Add(new Point(rx, cy - rr));
        Arc(points, new Point(rx, cy), rr, 270, rightJoin.BumpAngle + 360);

        // Right fillet: from the bump round to straight below its centre.
        if (rightJoin.Fillet > 0)
        {
            Arc(points, rightJoin.FilletCenter, rightJoin.Fillet, rightJoin.FilletEndAngle, 90);
        }

        points.Add(new Point(rightJoin.EdgeX, top));

        // The rest of the body, clockwise.
        var rc = rightJoin.Corner;
        var lc = leftJoin.Corner;
        points.Add(new Point(right - rc, top));
        if (rc > 0) Arc(points, new Point(right - rc, top + rc), rc, 270, 360);
        Arc(points, new Point(right - corner, bottom - corner), corner, 0, 90);
        Arc(points, new Point(left + corner, bottom - corner), corner, 90, 180);
        if (lc > 0) Arc(points, new Point(left + lc, top + lc), lc, 180, 270);

        var path = new PathF();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var y = (float)M(p.Y);
            if (i == 0) path.MoveTo((float)p.X, y);
            else path.LineTo((float)p.X, y);
        }

        path.Close();
        return path;
    }

    /// <summary>Where the bump meets the joining edge on one side, and how round that meeting is.</summary>
    private readonly record struct JoinGeometry(double EdgeX, double Fillet, Point FilletCenter, double FilletEndAngle, double BumpAngle, double Corner);

    /// <summary>
    /// The fillet on one side: a circle of radius f resting on the edge from inside the shape's outline
    /// and touching the bump's cap from outside. Its size shrinks with the room between the bump and the
    /// body's side, and so does the body's corner on that side, so a button near a card's edge still gets
    /// a clean outline instead of a fillet that runs past the corner.
    /// </summary>
    private static JoinGeometry Join(double capX, double cy, double rr, double edgeY, double d, double sideX, bool towardLeft, double corner)
    {
        var sign = towardLeft ? -1 : 1;

        // Where the edge would cut the cap with no fillet at all.
        var plainX = capX + sign * Math.Sqrt(rr * rr - d * d);
        var room = towardLeft ? plainX - sideX : sideX - plainX;

        var fillet = Math.Clamp(room * 0.45, 0, MaxFillet);
        double edgeX;
        Point center;
        double bumpAngle;
        double filletEnd;

        if (fillet > 0.5)
        {
            var dy = d - fillet;
            var reach = (rr + fillet) * (rr + fillet) - dy * dy;
            var fx = capX + sign * Math.Sqrt(Math.Max(0, reach));
            center = new Point(fx, edgeY - fillet);
            edgeX = fx;

            // The touching point lies on the line between the two centres, at the cap's radius.
            var k = rr / (rr + fillet);
            var touch = new Point(capX + (center.X - capX) * k, cy + (center.Y - cy) * k);
            bumpAngle = Normalize(Deg(Math.Atan2(touch.Y - cy, touch.X - capX)), towardLeft);
            filletEnd = FilletAngle(Deg(Math.Atan2(touch.Y - center.Y, touch.X - center.X)), towardLeft);
        }
        else
        {
            fillet = 0;
            edgeX = plainX;
            center = new Point(plainX, edgeY);
            bumpAngle = Normalize(Deg(Math.Atan2(d, plainX - capX)), towardLeft);
            filletEnd = 90;
        }

        var space = towardLeft ? edgeX - sideX : sideX - edgeX;
        var sideCorner = Math.Clamp(space - 1, 0, corner);
        return new JoinGeometry(edgeX, fillet, center, filletEnd, bumpAngle, sideCorner);
    }

    private static double Deg(double radians) => radians * 180 / Math.PI;

    /// <summary>The left cap's meeting point lies in (90°, 270°); the right cap's in (-90°, 90°).</summary>
    private static double Normalize(double angle, bool left)
    {
        if (left)
        {
            while (angle <= 90) angle += 360;
            while (angle > 270) angle -= 360;
            return angle;
        }

        while (angle <= -90) angle += 360;
        while (angle > 90) angle -= 360;
        return angle;
    }

    /// <summary>On the left fillet the end faces right, (-90°, 90°); on the right fillet it faces left, (90°, 270°).</summary>
    private static double FilletAngle(double angle, bool left)
    {
        if (left)
        {
            while (angle <= -90) angle += 360;
            while (angle > 90) angle -= 360;
            return angle;
        }

        while (angle <= 90) angle += 360;
        while (angle > 270) angle -= 360;
        return angle;
    }

    /// <summary>Appends points along an arc, screen angles (0° right, 90° down), from one angle to the other.</summary>
    private static void Arc(List<Point> points, Point center, double radius, double from, double to)
    {
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(to - from) / ArcStepDegrees));
        for (var i = 0; i <= steps; i++)
        {
            var a = (from + (to - from) * i / steps) * Math.PI / 180;
            points.Add(new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }
    }
}
