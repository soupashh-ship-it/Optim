using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Optim.App.Views;

/// <summary>
/// Lightweight right-aligned rolling line chart with gradient-free area fill.
/// Original implementation: values 0-100 rendered as a scrolling curve.
/// </summary>
public sealed partial class UsageGraph : UserControl
{
    private readonly List<double> _values = new();
    private readonly Polyline _line = new();
    private readonly Polygon _fill = new();

    public int Capacity { get; set; } = 60;

    public UsageGraph()
    {
        InitializeComponent();

        var accent = TryGetColor("SystemAccentColor", Windows.UI.Color.FromArgb(255, 0, 120, 212));
        _line.Stroke = new SolidColorBrush(accent);
        _line.StrokeThickness = 2;
        _line.StrokeLineJoin = PenLineJoin.Round;
        _line.StrokeStartLineCap = PenLineCap.Round;
        _line.StrokeEndLineCap = PenLineCap.Round;
        _fill.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(38, accent.R, accent.G, accent.B));

        PlotCanvas.Children.Add(_fill);
        PlotCanvas.Children.Add(_line);

        SizeChanged += (_, _) => { _gridDirty = true; Render(); };
    }

    public void AddValue(double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return;
        }

        _values.Add(Math.Clamp(percent, 0, 100));
        if (_values.Count > Capacity)
        {
            _values.RemoveAt(0);
        }
        Render();
    }

    public void Clear()
    {
        _values.Clear();
        Render();
    }

    /// <summary>
    /// Drops all plotted samples because sampling became unavailable (counter
    /// failure mid-session). Keeps rendering old points here would present
    /// stale data as current; cheap no-op when the chart is already empty.
    /// </summary>
    public void MarkUnavailable()
    {
        if (_values.Count > 0)
        {
            _values.Clear();
            Render();
        }
    }

    private bool _gridDirty = true;

    private void Render()
    {
        var w = PlotCanvas.ActualWidth;
        var h = PlotCanvas.ActualHeight;
        if (w < 10 || h < 10)
        {
            return;
        }

        // Grid lines only change with size; rebuilding them every sample
        // (3 graphs × every tick) wastes layout passes for nothing.
        if (_gridDirty)
        {
            GridCanvas.Children.Clear();
            foreach (var fy in new[] { 0.25, 0.5, 0.75 })
            {
                GridCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = h * fy,
                    X2 = w,
                    Y2 = h * fy,
                    Stroke = TryGetBrush("DividerStrokeColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.Gray)),
                    StrokeThickness = 1
                });
            }

            _gridDirty = false;
        }

        if (_values.Count == 0)
        {
            _line.Points = new PointCollection();
            _fill.Points = new PointCollection();
            return;
        }

        var step = Capacity > 1 ? w / (Capacity - 1) : w;
        var startX = w - (_values.Count - 1) * step;

        var pts = new List<Point>(_values.Count);
        for (var i = 0; i < _values.Count; i++)
        {
            pts.Add(new Point(startX + i * step, h - (_values[i] / 100.0 * (h - 4)) - 2));
        }

        var linePoints = new PointCollection();
        foreach (var p in pts)
        {
            linePoints.Add(p);
        }
        _line.Points = linePoints;

        var fillPts = new List<Point>(pts.Count + 2) { new(pts[0].X, h) };
        fillPts.AddRange(pts);
        fillPts.Add(new Point(pts[^1].X, h));

        var fillPoints = new PointCollection();
        foreach (var p in fillPts)
        {
            fillPoints.Add(p);
        }
        _fill.Points = fillPoints;
    }

    private static Windows.UI.Color TryGetColor(string key, Windows.UI.Color fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var v) && v is Windows.UI.Color c)
            {
                return c;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static Brush TryGetBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b)
            {
                return b;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
