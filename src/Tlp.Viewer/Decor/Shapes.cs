using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;

namespace Tlp.Viewer.Decor;

// Small drawing helpers the looks in Themes/ build with. They only draw; none of them holds state
// the app relies on, so a look can use them or not.

/// <summary>A box with cut corners (the angled panels of a sci-fi HUD). Cuts keep their size at any box size.</summary>
public class Chamfer : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<Chamfer, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = AvaloniaProperty.Register<Chamfer, IBrush?>(nameof(BorderBrush));
    public static readonly StyledProperty<double> BorderThicknessProperty = AvaloniaProperty.Register<Chamfer, double>(nameof(BorderThickness), 1);
    /// <summary>Cut size per corner: top-left, top-right, bottom-right, bottom-left.</summary>
    public static readonly StyledProperty<Thickness> CutsProperty = AvaloniaProperty.Register<Chamfer, Thickness>(nameof(Cuts), new Thickness(0, 12, 0, 12));

    static Chamfer() => AffectsRender<Chamfer>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CutsProperty);

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public Thickness Cuts { get => GetValue(CutsProperty); set => SetValue(CutsProperty, value); }

    public override void Render(DrawingContext context)
    {
        var half = BorderThickness / 2;
        var r = new Rect(Bounds.Size).Deflate(half);
        if (r.Width <= 0 || r.Height <= 0) return;
        var max = Math.Min(r.Width, r.Height) / 2;
        double tl = Math.Min(Cuts.Left, max), tr = Math.Min(Cuts.Top, max), br = Math.Min(Cuts.Right, max), bl = Math.Min(Cuts.Bottom, max);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + tl, r.Top), true);
            c.LineTo(new Point(r.Right - tr, r.Top));
            c.LineTo(new Point(r.Right, r.Top + tr));
            c.LineTo(new Point(r.Right, r.Bottom - br));
            c.LineTo(new Point(r.Right - br, r.Bottom));
            c.LineTo(new Point(r.Left + bl, r.Bottom));
            c.LineTo(new Point(r.Left, r.Bottom - bl));
            c.LineTo(new Point(r.Left, r.Top + tl));
            c.EndFigure(true);
        }
        context.DrawGeometry(Background, BorderBrush is null || BorderThickness <= 0 ? null : new Pen(BorderBrush, BorderThickness), g);
    }
}

/// <summary>A ticket: a box with a half-circle notch out of each side.</summary>
public class Ticket : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<Ticket, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = AvaloniaProperty.Register<Ticket, IBrush?>(nameof(BorderBrush));
    public static readonly StyledProperty<double> NotchProperty = AvaloniaProperty.Register<Ticket, double>(nameof(Notch), 7);

    static Ticket() => AffectsRender<Ticket>(BackgroundProperty, BorderBrushProperty, NotchProperty);

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double Notch { get => GetValue(NotchProperty); set => SetValue(NotchProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size).Deflate(0.5);
        var n = Math.Min(Notch, r.Height / 2);
        var mid = r.Center.Y;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(r.TopLeft, true);
            c.LineTo(r.TopRight);
            c.LineTo(new Point(r.Right, mid - n));
            c.ArcTo(new Point(r.Right, mid + n), new Size(n, n), 0, false, SweepDirection.CounterClockwise);
            c.LineTo(r.BottomRight);
            c.LineTo(r.BottomLeft);
            c.LineTo(new Point(r.Left, mid + n));
            c.ArcTo(new Point(r.Left, mid - n), new Size(n, n), 0, false, SweepDirection.CounterClockwise);
            c.EndFigure(true);
        }
        context.DrawGeometry(Background, BorderBrush is null ? null : new Pen(BorderBrush, 1), g);
    }
}

/// <summary>Corner marks: an L at each corner of the box, like a viewfinder or a drawing's crop marks.</summary>
public class CornerMarks : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<CornerMarks, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> LengthProperty = AvaloniaProperty.Register<CornerMarks, double>(nameof(Length), 12);
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<CornerMarks, double>(nameof(Thickness), 2);

    static CornerMarks() => AffectsRender<CornerMarks>(StrokeProperty, LengthProperty, ThicknessProperty);

    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Length { get => GetValue(LengthProperty); set => SetValue(LengthProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null) return;
        var t = Thickness;
        var r = new Rect(Bounds.Size);
        var l = Math.Min(Length, Math.Min(r.Width, r.Height) / 2);
        foreach (var (x, y, dx, dy) in new[] { (r.Left, r.Top, 1, 1), (r.Right, r.Top, -1, 1), (r.Right, r.Bottom, -1, -1), (r.Left, r.Bottom, 1, -1) })
        {
            context.FillRectangle(Stroke, Norm(x, y, dx * l, dy * t));
            context.FillRectangle(Stroke, Norm(x, y, dx * t, dy * l));
        }
    }

    private static Rect Norm(double x, double y, double w, double h) =>
        new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h));
}

/// <summary>
/// Covers the window's corners outside a rounded screen, so the content reads as a curved tube
/// behind a bezel. Draws the bezel colour outside a rounded rectangle and a soft inner rim.
/// </summary>
public class Bezel : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Bezel, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> RimProperty = AvaloniaProperty.Register<Bezel, IBrush?>(nameof(Rim));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<Bezel, double>(nameof(Radius), 28);
    public static readonly StyledProperty<double> InsetProperty = AvaloniaProperty.Register<Bezel, double>(nameof(Inset), 4);

    static Bezel() => AffectsRender<Bezel>(FillProperty, RimProperty, RadiusProperty, InsetProperty);

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Rim { get => GetValue(RimProperty); set => SetValue(RimProperty, value); }
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Inset { get => GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    public override void Render(DrawingContext context)
    {
        var outer = new Rect(Bounds.Size);
        var screen = outer.Deflate(Inset);
        if (screen.Width <= 0 || screen.Height <= 0) return;
        var rounded = new RoundedRect(screen, Radius);
        if (Fill is not null)
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(outer));
            g.Children.Add(new RectangleGeometry(screen, Radius, Radius));
            context.DrawGeometry(Fill, null, g);
        }
        if (Rim is not null)
            context.DrawRectangle(null, new Pen(Rim, 2), rounded);
    }
}

/// <summary>Shows the local time (and optionally the date), refreshed every few seconds.</summary>
public class Clock : TextBlock
{
    public static readonly StyledProperty<string> FormatProperty = AvaloniaProperty.Register<Clock, string>(nameof(Format), "HH:mm");
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    /// <summary>Upper-cases the text, for looks that set everything in capitals.</summary>
    public static readonly StyledProperty<bool> UpperProperty = AvaloniaProperty.Register<Clock, bool>(nameof(Upper));
    public bool Upper { get => GetValue(UpperProperty); set => SetValue(UpperProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    private DispatcherTimer? _timer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FormatProperty || change.Property == UpperProperty) Tick();
    }

    private void Tick()
    {
        var text = DateTime.Now.ToString(Format, CultureInfo.CurrentCulture);
        Text = Upper ? text.ToUpper(CultureInfo.CurrentCulture) : text;
    }
}

/// <summary>
/// Upper-cases text content; anything else passes through unchanged. A parameter such as
/// "[ {0} ]" wraps the text too.
/// </summary>
public sealed class UpperConverter : IValueConverter
{
    public static readonly UpperConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? (parameter is string format ? string.Format(culture, format, s.ToUpper(culture)) : s.ToUpper(culture)) : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
