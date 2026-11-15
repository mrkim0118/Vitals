using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using PCMonitoring.App.Models;
using PCMonitoring.App.Services;
using Velopack;

namespace PCMonitoring.App;

public sealed class MainForm : Form
{
    private const int ProcessPidColumnWidth = 70;
    private const int ProcessCpuColumnWidth = 90;
    private const int ProcessMemoryColumnWidth = 110;

    private readonly MonitorRuntime _monitorRuntime = new();
    private readonly ListView _processList = new();
    private readonly TableLayoutPanel _processHeader = new();
    private readonly TextBox _durationTextBox = new();
    private readonly TextBox _logPathTextBox = new();
    private readonly Label _statusLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _loggingLabel = new();
    private readonly Button _startLoggingButton = new();
    private readonly Button _stopLoggingButton = new();
    private readonly Button _browseLogPathButton = new();
    private readonly Button _openLogPathButton = new();

    private readonly Label _cpuPrimary = new();
    private readonly Label _cpuSecondary = new();
    private readonly ProgressBar _cpuBar = new();

    private readonly Label _memoryPrimary = new();
    private readonly Label _memorySecondary = new();
    private readonly ProgressBar _memoryBar = new();

    private readonly Label _diskPrimary = new();
    private readonly Label _diskSecondary = new();
    private readonly ProgressBar _diskBar = new();

    private readonly Label _gpuPrimary = new();
    private readonly Label _gpuSecondary = new();
    private readonly ProgressBar _gpuBar = new();

    private readonly Label _networkPrimary = new();
    private readonly Label _networkSecondary = new();

    private readonly Label _loggerPrimary = new();
    private readonly Label _loggerSummary = new();

    private readonly AppSettings _settings;
    private readonly string _appVersionText;
    private LoggingSession? _loggingSession;
    private Task? _readerTask;
    private bool _isCheckingForUpdates;
    private bool _isClosing;
    private bool _startupUpdateCheckPending = true;

