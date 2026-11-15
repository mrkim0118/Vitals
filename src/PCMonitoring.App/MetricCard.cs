using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PCMonitoring.App;

// One owner-drawn control per card: a single HWND, no child layout, one repaint per sample.
internal sealed class MetricCard : Control
{
    public const int HistoryLength = 60;

    private static readonly Color CardBack = Color.FromArgb(27, 38, 59);
    private static readonly Color TitleColor = Color.FromArgb(224, 225, 221);
    private static readonly Color MutedColor = Color.FromArgb(159, 179, 200);
    private static readonly Color TrackColor = Color.FromArgb(40, 56, 82);
    private static readonly Color WarnColor = Color.FromArgb(251, 146, 60);
    private static readonly Color CriticalColor = Color.FromArgb(248, 113, 113);

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
        BackColor = CardBack;
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
        percent >= 85 ? CriticalColor : percent >= 60 ? WarnColor : normal;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = DeviceDpi / 96f;
        var pad = (int)(12 * s);
        var x = pad;
        var width = Width - pad * 2;
        var y = (int)(10 * s);

        TextRenderer.DrawText(g, Title, TitleFont, new Point(x, y), TitleColor, TextFormatFlags.NoPadding);
        y += (int)(20 * s);

        var valueColor = _percent.HasValue ? StatusColor(_percent.Value, Color.White) : Color.White;
        TextRenderer.DrawText(g, _value, ValueFont, new Point(x - (int)(2 * s), y), valueColor, TextFormatFlags.NoPadding);
        y += (int)(32 * s);

        if (_percent.HasValue)
        {
            var barHeight = Math.Max(4, (int)(6 * s));
            using var track = new SolidBrush(TrackColor);
            g.FillRectangle(track, x, y, width, barHeight);
            var fill = (int)(width * Math.Clamp(_percent.Value, 0, 100) / 100);
            if (fill > 0)
            {
                using var bar = new SolidBrush(StatusColor(_percent.Value, Accent));
                g.FillRectangle(bar, x, y, fill, barHeight);
            }
        }

        y += (int)(12 * s);

        // Label left, value right-aligned: changing digit counts don't shift the text.
        var rowHeight = (int)(17 * s);
        foreach (var (label, value) in _rows)
        {
            var rect = new Rectangle(x, y, width, rowHeight);
            TextRenderer.DrawText(g, label, RowFont, rect, MutedColor, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, RowFont, rect, Color.White, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += rowHeight;
        }

        var graphTop = y + (int)(4 * s);
        var graphHeight = Height - graphTop - (int)(8 * s);
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

        using var pen = new Pen(color, Math.Max(1f, 1.5f * s));
        g.DrawLines(pen, points);
    }
}
