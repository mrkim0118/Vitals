using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Vitals.App;

// The app's colours in one place; values are the ones the UI already used.
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 23, 38);
    public static readonly Color Header = Color.FromArgb(22, 37, 58);
    public static readonly Color Surface = Color.FromArgb(27, 38, 59);
    public static readonly Color SurfaceRaised = Color.FromArgb(33, 49, 74);
    public static readonly Color Control = Color.FromArgb(36, 59, 83);
    public static readonly Color ControlHover = Color.FromArgb(45, 69, 96);
    public static readonly Color Border = Color.FromArgb(44, 60, 86);
    public static readonly Color Track = Color.FromArgb(40, 56, 82);

    public static readonly Color Text = Color.White;
    public static readonly Color TextTitle = Color.FromArgb(224, 225, 221);
    public static readonly Color TextMuted = Color.FromArgb(159, 179, 200);
    public static readonly Color TextDisabled = Color.FromArgb(110, 128, 150);

    public static readonly Color Accent = Color.FromArgb(127, 209, 185);
    public static readonly Color AccentText = Color.FromArgb(10, 30, 26);
    public static readonly Color Warn = Color.FromArgb(251, 146, 60);
    public static readonly Color Critical = Color.FromArgb(248, 113, 113);

    public const int Radius = 8;

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Paints a rounded card on the parent's colour so the corners blend in.
    public static void PaintCard(Control control, Graphics g, Color fill)
    {
        g.Clear(control.Parent?.BackColor ?? Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = control.DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, control.Width - 1.5f, control.Height - 1.5f);
        using var path = RoundedRect(rect, Radius * s);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Border);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.None;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Dark title bar to match the window (Windows 10 2004+; caption colour needs Windows 11, ignored elsewhere).
    public static void ApplyDarkTitleBar(Form form)
    {
        var on = 1;
        DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        var caption = ColorTranslator.ToWin32(Header);
        DwmSetWindowAttribute(form.Handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
    }
}

// Panel drawn as a rounded card; children sit inside its padding.
internal sealed class CardPanel : Panel
{
    public CardPanel(Color? fill = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = fill ?? Theme.Surface;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintCard(this, e.Graphics, BackColor);
}

// Flat rounded button with readable hover, pressed and disabled states.
internal sealed class ThemedButton : Button
{
    private bool _hover;
    private bool _pressed;

    public ThemedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        Margin = new Padding(3);
    }

    // Primary = the main action in its group (accent fill).
    public bool Primary { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        Color fill, text, border;
        if (!Enabled)
        {
            fill = Theme.Surface;
            text = Theme.TextDisabled;
            border = Theme.Border;
        }
        else if (Primary)
        {
            fill = _pressed ? ControlPaint.Dark(Theme.Accent, 0.1f) : _hover ? ControlPaint.Light(Theme.Accent, 0.3f) : Theme.Accent;
            text = Theme.AccentText;
            border = fill;
        }
        else
        {
            fill = _pressed ? Theme.SurfaceRaised : _hover ? Theme.ControlHover : Theme.Control;
            text = Theme.Text;
            border = _hover ? Theme.TextMuted : Theme.Border;
        }

        var s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6 * s))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var focus = ClientRectangle;
            focus.Inflate(-3, -3);
            ControlPaint.DrawFocusRectangle(g, focus, text, fill);
        }
    }
}
