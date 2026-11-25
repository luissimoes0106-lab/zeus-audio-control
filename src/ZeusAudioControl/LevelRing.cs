using System.Windows;
using System.Windows.Media;

namespace ZeusAudioControl;

public sealed class LevelRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(LevelRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Pen Track = MakePen(Color.FromRgb(57, 57, 70));
    private static readonly Pen Fill = MakePen(Color.FromRgb(255, 71, 97));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(center.X, center.Y) - 4);
        drawing.DrawEllipse(null, Track, center, radius, radius);
        var fraction = Math.Clamp(Value, 0, 100) / 100;
        if (fraction >= 1) drawing.DrawEllipse(null, Fill, center, radius, radius);
        else if (fraction > 0)
        {
            var angle = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(start, false, false);
                path.ArcTo(end, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            drawing.DrawGeometry(null, Fill, geometry);
        }
    }

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}

public sealed class GainToPercent : System.Windows.Data.IValueConverter
{
    public static GainToPercent Instance { get; } = new();
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is double gain ? gain * 100 / 24 : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
