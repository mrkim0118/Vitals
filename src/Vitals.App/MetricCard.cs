using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Vitals.App;

// One owner-drawn control per card: a single HWND, no child layout, one repaint per sample.
internal sealed class MetricCard : Control
{
    public const int HistoryLength = 60;


    private static readonly Font TitleFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private static readonly Font ValueFont = new("Segoe UI", 16F, FontStyle.Bold);
    private static readonly Font RowFont = new("Segoe UI", 8.75F);

    private readonly double[] _series =new double[HistoryLength];
    private readonly double[] _series2 = new double[HistoryLength];
    private int _count;
    private int _head;

    private string _value = "-";
    private double? _percent;
    private (string Label, string Value)[] _rows = [];

    public MetricCard(string title, Color accent, bool sparkline = true, Color? accent2 = null, double? sparklineMax = 100)
    {
        Title = title;
        Accent = accent;
        Accent2 = accent2;
        ShowSparkline = sparkline;
        SparklineMax = sparklineMax;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 10, 10);
    }

    public string Title { get; }

    public Color Accent { get; }

    public Color? Accent2 { get; }

    public bool ShowSparkline { get; }

    // null = scale to the largest value in view (for rates such as network).
    public double? SparklineMax { get; }

    // percent drives the bar and the warning colour; null hides the bar.
    public void SetContent(string value, double? percent, params (string Label, string Value)[] rows)
    {
        _value = value;
        _percent = percent;
        _rows = rows;
        Invalidate();
    }

    public void Push(double value, double value2 = 0)
    {
        _series[_head] = value;
        _series2[_head] = value2;
        _head = (_head + 1) % HistoryLength;
        _count = Math.Min(_count + 1, HistoryLength);
    }

    public static Color StatusColor(double percent, Color normal) =>
        percent >= 85 ? Theme.Critical : percent >= 60 ? Theme.Warn : normal;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintCard(this, g, Theme.Surface);
        var s = DeviceDpi / 96f;
        var pad = (int)(12 * s);
        var x = pad;
        var width = Width - pad * 2;
        var y = (int)(10 * s);

        TextRenderer.DrawText(g, Title, TitleFont, new Point(x, y), Theme.TextTitle, TextFormatFlags.NoPadding);
        y += (int)(20 * s);

        var valueColor = _percent.HasValue ? StatusColor(_percent.Value, Color.White) : Color.White;
        TextRenderer.DrawText(g, _value, ValueFont, new Point(x - (int)(2 * s), y), valueColor, TextFormatFlags.NoPadding);
        y += (int)(32 * s);

        if (_percent.HasValue)
        {
            var barHeight = Math.Max(4, (int)(6 * s));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var trackPath = Theme.RoundedRect(new RectangleF(x, y, width, barHeight), barHeight / 2f))
            using (var track = new SolidBrush(Theme.Track))
            {
                g.FillPath(track, trackPath);
            }

            var fill = (float)(width * Math.Clamp(_percent.Value, 0, 100) / 100);
            if (fill >= barHeight)
            {
                using var barPath = Theme.RoundedRect(new RectangleF(x, y, fill, barHeight), barHeight / 2f);
                using var bar = new SolidBrush(StatusColor(_percent.Value, Accent));
                g.FillPath(bar, barPath);
            }

            g.SmoothingMode = SmoothingMode.None;
        }

        y += (int)(12 * s);

        // Label left, value right-aligned: changing digit counts don't shift the text.
        var rowHeight = (int)(17 * s);
        foreach (var (label, value) in _rows)
        {
            var rect = new Rectangle(x, y, width, rowHeight);
            TextRenderer.DrawText(g, label, RowFont, rect, Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, RowFont, rect, Color.White, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += rowHeight;
        }

        var graphTop = y + (int)(6 * s);
        var graphHeight = Height - graphTop - (int)(10 * s);
        if (ShowSparkline && _count > 1 && graphHeight > 8 * s)
        {
            DrawSparkline(g, new Rectangle(x, graphTop, width, graphHeight), s);
        }
    }

    private void DrawSparkline(Graphics g, Rectangle area, float s)
    {
        var max = SparklineMax ?? 0;
        if (SparklineMax is null)
        {
            for (var i = 0; i < _count; i++)
            {
                max = Math.Max(max, Math.Max(_series[i], _series2[i]));
            }

            max = Math.Max(max, 1024); // keep an idle link flat instead of amplifying noise
        }

        using (var baseline = new Pen(Theme.Track))
        {
            g.DrawLine(baseline, area.Left, area.Bottom, area.Right, area.Bottom);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Accent2.HasValue)
        {
            DrawLine(g, area, _series2, max, Accent2.Value, s);
        }

        DrawLine(g, area, _series, max, Accent, s);
        g.SmoothingMode = SmoothingMode.None;
    }

    private void DrawLine(Graphics g, Rectangle area, double[] series, double max, Color color, float s)
    {
        var points = new PointF[_count];
        var step = area.Width / (float)(HistoryLength - 1);
        var start = (_head - _count + HistoryLength) % HistoryLength;
        var offset = HistoryLength - _count; // newest point sits on the right edge
        for (var i = 0; i < _count; i++)
        {
            var v = Math.Clamp(series[(start + i) % HistoryLength] / max, 0, 1);
            points[i] = new PointF(area.Left + (offset + i) * step, area.Bottom - (float)(v * area.Height));
        }

        // Soft fill under the line so the trend reads at a glance.
        var area2 = new PointF[_count + 2];
        points.CopyTo(area2, 0);
        area2[_count] = new PointF(points[^1].X, area.Bottom);
        area2[_count + 1] = new PointF(points[0].X, area.Bottom);
        using (var fill = new LinearGradientBrush(
                   new RectangleF(area.Left, area.Top - 1, area.Width, area.Height + 2),
                   Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical))
        {
            g.FillPolygon(fill, area2);
        }

        using var pen = new Pen(color, Math.Max(1f, 1.5f * s));
        g.DrawLines(pen, points);
    }
}
