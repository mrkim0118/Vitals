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
    private readonly Button _cancelLoggingButton = new();
    private readonly Button _browseLogPathButton = new();
    private readonly Button _openLogPathButton = new();

    private readonly MetricCard _cpuCard = new("CPU", Color.FromArgb(96, 165, 250));
    private readonly MetricCard _memoryCard = new("Memory", Color.FromArgb(167, 139, 250));
    private readonly MetricCard _diskCard = new("Disk (active time)", Color.FromArgb(45, 212, 191));
    private readonly MetricCard _gpuCard = new("GPU", Color.FromArgb(163, 230, 53));
    private readonly MetricCard _networkCard = new(
        "Network", Color.FromArgb(56, 189, 248), accent2: Color.FromArgb(251, 191, 36), sparklineMax: null);
    private readonly MetricCard _logCard = new("Log", Color.FromArgb(127, 209, 185), sparkline: false);

    private readonly Label _selfUsageLabel = new();
    private readonly Process _selfProcess = Process.GetCurrentProcess();
    private readonly Stopwatch _selfClock = Stopwatch.StartNew();
    private TimeSpan _lastSelfCpu;
    private TimeSpan _lastSelfWall;
    private readonly bool _isAdministrator = IsRunningAsAdministrator();

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
        MinimumSize = new Size(880, 720);
        Size = new Size(980, 780);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 37, 58),
            Padding = new Padding(14, 7, 14, 4),
        };
        root.Controls.Add(header, 0, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 300,
            FlowDirection = FlowDirection.RightToLeft,
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
            Location = new Point(10, 14),
        };
        header.Controls.Add(title);

        actionPanel.Controls.Add(CreateMenuButton());

        var statusStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = header.BackColor,
            Margin = new Padding(0, 0, 12, 0),
        };
        actionPanel.Controls.Add(statusStack);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(127, 209, 185);
        _statusLabel.Text = "Idle";
        _statusLabel.Margin = new Padding(0);
        statusStack.Controls.Add(_statusLabel);

        // How much this app itself costs: the number a monitoring tool should be honest about.
        _selfUsageLabel.AutoSize = true;
        _selfUsageLabel.ForeColor = Color.FromArgb(159, 179, 200);
        _selfUsageLabel.Text = "This app: -";
        _selfUsageLabel.Margin = new Padding(0);
        statusStack.Controls.Add(_selfUsageLabel);

        _updatedLabel.AutoSize = true;
        _updatedLabel.ForeColor = Color.FromArgb(159, 179, 200);
        _updatedLabel.Text = "Updated: -";
        _updatedLabel.Margin = new Padding(0);
        statusStack.Controls.Add(_updatedLabel);

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

        metrics.Controls.Add(_cpuCard, 0, 0);
        metrics.Controls.Add(_memoryCard, 1, 0);
        metrics.Controls.Add(_diskCard, 2, 0);
        metrics.Controls.Add(_gpuCard, 0, 1);
        metrics.Controls.Add(_networkCard, 1, 1);
        metrics.Controls.Add(_logCard, 2, 1);

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
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
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
            Text = "Duration (h:mm:ss)",
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

        var presets = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = sidePanel.BackColor,
            Margin = new Padding(0),
        };
        foreach (var (label, value) in new[] { ("10 min", "00:10:00"), ("30 min", "00:30:00"), ("1 hour", "01:00:00") })
        {
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            var preset = new Button { Text = label, Dock = DockStyle.Fill };
            preset.Click += (_, _) => _durationTextBox.Text = value;
            presets.Controls.Add(preset);
        }

        sideLayout.Controls.Add(presets, 0, 3);

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

        var stopButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = sidePanel.BackColor,
            Margin = new Padding(0),
        };
        stopButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        stopButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        sideLayout.Controls.Add(stopButtons, 0, 9);

        _stopLoggingButton.Text = "Stop && Save";
        _stopLoggingButton.Dock = DockStyle.Fill;
        _stopLoggingButton.Enabled = false;
        _stopLoggingButton.Click += (_, _) => StopLogging();
        stopButtons.Controls.Add(_stopLoggingButton, 0, 0);

        _cancelLoggingButton.Text = "Discard";
        _cancelLoggingButton.Dock = DockStyle.Fill;
        _cancelLoggingButton.Enabled = false;
        _cancelLoggingButton.Click += (_, _) => CancelLoggingSession();
        stopButtons.Controls.Add(_cancelLoggingButton, 1, 0);

        _loggingLabel.Dock = DockStyle.Fill;
        _loggingLabel.ForeColor = Color.FromArgb(159, 179, 200);
        _loggingLabel.Text = "Stop && Save writes summary.txt now. Discard deletes the session.";
        _loggingLabel.TextAlign = ContentAlignment.TopLeft;
        sideLayout.Controls.Add(_loggingLabel, 0, 10);

        _cpuCard.SetContent("-", null, ("Clock", "-"), ("Temp", "-"));
        _memoryCard.SetContent("-", null, ("Used", "-"), ("Total", "-"));
        _diskCard.SetContent("-", null, ("Read", "-"), ("Write", "-"), ("Free", "-"));
        _gpuCard.SetContent("-", null, ("VRAM", "-"), ("Temp", "-"));
        _networkCard.SetContent("-", null, ("Upload", "-"), ("Total down / up", "-"));

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

        var cpu = snapshot.Cpu;
        _cpuCard.Push(cpu.UsagePercent);
        _cpuCard.SetContent(
            $"{cpu.UsagePercent:F1}%",
            cpu.UsagePercent,
            ("Clock", cpu.CurrentClockSpeedMHz.HasValue ? FormatClockSpeed(cpu.CurrentClockSpeedMHz.Value) : "N/A"),
            ("Temp", FormatTemperature(cpu.TemperatureCelsius)));

        var memory = snapshot.Memory;
        _memoryCard.Push(memory.UsagePercent);
        _memoryCard.SetContent(
            $"{memory.UsagePercent:F1}%",
            memory.UsagePercent,
            ("Used", FormatBytes(memory.UsedBytes)),
            ("Total", FormatBytes(memory.TotalBytes)));

        var disk = snapshot.Disk;
        _diskCard.Push(disk.UsagePercent);
        _diskCard.SetContent(
            $"{disk.UsagePercent:F1}%",
            disk.UsagePercent,
            ("Read", FormatRate(disk.ReadBytesPerSecond)),
            ("Write", FormatRate(disk.WriteBytesPerSecond)),
            ("Free", $"{FormatBytes(disk.FreeBytes)} / {FormatBytes(disk.TotalBytes)}"));

        var gpu = snapshot.Gpu;
        _gpuCard.Push(gpu.UsagePercent);
        _gpuCard.SetContent(
            $"{gpu.UsagePercent:F1}%",
            gpu.UsagePercent,
            ("VRAM", FormatBytes(gpu.DedicatedMemoryBytes)),
            ("Temp", FormatTemperature(gpu.TemperatureCelsius)));

        var network = snapshot.Network;
        _networkCard.Push(network.ReceiveBytesPerSecond, network.SendBytesPerSecond);
        _networkCard.SetContent(
            $"↓ {FormatRate(network.ReceiveBytesPerSecond)}",
            null,
            ("Upload", $"↑ {FormatRate(network.SendBytesPerSecond)}"),
            ("Total down / up", $"{FormatBytes(network.TotalReceivedBytes)} / {FormatBytes(network.TotalSentBytes)}"));

        _updatedLabel.Text = $"Updated: {snapshot.Timestamp.LocalDateTime:HH:mm:ss}";
        UpdateSelfUsage();

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
            _loggingLabel.Text = "Use the h:mm:ss format, e.g. 00:30:00 or 36:00:00.";
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
        SetLoggingButtons(isLogging: true);
        SetModeStatus();
        UpdateLogStatusDisplay();
    }

    private void SetLoggingButtons(bool isLogging)
    {
        _startLoggingButton.Enabled = !isLogging;
        _stopLoggingButton.Enabled = isLogging;
        _cancelLoggingButton.Enabled = isLogging;
    }

    private async void StopLogging()
    {
        await SaveLoggingSessionAsync();
    }

    private void CancelLoggingSession()
    {
        // Exchange: the reader thread may be saving the same session at its end time.
        var session = Interlocked.Exchange(ref _loggingSession, null);
        if (session is null)
        {
            return;
        }

        session.Discard();
        SetLoggingButtons(isLogging: false);
        _loggingLabel.Text = "Log session discarded.";
        SetModeStatus();
        UpdateLogStatusDisplay();
    }

    private async Task SaveLoggingSessionAsync()
    {
        var session = Interlocked.Exchange(ref _loggingSession, null);
        if (session is null)
        {
            return;
        }

        await session.SaveAsync();

        if (!_isClosing && !IsDisposed && IsHandleCreated)
        {
            BeginInvoke(new Action(() =>
            {
                SetLoggingButtons(isLogging: false);
                _loggingLabel.Text = session.HasSamples
                    ? $"summary.txt saved to {session.SessionDirectory}"
                    : "Nothing recorded yet, so no file was saved.";
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

    private async Task CheckForUpdatesAsync(bool manual = false)
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
                if (manual)
                {
                    MessageBox.Show(this, "Updates work only in the installed app.", "Check for updates");
                }

                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null || _isClosing)
            {
                if (manual && update is null)
                {
                    MessageBox.Show(this, $"You have the latest version ({_appVersionText}).", "Check for updates");
                }

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
        var session = _loggingSession;
        if (session is not null)
        {
            var progress = session.Duration > TimeSpan.Zero
                ? session.Elapsed.TotalSeconds / session.Duration.TotalSeconds * 100
                : 0;
            // percent drives the bar; logging progress is not a load level, so keep it out of the warning colours.
            _logCard.SetContent(
                "Logging",
                Math.Min(progress, 59.9),
                ("Elapsed", FormatDuration(session.Elapsed)),
                ("Left", FormatDuration(session.Remaining)),
                ("Target", FormatDuration(session.Duration)));
            return;
        }

        var target = TryGetConfiguredDuration(out var duration) ? FormatDuration(duration) : "invalid";
        _logCard.SetContent(
            "Ready",
            null,
            ("Target", target),
            ("Last saved", string.IsNullOrWhiteSpace(savedSessionDirectory) ? "-" : Path.GetFileName(savedSessionDirectory)));
    }

    private void UpdateSelfUsage()
    {
        _selfProcess.Refresh();
        var cpu = _selfProcess.TotalProcessorTime;
        var wall = _selfClock.Elapsed;
        var cpuPercent = (cpu - _lastSelfCpu).TotalMilliseconds /
                         Math.Max(1, (wall - _lastSelfWall).TotalMilliseconds) / Environment.ProcessorCount * 100;
        _lastSelfCpu = cpu;
        _lastSelfWall = wall;
        _selfUsageLabel.Text = $"This app: CPU {cpuPercent:F1}% · RAM {FormatBytes(_selfProcess.WorkingSet64)}";
    }

    private string FormatTemperature(double? celsius) =>
        // Sensors that can't be read without admin often report 0 instead of nothing.
        celsius > 0 ? $"{celsius.Value:F0} °C" : _isAdministrator ? "N/A" : "N/A (run as admin)";

    private static bool IsRunningAsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private Button CreateMenuButton()
    {
        var button = new Button
        {
            Text = "⋯",
            Width = 36,
            Height = 30,
            Margin = new Padding(0, 6, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(36, 59, 83),
            ForeColor = Color.White,
            TabStop = false,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(78, 99, 126);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 69, 96);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Homepage", null, (_, _) => OpenExternalUrl("https://techarchive.click/"));
        menu.Items.Add("Donate", null, (_, _) => OpenExternalUrl("https://www.paypal.com/ncp/payment/T2KAST2XQC6BS"));
        button.Click += (_, _) => menu.Show(button, new Point(button.Width - menu.Width, button.Height));
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

    // h:mm:ss with any hour count, so sessions longer than 24h are allowed.
    private bool TryGetConfiguredDuration(out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var parts = _durationTextBox.Text.Trim().Split(':');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            minutes > 59 || seconds > 59 || hours > 999)
        {
            return false;
        }

        duration = new TimeSpan(hours, minutes, seconds);
        return true;
    }

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
        // The SDK appends "+<commit hash>" to the product version; drop it for display.
        var versionText = Application.ProductVersion.Split('+')[0];
        if (Version.TryParse(versionText, out var version))
        {
            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }

        return versionText;
    }

    private static string FormatDuration(TimeSpan value) =>
        value < TimeSpan.Zero
            ? "00:00:00"
            : $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

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