    public MainForm()
    {
        _settings = AppSettingsStore.Load();
        _appVersionText = GetDisplayVersion();

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = $"TechArchive PC Monitoring v{_appVersionText}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 650);
        Size = new Size(960, 700);
        BackColor = Color.FromArgb(14, 23, 38);
        ForeColor = Color.White;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildLayout();

        Load += OnLoad;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 37, 58),
            Padding = new Padding(14, 10, 14, 10),
        };
        root.Controls.Add(header, 0, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 200,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = header.BackColor,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };
        header.Controls.Add(actionPanel);

        var title = new Label
        {
            Text = $"TechArchive PC Monitoring v{_appVersionText}",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(10, 10),
        };
        header.Controls.Add(title);

        var donateButton = CreateHeaderButton("Donate", "https://www.paypal.com/ncp/payment/T2KAST2XQC6BS");
        var homepageButton = CreateHeaderButton("Homepage", "https://techarchive.click/");
        actionPanel.Controls.Add(donateButton);
        actionPanel.Controls.Add(homepageButton);

        _updatedLabel.AutoSize = true;
        _updatedLabel.ForeColor = Color.FromArgb(220, 226, 233);
        _updatedLabel.Text = "Updated: -";
        _updatedLabel.Location = new Point(450, 10);
        _updatedLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_updatedLabel);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(127, 209, 185);
        _statusLabel.Text = "Idle";
        _statusLabel.Location = new Point(450, 30);
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_statusLabel);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 10),
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        root.Controls.Add(metrics, 0, 1);

        metrics.Controls.Add(CreateMetricCard("CPU", _cpuPrimary, _cpuSecondary, _cpuBar), 0, 0);
        metrics.Controls.Add(CreateMetricCard("Memory", _memoryPrimary, _memorySecondary, _memoryBar), 1, 0);
        metrics.Controls.Add(CreateMetricCard("Disk", _diskPrimary, _diskSecondary, _diskBar), 2, 0);
        metrics.Controls.Add(CreateMetricCard("GPU", _gpuPrimary, _gpuSecondary, _gpuBar), 0, 1);
        metrics.Controls.Add(CreateMetricCard("Network", _networkPrimary, _networkSecondary, null), 1, 1);
        metrics.Controls.Add(CreateLogStatusCard(), 2, 1);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ColumnCount = 2,
            RowCount = 1,
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        root.Controls.Add(bottom, 0, 2);

        var processPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(27, 38, 59),
            Padding = new Padding(10),
        };
        processPanel.Margin = new Padding(0, 0, 8, 0);
        bottom.Controls.Add(processPanel, 0, 0);

        var processTitle = new Label
        {
            Text = "Top Processes",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            ForeColor = Color.White,
        };
        var processLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = processPanel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        processLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        processLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        processPanel.Controls.Add(processLayout);
        processPanel.Controls.Add(processTitle);

        _processHeader.Dock = DockStyle.Fill;
        _processHeader.ColumnCount = 4;
        _processHeader.RowCount = 1;
        _processHeader.BackColor = Color.FromArgb(33, 49, 74);
        _processHeader.Margin = new Padding(0, 0, 0, 6);
        _processHeader.Padding = new Padding(8, 0, 8, 0);
        _processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProcessPidColumnWidth));
        _processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        _processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProcessCpuColumnWidth));
        _processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProcessMemoryColumnWidth));
        _processHeader.Controls.Add(CreateProcessHeaderLabel("PID", ContentAlignment.MiddleLeft), 0, 0);
        _processHeader.Controls.Add(CreateProcessHeaderLabel("Process", ContentAlignment.MiddleLeft), 1, 0);
        _processHeader.Controls.Add(CreateProcessHeaderLabel("CPU %", ContentAlignment.MiddleRight), 2, 0);
        _processHeader.Controls.Add(CreateProcessHeaderLabel("Memory", ContentAlignment.MiddleRight), 3, 0);
        processLayout.Controls.Add(_processHeader, 0, 0);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.HeaderStyle = ColumnHeaderStyle.None;
        _processList.MultiSelect = false;
        _processList.HideSelection = false;
        _processList.BorderStyle = BorderStyle.None;
        _processList.BackColor = Color.FromArgb(15, 39, 67);
        _processList.ForeColor = Color.White;
        _processList.Columns.Add("PID", 70, HorizontalAlignment.Left);
        _processList.Columns.Add("Process", 220, HorizontalAlignment.Left);
        _processList.Columns.Add("CPU %", 90, HorizontalAlignment.Right);
        _processList.Columns.Add("Memory", 110, HorizontalAlignment.Right);
        _processList.SizeChanged += (_, _) => UpdateProcessColumnLayout();
        processLayout.Controls.Add(_processList, 0, 1);

        for (var i = 0; i < 5; i++)
        {
            _processList.Items.Add(new ListViewItem(["-", "-", "-", "-"]));
        }

        UpdateProcessColumnLayout();

        var sidePanel = CreateSectionPanel("Controls");
        sidePanel.Margin = new Padding(8, 0, 0, 0);
        bottom.Controls.Add(sidePanel, 1, 0);

        var sideLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            BackColor = sidePanel.BackColor,
            Padding = new Padding(0),
        };
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        sidePanel.Controls.Add(sideLayout);

        sideLayout.Controls.Add(new Label
        {
            Text = "Time Setting",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(224, 225, 221),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 0);

        sideLayout.Controls.Add(new Label
        {
            Text = "Duration (hh:mm:ss)",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(224, 225, 221),
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 1);

        _durationTextBox.Dock = DockStyle.Fill;
        _durationTextBox.Text = "00:10:00";
        _durationTextBox.BorderStyle = BorderStyle.FixedSingle;
        _durationTextBox.BackColor = Color.FromArgb(36, 59, 83);
        _durationTextBox.ForeColor = Color.White;
        _durationTextBox.TextChanged += (_, _) => UpdateLogStatusDisplay();
        sideLayout.Controls.Add(_durationTextBox, 0, 2);

        sideLayout.Controls.Add(new Label
        {
            Text = "Log folder",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(224, 225, 221),
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 4);

        _logPathTextBox.Dock = DockStyle.Fill;
        _logPathTextBox.BorderStyle = BorderStyle.FixedSingle;
        _logPathTextBox.BackColor = Color.FromArgb(36, 59, 83);
        _logPathTextBox.ForeColor = Color.White;
        _logPathTextBox.Text = _settings.LogRootPath;
        sideLayout.Controls.Add(_logPathTextBox, 0, 5);

        var pathButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = sidePanel.BackColor,
            Margin = new Padding(0),
        };
        pathButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pathButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        sideLayout.Controls.Add(pathButtons, 0, 6);

        _browseLogPathButton.Text = "Browse";
        _browseLogPathButton.Dock = DockStyle.Fill;
        _browseLogPathButton.Click += (_, _) => BrowseLogPath();
        pathButtons.Controls.Add(_browseLogPathButton, 0, 0);

        _openLogPathButton.Text = "Open Folder";
        _openLogPathButton.Dock = DockStyle.Fill;
        _openLogPathButton.Click += (_, _) => OpenLogPath();
        pathButtons.Controls.Add(_openLogPathButton, 1, 0);

        _startLoggingButton.Text = "Start Log";
        _startLoggingButton.Dock = DockStyle.Fill;
        _startLoggingButton.Click += (_, _) => StartLogging();
        sideLayout.Controls.Add(_startLoggingButton, 0, 8);

        _stopLoggingButton.Text = "Stop Log";
        _stopLoggingButton.Dock = DockStyle.Fill;
        _stopLoggingButton.Enabled = false;
        _stopLoggingButton.Click += (_, _) => StopLogging();
        sideLayout.Controls.Add(_stopLoggingButton, 0, 9);

        _loggingLabel.Dock = DockStyle.Fill;
        _loggingLabel.ForeColor = Color.FromArgb(159, 179, 200);
        _loggingLabel.Text = "Stop Log clears the current session without saving.";
        _loggingLabel.TextAlign = ContentAlignment.TopLeft;
        sideLayout.Controls.Add(_loggingLabel, 0, 10);

        _cpuPrimary.Text = "0.0%";
        _cpuSecondary.Text = "Clock N/A";
        _memoryPrimary.Text = "0.0%";
        _memorySecondary.Text = "0 B / 0 B";
        _diskPrimary.Text = "0.0%";
        _diskSecondary.Text = "Free 0 B | R 0 B/s | W 0 B/s";
        _gpuPrimary.Text = "0.0%";
        _gpuSecondary.Text = "VRAM 0 B | Temp N/A";
        _networkPrimary.Text = "RX 0 B/s | TX 0 B/s";
        _networkSecondary.Text = "Total RX 0 B | Total TX 0 B";

        UpdateLogStatusDisplay();
        ResumeLayout();
    }

    private Panel CreateSectionPanel(string title)
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(27, 38, 59),
            Padding = new Padding(10),
        };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            ForeColor = Color.White,
        };
        container.Controls.Add(titleLabel);
        return container;
    }

    private Control CreateMetricCard(string title, Label primary, Label secondary, ProgressBar? progressBar)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(27, 38, 59),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Height = 20,
            ForeColor = Color.FromArgb(224, 225, 221),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
        };
        layout.Controls.Add(titleLabel, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(content, 0, 1);

        primary.Dock = DockStyle.Fill;
        primary.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
        primary.ForeColor = Color.White;
        primary.Padding = new Padding(0, 6, 0, 0);
        content.Controls.Add(primary, 0, 0);

        if (progressBar is not null)
        {
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = 12;
            progressBar.Maximum = 100;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Margin = new Padding(0, 2, 0, 2);
            content.Controls.Add(progressBar, 0, 1);
        }
        else
        {
            var spacer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = panel.BackColor,
                Margin = new Padding(0),
            };
            content.Controls.Add(spacer, 0, 1);
        }

        secondary.Dock = DockStyle.Fill;
        secondary.AutoSize = false;
        secondary.AutoEllipsis = false;
        secondary.Font = new Font(Font.FontFamily, 8.75F, FontStyle.Regular);
        secondary.ForeColor = Color.FromArgb(159, 179, 200);
        secondary.TextAlign = ContentAlignment.TopLeft;
        secondary.Padding = new Padding(0, 2, 0, 0);
        content.Controls.Add(secondary, 0, 2);

        var filler = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
        };
        content.Controls.Add(filler, 0, 3);

        return panel;
    }

    private Control CreateLogStatusCard()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(27, 38, 59),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        var titleLabel = new Label
        {
            Text = "Log",
            Dock = DockStyle.Fill,
            Height = 20,
            ForeColor = Color.FromArgb(224, 225, 221),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
        };
        layout.Controls.Add(titleLabel, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(content, 0, 1);

        _loggerPrimary.Dock = DockStyle.Fill;
        _loggerPrimary.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
        _loggerPrimary.ForeColor = Color.White;
        _loggerPrimary.Padding = new Padding(0, 6, 0, 0);
        content.Controls.Add(_loggerPrimary, 0, 0);

        var spacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
        };
        content.Controls.Add(spacer, 0, 1);

        ConfigureLogStatLabel(_loggerSummary);
        _loggerSummary.Margin = new Padding(0);
        content.Controls.Add(_loggerSummary, 0, 2);

        var filler = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = panel.BackColor,
            Margin = new Padding(0),
        };
        content.Controls.Add(filler, 0, 3);

        return panel;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        if (!_monitorRuntime.Initialize())
        {
            _statusLabel.Text = "Native collector init failed";
            return;
        }

        SetModeStatus();
        _readerTask = Task.Run(ReadSnapshotsAsync);
        await Task.CompletedTask;
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        if (!_startupUpdateCheckPending)
        {
            return;
        }

        _startupUpdateCheckPending = false;
        await Task.Delay(800);
        await CheckForUpdatesAsync();
    }

    private async Task ReadSnapshotsAsync()
    {
        await foreach (var snapshot in _monitorRuntime.Snapshots.ReadAllAsync())
        {
            // Read once: the UI thread can null this field between checks (Stop Log).
            var session = _loggingSession;
            if (session is not null)
            {
                session.AddSample(snapshot);
                if (DateTimeOffset.Now >= session.EndTime)
                {
                    await SaveLoggingSessionAsync();
                }
            }

            if (!_isClosing && !IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
            }
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        // Nothing visible to repaint; the next snapshot after restore refreshes everything.
        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _cpuPrimary.Text = $"{snapshot.Cpu.UsagePercent:F1}%";
        _cpuSecondary.Text = snapshot.Cpu.CurrentClockSpeedMHz.HasValue
            ? $"Clock {FormatClockSpeed(snapshot.Cpu.CurrentClockSpeedMHz.Value)}"
            : "Clock N/A";
        _cpuBar.Value = ClampPercent(snapshot.Cpu.UsagePercent);

        _memoryPrimary.Text = $"{snapshot.Memory.UsagePercent:F1}%";
        _memorySecondary.Text = $"{FormatBytes(snapshot.Memory.UsedBytes)} / {FormatBytes(snapshot.Memory.TotalBytes)}";
        _memoryBar.Value = ClampPercent(snapshot.Memory.UsagePercent);

        _diskPrimary.Text = $"{snapshot.Disk.UsagePercent:F1}%";
        _diskSecondary.Text =
            $"Free {FormatBytes(snapshot.Disk.FreeBytes)} | R {FormatRate(snapshot.Disk.ReadBytesPerSecond)} | W {FormatRate(snapshot.Disk.WriteBytesPerSecond)}";
        _diskBar.Value = ClampPercent(snapshot.Disk.UsagePercent);

        _gpuPrimary.Text = $"{snapshot.Gpu.UsagePercent:F1}%";
        _gpuSecondary.Text = snapshot.Gpu.TemperatureCelsius.HasValue
            ? $"VRAM {FormatBytes(snapshot.Gpu.DedicatedMemoryBytes)} | Temp {snapshot.Gpu.TemperatureCelsius.Value:F1} C"
            : $"VRAM {FormatBytes(snapshot.Gpu.DedicatedMemoryBytes)} | Temp N/A";
        _gpuBar.Value = ClampPercent(snapshot.Gpu.UsagePercent);

        _networkPrimary.Text = $"RX {FormatRate(snapshot.Network.ReceiveBytesPerSecond)} | TX {FormatRate(snapshot.Network.SendBytesPerSecond)}";
        _networkSecondary.Text =
            $"Total RX {FormatBytes(snapshot.Network.TotalReceivedBytes)} | Total TX {FormatBytes(snapshot.Network.TotalSentBytes)}";

        _updatedLabel.Text = $"Updated: {snapshot.Timestamp.LocalDateTime:HH:mm:ss}";

        for (var i = 0; i < 5; i++)
        {
            var item = _processList.Items[i];
            if (i < snapshot.TopProcesses.Count)
            {
                var process = snapshot.TopProcesses[i];
                item.SubItems[0].Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
                item.SubItems[1].Text = process.Name;
                item.SubItems[2].Text = process.CpuPercent.ToString("F1", CultureInfo.InvariantCulture);
                item.SubItems[3].Text = FormatBytes(process.WorkingSetBytes);
            }
            else
            {
                item.SubItems[0].Text = "-";
                item.SubItems[1].Text = "-";
                item.SubItems[2].Text = "-";
                item.SubItems[3].Text = "-";
            }
        }

        UpdateLogStatusDisplay();
    }

    private void StartLogging()
    {
        SaveSettings();

        if (!TryGetConfiguredDuration(out var duration) || duration <= TimeSpan.Zero)
        {
            _loggingLabel.Text = "Use the hh:mm:ss format.";
            return;
        }

        var rootPath = _logPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            _loggingLabel.Text = "Select a valid log folder.";
            return;
        }

        Directory.CreateDirectory(rootPath);
        _loggingSession = new LoggingSession(duration, rootPath);
        _loggingLabel.Text = "summary.txt is saved automatically when the timer completes.";
        _startLoggingButton.Enabled = false;
        _stopLoggingButton.Enabled = true;
        SetModeStatus();
        UpdateLogStatusDisplay();
    }

    private void StopLogging()
    {
        CancelLoggingSession();
    }

    private void CancelLoggingSession()
    {
        var session = _loggingSession;
        if (session is null)
        {
            return;
        }

        _loggingSession = null;
        session.Discard();
        _startLoggingButton.Enabled = true;
        _stopLoggingButton.Enabled = false;
        _loggingLabel.Text = "Log session cleared.";
        SetModeStatus();
        UpdateLogStatusDisplay();
    }

    private async Task SaveLoggingSessionAsync()
    {
        var session = _loggingSession;
        if (session is null)
        {
            return;
        }

        _loggingSession = null;
        await session.SaveAsync();

        if (!_isClosing && !IsDisposed && IsHandleCreated)
        {
            BeginInvoke(new Action(() =>
            {
                _startLoggingButton.Enabled = true;
                _stopLoggingButton.Enabled = false;
                _loggingLabel.Text = $"summary.txt saved to {session.SessionDirectory}";
                SetModeStatus();
                UpdateLogStatusDisplay(session.SessionDirectory);
            }));
        }
    }

    private void BrowseLogPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder where log sessions will be created.",
            InitialDirectory = Directory.Exists(_logPathTextBox.Text) ? _logPathTextBox.Text : AppContext.BaseDirectory,
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _logPathTextBox.Text = dialog.SelectedPath;
            UpdateLogStatusDisplay();
        }
    }

    private void OpenLogPath()
    {
        var targetPath = _loggingSession?.SessionDirectory ?? _logPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(targetPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true,
        });
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _isClosing = true;
        _startupUpdateCheckPending = false;

        CancelLoggingSession();

        try
        {
            _monitorRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
        }

        try
        {
            if (_readerTask is not null)
            {
                Task.WhenAny(_readerTask, Task.Delay(1500)).GetAwaiter().GetResult();
            }
        }
        catch
        {
        }

        SaveSettings();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates || _isClosing)
        {
            return;
        }

        var updateSourceInput = AppRuntimeDefaults.GetDefaultUpdateSource();
        if (string.IsNullOrWhiteSpace(updateSourceInput))
        {
            return;
        }

        if (!TryResolveUpdateSource(updateSourceInput, out var updateSource))
        {
            return;
        }

        _isCheckingForUpdates = true;
        _statusLabel.Text = "Checking updates";

        try
        {
            var manager = new UpdateManager(updateSource);
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null || _isClosing)
            {
                return;
            }

            var versionText = update.TargetFullRelease.Version?.ToString() ?? _appVersionText;
            var confirm = MessageBox.Show(
                this,
                $"An update is available.\nCurrent version: {_appVersionText}\nNew version: {versionText}\n\nWould you like to download and install it now?\nThe app will restart automatically after the update.",
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes || _isClosing)
            {
                return;
            }

            _statusLabel.Text = "Downloading update...";
            using var progressDialog = new UpdateDownloadProgressDialog();
            progressDialog.Show(this);
            try
            {
                await manager.DownloadUpdatesAsync(update, progress =>
                {
                    if (!_isClosing && !IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _statusLabel.Text = $"Downloading update... {progress}%";
                            progressDialog.ReportProgress(progress);
                        }));
                    }
                });
            }
            finally
            {
                progressDialog.CloseSafely();
            }

            if (_isClosing)
            {
                return;
            }

            _statusLabel.Text = "Applying update and restarting...";
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch
        {
        }
        finally
        {
            _isCheckingForUpdates = false;
            if (!_isClosing && !IsDisposed)
            {
                SetModeStatus();
            }
        }
    }

    private void SaveSettings()
    {
        _settings.LogRootPath = _logPathTextBox.Text.Trim();
        AppSettingsStore.Save(_settings);
    }

    private void SetModeStatus()
    {
        _statusLabel.Text = _loggingSession is null ? "Monitoring" : "Monitoring + logging";
    }

    private void UpdateLogStatusDisplay(string? savedSessionDirectory = null)
    {
        if (_loggingSession is not null)
        {
            _loggerPrimary.Text = "Logging";
            _loggerSummary.Text =
                $"Target {FormatDuration(_loggingSession.Duration)} | Left {FormatDuration(_loggingSession.Remaining)}";
            return;
        }

        _loggerPrimary.Text = "Ready";

        if (!string.IsNullOrWhiteSpace(savedSessionDirectory))
        {
            if (TryGetConfiguredDuration(out var savedDuration))
            {
                _loggerSummary.Text =
                    $"Target {FormatDuration(savedDuration)} | Left {FormatDuration(savedDuration)}";
            }
            else
            {
                _loggerSummary.Text = "Target --:--:-- | Left --:--:--";
            }

            return;
        }

        if (TryGetConfiguredDuration(out var duration))
        {
            _loggerSummary.Text =
                $"Target {FormatDuration(duration)} | Left {FormatDuration(duration)}";
        }
        else
        {
            _loggerSummary.Text = "Target --:--:-- | Left --:--:--";
        }
    }

    private Button CreateHeaderButton(string text, string url)
    {
        var button = new Button
        {
            Text = text,
            Width = 92,
            Height = 28,
            Margin = new Padding(6, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(36, 59, 83),
            ForeColor = Color.White,
            TabStop = false,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(78, 99, 126);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(52, 76, 103);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 69, 96);
        button.Click += (_, _) => OpenExternalUrl(url);
        return button;
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private bool TryGetConfiguredDuration(out TimeSpan duration) =>
        TimeSpan.TryParseExact(_durationTextBox.Text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out duration);

    private static bool TryResolveUpdateSource(string input, out string source)
    {
        source = input;

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                source = uri.AbsoluteUri;
                return true;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                source = uri.LocalPath;
                return true;
            }
        }

        try
        {
            source = Path.GetFullPath(input);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDisplayVersion()
    {
        var versionText = Application.ProductVersion;
        if (Version.TryParse(versionText, out var version))
        {
            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }

        return versionText;
    }

    private static string FormatDuration(TimeSpan value) =>
        value < TimeSpan.Zero ? "00:00:00" : value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static int ClampPercent(double value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 100)
        {
            return 100;
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F1} {units[unitIndex]}";
    }

    private static string FormatRate(double bytesPerSecond) => $"{FormatBytes(bytesPerSecond)}/s";

    private void ConfigureLogStatLabel(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular);
        label.ForeColor = Color.FromArgb(159, 179, 200);
        label.TextAlign = ContentAlignment.TopLeft;
        label.Padding = new Padding(0, 2, 0, 0);
    }

    private Label CreateProcessHeaderLabel(string text, ContentAlignment alignment)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(224, 225, 221),
            Font = new Font(Font.FontFamily, 8.75F, FontStyle.Bold),
            TextAlign = alignment,
            Margin = new Padding(0),
        };
    }

    private void UpdateProcessColumnLayout()
    {
        if (_processList.Columns.Count < 4 || _processHeader.ColumnStyles.Count < 4)
        {
            return;
        }

        var availableWidth = _processList.ClientSize.Width;
        var fixedWidth = ProcessPidColumnWidth + ProcessCpuColumnWidth + ProcessMemoryColumnWidth;
        var processColumnWidth = Math.Max(160, availableWidth - fixedWidth - 4);

        _processList.Columns[0].Width = ProcessPidColumnWidth;
        _processList.Columns[1].Width = processColumnWidth;
        _processList.Columns[2].Width = ProcessCpuColumnWidth;
        _processList.Columns[3].Width = ProcessMemoryColumnWidth;

        _processHeader.ColumnStyles[0].SizeType = SizeType.Absolute;
        _processHeader.ColumnStyles[0].Width = ProcessPidColumnWidth;
        _processHeader.ColumnStyles[1].SizeType = SizeType.Absolute;
        _processHeader.ColumnStyles[1].Width = processColumnWidth;
        _processHeader.ColumnStyles[2].SizeType = SizeType.Absolute;
        _processHeader.ColumnStyles[2].Width = ProcessCpuColumnWidth;
        _processHeader.ColumnStyles[3].SizeType = SizeType.Absolute;
        _processHeader.ColumnStyles[3].Width = ProcessMemoryColumnWidth;
    }

    private static string FormatClockSpeed(double megahertz)
    {
        if (megahertz >= 1000d)
        {
            return $"{megahertz / 1000d:F2} GHz";
        }

        return $"{megahertz:F0} MHz";
    }
}
