using System;
using System.Drawing;
using System.Windows.Forms;

namespace PCMonitoring.App;

public sealed class UpdateDownloadProgressDialog : Form
{
    private readonly Label _messageLabel;
    private readonly Label _percentLabel;
    private readonly ProgressBar _progressBar;

    public UpdateDownloadProgressDialog()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "Downloading Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 120);
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;

        _messageLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 18),
            Text = "Downloading update... 0%"
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 46),
            Size = new Size(320, 18),
            Minimum = 0,
            Maximum = 100
        };

        _percentLabel = new Label
        {
            AutoSize = true,
            Location = new Point(304, 76),
            Text = "0%"
        };

        Controls.Add(_messageLabel);
        Controls.Add(_progressBar);
        Controls.Add(_percentLabel);
    }

    public void ReportProgress(int progress)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<int>(ReportProgress), progress);
            return;
        }

        var clamped = Math.Max(0, Math.Min(100, progress));
        _progressBar.Value = clamped;
        _messageLabel.Text = $"Downloading update... {clamped}%";
        _percentLabel.Text = $"{clamped}%";
    }

    public void CloseSafely()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(CloseSafely));
            return;
        }

        Close();
    }
}
