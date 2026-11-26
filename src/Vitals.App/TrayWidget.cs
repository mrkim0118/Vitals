using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Vitals.App.Models;

namespace Vitals.App;

// Small opaque panel above the taskbar's corner while the main window is in the tray: CPU, GPU, RAM and C:.
// It never takes focus and has no taskbar button; clicking it opens the main window.
internal sealed class TrayWidget : Form
{
    private static readonly Font LabelFont = new("Segoe UI", 9F, FontStyle.Bold);
    private static readonly Font ValueFont = new("Segoe UI", 9F);
    private static readonly Color CpuColor = Color.FromArgb(96, 165, 250);
    private static readonly Color GpuColor = Color.FromArgb(163, 230, 53);
    private static readonly Color MemoryColor = Color.FromArgb(167, 139, 250);
    private static readonly Color DiskColor = Color.FromArgb(45, 212, 191);

    private (string Label, string Value, double Percent, Color Color)[] _rows =
    [
        ("CPU", "-", 0, CpuColor), ("GPU", "-", 0, GpuColor), ("RAM", "-", 0, MemoryColor), ("C:", "-", 0, DiskColor),
    ];

    public TrayWidget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Cursor = Cursors.Hand;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;       // no taskbar button, no Alt+Tab entry
            const int WS_EX_NOACTIVATE = 0x08000000; // clicking it doesn't steal focus
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowInCorner()
    {
        var s = DeviceDpi / 96f;
        Size = new Size((int)(230 * s), (int)(118 * s));
        // WorkingArea excludes the taskbar, so its bottom-right corner sits just above (or beside) it.
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        var margin = (int)(8 * s);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
        using (var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 10 * s))
        {
            Region = new Region(path);
        }

        if (!Visible)
        {
            Show();
        }
    }

    public void Update(SystemSnapshot snapshot)
    {
        var drive = snapshot.Drives.FirstOrDefault(d => d.Name.Equals("C:", StringComparison.OrdinalIgnoreCase));
        var driveUsed = drive is { TotalBytes: > 0 } ? 100.0 * (drive.TotalBytes - drive.FreeBytes) / drive.TotalBytes : 0;
        _rows =
        [
            ("CPU", $"{snapshot.Cpu.UsagePercent:F0}%", snapshot.Cpu.UsagePercent, CpuColor),
            ("GPU", $"{snapshot.Gpu.UsagePercent:F0}%", snapshot.Gpu.UsagePercent, GpuColor),
            ("RAM", $"{snapshot.Memory.UsagePercent:F0}%", snapshot.Memory.UsagePercent, MemoryColor),
            // C: shows how full the drive is; the bar matches, the active time is in the main window.
            ("C:", drive is null ? "N/A" : $"{driveUsed:F0}% · {FormatGb(drive.FreeBytes)} free", driveUsed, DiskColor),
        ];

        if (Visible)
        {
            Invalidate();
        }
    }

    private static string FormatGb(ulong bytes) => $"{bytes / 1024d / 1024d / 1024d:F0} GB";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = DeviceDpi / 96f;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(Theme.Border, Math.Max(1f, s)))
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10 * s))
        {
            g.DrawPath(border, path);
        }

        var pad = 12 * s;
        var rowHeight = (Height - pad * 2) / _rows.Length;
        var barHeight = Math.Max(3f, 4 * s);
        for (var i = 0; i < _rows.Length; i++)
        {
            var (label, value, percent, color) = _rows[i];
            var top = pad + i * rowHeight;
            var textRect = Rectangle.Round(new RectangleF(pad, top, Width - pad * 2, rowHeight - barHeight - 2 * s));
            TextRenderer.DrawText(g, label, LabelFont, textRect, Theme.TextTitle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, ValueFont, textRect, MetricCard.StatusColor(percent, Theme.Text), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var bar = new RectangleF(pad, textRect.Bottom + s, Width - pad * 2, barHeight);
            using (var track = new SolidBrush(Theme.Track))
            using (var trackPath = Theme.RoundedRect(bar, barHeight / 2))
            {
                g.FillPath(track, trackPath);
            }

            var fill = (float)(bar.Width * Math.Clamp(percent, 0, 100) / 100);
            if (fill >= barHeight)
            {
                using var brush = new SolidBrush(MetricCard.StatusColor(percent, color));
                using var fillPath = Theme.RoundedRect(new RectangleF(bar.X, bar.Y, fill, barHeight), barHeight / 2);
                g.FillPath(brush, fillPath);
            }
        }
    }
}
