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

    /// <summary>Cadence of AddValue calls, used to label the hover readout's "-Ns" age.</summary>
    public double SecondsPerSample { get; set; } = 1.5;

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

        // Hover readout: translate pointer X back into a sample index. The
        // badge is informational only and must never throw from a race with
        // layout (ActualWidth can be 0 before the first measure).
        PointerMoved += Graph_PointerMoved;
        PointerExited += (_, _) => HoverBadge.Visibility = Visibility.Collapsed;
    }

    private void Graph_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            var w = PlotCanvas.ActualWidth;
            if (w < 10 || _values.Count == 0)
            {
                HoverBadge.Visibility = Visibility.Collapsed;
                return;
            }

            var x = e.GetCurrentPoint(this).Position.X;
            var step = Capacity > 1 ? w / (Capacity - 1) : w;
            var startX = w - (_values.Count - 1) * step;
            var index = (int)Math.Round((x - startX) / step);
            if (index < 0 || index >= _values.Count)
            {
                HoverBadge.Visibility = Visibility.Collapsed;
                return;
            }

            var age = _values.Count - 1 - index;
            HoverText.Text = _values[index].ToString("F0") + "%"
                + (age == 0 ? " · now" : $" · -{age * SecondsPerSample:0}s");
            HoverBadge.Visibility = Visibility.Visible;
        }
        catch
        {
            HoverBadge.Visibility = Visibility.Collapsed;
        }
    }

    public void AddValue(double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return;
        }

        var previous = _values.Count > 0 ? _values[^1] : double.NaN;
        _values.Add(Math.Clamp(percent, 0, 100));
        if (_values.Count > Capacity)
        {
            _values.RemoveAt(0);
        }

        var target = _values[^1];
        if (double.IsNaN(previous) || Math.Abs(previous - target) < 0.5)
        {
            StopGlide();
            Render();
            return;
        }

        // The newest point glides to its value instead of jumping. Sampling is
        // every 1.5s, so a 320ms ease reads as a live curve moving rather than a
        // chart being redrawn, and it is far shorter than the sample interval.
        StartGlide(previous, target);
    }

    public void Clear()
    {
        StopGlide();
        _values.Clear();
        Render();
    }

    /// <summary>Cadence of an in-progress glide.</summary>
    private const int GlideFrameMilliseconds = 33;

    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(320);

    private bool _gliding;
    private double _glideFrom;
    private double _glideTo;
    private double _glideCurrent;
    private long _glideStarted;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _glideTimer;

    private void StartGlide(double from, double to)
    {
        _gliding = true;
        _glideFrom = from;
        _glideTo = to;
        _glideCurrent = from;
        _glideStarted = Environment.TickCount64;

        _glideTimer ??= CreateGlideTimer();
        _glideTimer.Stop();
        _glideTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateGlideTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(GlideFrameMilliseconds);
        timer.IsRepeating = true;
        timer.Tick += (_, _) => AdvanceGlide();
        return timer;
    }

    private void AdvanceGlide()
    {
        var progress = (Environment.TickCount64 - _glideStarted) / GlideDuration.TotalMilliseconds;
        if (progress >= 1)
        {
            StopGlide();
            Render();
            return;
        }

        // Ease out: the curve reacts immediately, then settles into the value.
        var eased = 1 - Math.Pow(1 - progress, 3);
        _glideCurrent = _glideFrom + ((_glideTo - _glideFrom) * eased);
        Render();
    }

    private void StopGlide()
    {
        _gliding = false;
        _glideTimer?.Stop();
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
            StopGlide();
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
            // The last sample is the one still gliding, if a glide is running.
            var value = _gliding && i == _values.Count - 1 ? _glideCurrent : _values[i];
            pts.Add(new Point(startX + i * step, h - (value / 100.0 * (h - 4)) - 2));
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
