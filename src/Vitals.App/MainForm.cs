using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vitals.App.Models;
using Vitals.App.Services;

namespace Vitals.App;

public sealed class MainForm : Form
{

    private readonly MonitorRuntime _monitorRuntime = new();
    private readonly ListView _processList = new();
    private readonly TableLayoutPanel _processHeader = new();
    private const int ProcessRowCount = 10;
    // Width 0 for Process: it is sized to fill the remaining space.
    private static readonly (string Title, int Width)[] ProcessColumns =
        [("PID", 56), ("Process", 0), ("CPU %", 58), ("GPU %", 58), ("Memory", 84), ("Disk", 84), ("Network", 84)];
    private readonly Label[] _processHeaderLabels = new Label[ProcessColumns.Length];
    private int _processSortColumn = 2;
    private bool _processSortDescending = true;
    private IReadOnlyList<ProcessMetrics> _lastProcesses = [];
    private bool _processNetworkAvailable;
    private readonly TextBox _durationTextBox = new();
    private readonly TextBox _logPathTextBox = new();
    private readonly Label _statusLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _loggingLabel = new();
    private readonly ThemedButton _startLoggingButton = new();
    private readonly ThemedButton _stopLoggingButton = new();
    private readonly ThemedButton _cancelLoggingButton = new();
    private readonly ThemedButton _browseLogPathButton = new();
    private readonly ThemedButton _openLogPathButton = new();

    private readonly MetricCard _cpuCard = new("CPU", Color.FromArgb(96, 165, 250));
    private readonly MetricCard _memoryCard = new("Memory", Color.FromArgb(167, 139, 250));
    private readonly MetricCard _diskCard = new("Disk (active time)", Color.FromArgb(45, 212, 191));
    private readonly MetricCard _gpuCard = new("GPU", Color.FromArgb(163, 230, 53));
    private readonly MetricCard _networkCard = new(
        "Network", Color.FromArgb(56, 189, 248), accent2: Color.FromArgb(251, 191, 36), sparklineMax: null);
    private readonly MetricCard _logCard = new("Log", Theme.Accent, sparkline: false);

    private readonly Label _selfUsageLabel = new();
    private readonly Process _selfProcess = Process.GetCurrentProcess();
    private readonly Stopwatch _selfClock = Stopwatch.StartNew();
    private TimeSpan _lastSelfCpu;
    private TimeSpan _lastSelfWall;
    private readonly bool _isAdministrator = Environment.IsPrivilegedProcess;
    private readonly bool _pawnIoInstalled = HardwareSensorService.IsPawnIoInstalled();

    private readonly AppSettings _settings;
    private readonly string _appVersionText;
    private LoggingSession? _loggingSession;
    private TableLayoutPanel _root = null!;
    private TableLayoutPanel _bottom = null!;
    private readonly NotifyIcon _tray = new();
    private readonly TrayWidget _widget = new();
    private readonly AlertMonitor _alerts = new();
    private List<ProcessMetrics> _shownProcesses = [];
    private Task? _readerTask;
    private bool _isClosing;
    private bool _exitRequested;

    public MainForm()
    {
        _settings = AppSettingsStore.Load();
        _appVersionText = GetDisplayVersion();

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = $"Vitals v{_appVersionText}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(880, 740);
        Size = new Size(980, 800);
        BackColor = Theme.Background;
        ForeColor = Color.White;
        // From the embedded resource: extracting it from the exe pulls in shell32 and ~16 MB of shell DLLs.
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("Vitals.AppIcon.ico"))
        {
            Icon = iconStream is null ? null : new Icon(iconStream);
        }

        BuildLayout();
        CreateTray();
        _widget.Click += (_, _) => RestoreFromTray();
        TopMost = _settings.AlwaysOnTop;
        Resize += (_, _) => OnResized();

        Load += OnLoad;
        Shown += (_, _) => CheckForUpdateOnStartup();
        FormClosing += OnFormClosing;
    }

    // Asks once per launch when GitHub has a newer release, then runs its setup and closes.
    private async void CheckForUpdateOnStartup()
    {
        var release = await GitHubUpdater.FindNewerReleaseAsync();
        if (release is null || IsDisposed)
        {
            return;
        }

        var message = $"Version v{release.Version} is available.\nCurrent version: {GitHubUpdater.CurrentVersion}\n\nDownload and install it now?";
        if (MessageBox.Show(this, message, "Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        var originalTitle = Text;
        UseWaitCursor = true;
        try
        {
            await GitHubUpdater.DownloadAndStartSetupAsync(release, new Progress<int>(percent => Text = $"Downloading update... {percent}%"));
            ExitApp();
        }
        catch (Exception ex)
        {
            Text = originalTitle;
            MessageBox.Show(this, $"Failed to download the update.\n{ex.Message}", "Update Available", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var root = _root = new TableLayoutPanel
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

        var header = new CardPanel(Theme.Header)
        {
            Dock = DockStyle.Fill,
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
            Text = $"Vitals v{_appVersionText}",
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
        _statusLabel.ForeColor = Theme.Accent;
        _statusLabel.Text = "Idle";
        _statusLabel.Margin = new Padding(0);
        statusStack.Controls.Add(_statusLabel);

        // How much this app itself costs: the number a monitoring tool should be honest about.
        _selfUsageLabel.AutoSize = true;
        _selfUsageLabel.ForeColor = Theme.TextMuted;
        _selfUsageLabel.Text = "This app: -";
        _selfUsageLabel.Margin = new Padding(0);
        statusStack.Controls.Add(_selfUsageLabel);

        _updatedLabel.AutoSize = true;
        _updatedLabel.ForeColor = Theme.TextMuted;
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

        var bottom = _bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ColumnCount = 2,
            RowCount = 1,
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        root.Controls.Add(bottom, 0, 2);

        var processPanel = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
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
        _processHeader.ColumnCount = ProcessColumns.Length;
        _processHeader.RowCount = 1;
        _processHeader.BackColor = Theme.SurfaceRaised;
        _processHeader.Margin = new Padding(0, 0, 0, 6);
        _processHeader.Padding = new Padding(8, 0, 8, 0);
        for (var column = 0; column < ProcessColumns.Length; column++)
        {
            _processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProcessColumns[column].Width));
            var label = CreateProcessHeaderLabel(
                ProcessColumns[column].Title, column >= 2 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft);
            var sortColumn = column;
            label.Cursor = Cursors.Hand;
            label.Click += (_, _) => SortProcessesBy(sortColumn);
            _processHeaderLabels[column] = label;
            _processHeader.Controls.Add(label, column, 0);
        }

        _processHeaderLabels[_processSortColumn].Text += " ▼";
        processLayout.Controls.Add(_processHeader, 0, 0);

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.HeaderStyle = ColumnHeaderStyle.None;
        _processList.MultiSelect = false;
        _processList.HideSelection = false;
        _processList.BorderStyle = BorderStyle.None;
        _processList.BackColor = Theme.Surface;
        _processList.ForeColor = Color.White;
        for (var column = 0; column < ProcessColumns.Length; column++)
        {
            _processList.Columns.Add(
                ProcessColumns[column].Title,
                ProcessColumns[column].Width,
                column >= 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }

        _processList.SizeChanged += (_, _) => UpdateProcessColumnLayout();
        _processList.ContextMenuStrip = CreateProcessMenu();
        processLayout.Controls.Add(_processList, 0, 1);

        for (var i = 0; i < ProcessRowCount; i++)
        {
            _processList.Items.Add(new ListViewItem(Enumerable.Repeat("-", ProcessColumns.Length).ToArray()));
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
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F)); // was a "Time Setting" heading that repeated the label below
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
        sidePanel.Controls.Add(sideLayout);
        sideLayout.BringToFront(); // dock after the title, not underneath it

        sideLayout.Controls.Add(new Label
        {
            Text = "Duration (h:mm:ss)",
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextTitle,
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 1);

        _durationTextBox.Text = "00:10:00";
        _durationTextBox.TextChanged += (_, _) => UpdateLogStatusDisplay();
        sideLayout.Controls.Add(WrapInput(_durationTextBox), 0, 2);

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
            var preset = new ThemedButton { Text = label, Dock = DockStyle.Fill };
            preset.Click += (_, _) => _durationTextBox.Text = value;
            presets.Controls.Add(preset);
        }

        sideLayout.Controls.Add(presets, 0, 3);

        sideLayout.Controls.Add(new Label
        {
            Text = "Log folder",
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextTitle,
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 4);

        _logPathTextBox.Text = _settings.LogRootPath;
        sideLayout.Controls.Add(WrapInput(_logPathTextBox), 0, 5);

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
        _startLoggingButton.Primary = true;
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
        _loggingLabel.ForeColor = Theme.TextMuted;
        _loggingLabel.Text = "Save keeps the log, Discard deletes it.";
        _loggingLabel.TextAlign = ContentAlignment.TopLeft;
        sideLayout.Controls.Add(_loggingLabel, 0, 10);

        _cpuCard.SetContent("-", null, ("Clock", "-"), ("Temp", "-"));
        _memoryCard.SetContent("-", null, ("Used", "-"), ("Total", "-"));
        _diskCard.SetContent("-", null, ("Read", "-"), ("Write", "-"));
        _gpuCard.SetContent("-", null, ("VRAM", "-"), ("Temp", "-"));
        _networkCard.SetContent("-", null, ("Upload", "-"), ("Total down / up", "-"));

        UpdateLogStatusDisplay();
        ResumeLayout();
    }

    private Panel CreateSectionPanel(string title)
    {
        var container = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
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

    // TextBox has no padding of its own; a rounded box around a borderless one matches the buttons.
    private static Control WrapInput(TextBox textBox)
    {
        textBox.BorderStyle = BorderStyle.None;
        textBox.BackColor = Theme.Control;
        textBox.ForeColor = Theme.Text;
        textBox.Dock = DockStyle.Fill;
        var box = new CardPanel(Theme.Control)
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 4),
            Margin = new Padding(3),
        };
        box.Controls.Add(textBox);
        box.Click += (_, _) => textBox.Focus();
        return box;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
        // Let a non-elevated second copy reach this window even when it runs as administrator (UIPI).
        ChangeWindowMessageFilterEx(Handle, SingleInstance.ShowMessage, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == SingleInstance.ShowMessage)
        {
            RestoreFromTray();
            return;
        }

        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int message, uint action, IntPtr changeInfo);

    private async void OnLoad(object? sender, EventArgs e)
    {
        if (!_monitorRuntime.Initialize())
        {
            _statusLabel.Text = "Native collector init failed";
            return;
        }

        _processNetworkAvailable = Native.NativeMethods.ProcessNetworkAvailable;
        _monitorRuntime.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.UpdateIntervalSeconds, 1, 5));
        ApplyCompactView();
        SetModeStatus();
        _readerTask = Task.Run(ReadSnapshotsAsync);
        await Task.CompletedTask;
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
        UpdateTrayAndAlerts(snapshot);

        // Nothing visible to repaint; the next snapshot after restore refreshes everything.
        if (WindowState == FormWindowState.Minimized || !Visible)
        {
            return;
        }

        var cpu = snapshot.Cpu;
        _cpuCard.Push(cpu.UsagePercent);
        _cpuCard.SetContent(
            $"{cpu.UsagePercent:F1}%",
            cpu.UsagePercent,
            ("Clock", cpu.CurrentClockSpeedMHz.HasValue ? FormatClockSpeed(cpu.CurrentClockSpeedMHz.Value) : "N/A"),
            ("Temp", FormatCpuTemperature(cpu.TemperatureCelsius)));

        var memory = snapshot.Memory;
        _memoryCard.Push(memory.UsagePercent);
        _memoryCard.SetContent(
            $"{memory.UsagePercent:F1}%",
            memory.UsagePercent,
            ("Used", FormatBytes(memory.UsedBytes)),
            ("Total", FormatBytes(memory.TotalBytes)));

        var disk = snapshot.Disk;
        _diskCard.Push(disk.UsagePercent);
        // shortcut: the card fits three drive rows; a fourth drive and beyond are left out.
        var diskRows = new List<(string, string)>
        {
            ("Read", FormatRate(disk.ReadBytesPerSecond)),
            ("Write", FormatRate(disk.WriteBytesPerSecond)),
        };
        diskRows.AddRange(snapshot.Drives.Take(3).Select(d => (d.Name, FormatDrive(d))));
        _diskCard.SetContent($"{disk.UsagePercent:F1}%", disk.UsagePercent, diskRows.ToArray());

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

        _lastProcesses = snapshot.TopProcesses;
        RenderProcessList();
        UpdateLogStatusDisplay();
    }

    private void SortProcessesBy(int column)
    {
        // Same column flips direction; a new column starts with the most useful order (largest first, names A-Z).
        _processSortDescending = column == _processSortColumn ? !_processSortDescending : column >= 2;
        _processSortColumn = column;
        for (var i = 0; i < _processHeaderLabels.Length; i++)
        {
            var label = _processHeaderLabels[i];
            var baseText = ProcessColumns[i].Title;
            label.Text = i != column ? baseText : $"{baseText} {(_processSortDescending ? "▼" : "▲")}";
        }

        RenderProcessList();
    }

    private void RenderProcessList()
    {
        IEnumerable<ProcessMetrics> ordered = _processSortColumn switch
        {
            0 => _lastProcesses.OrderBy(p => p.ProcessId),
            1 => _lastProcesses.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            3 => _lastProcesses.OrderBy(p => p.GpuPercent).ThenBy(p => p.WorkingSetBytes),
            4 => _lastProcesses.OrderBy(p => p.WorkingSetBytes),
            5 => _lastProcesses.OrderBy(p => p.DiskBytesPerSecond).ThenBy(p => p.WorkingSetBytes),
            6 => _lastProcesses.OrderBy(p => p.NetworkBytesPerSecond).ThenBy(p => p.WorkingSetBytes),
            _ => _lastProcesses.OrderBy(p => p.CpuPercent).ThenBy(p => p.WorkingSetBytes),
        };
        var rows = _shownProcesses = (_processSortDescending ? ordered.Reverse() : ordered).Take(ProcessRowCount).ToList();

        for (var i = 0; i < ProcessRowCount; i++)
        {
            var item = _processList.Items[i];
            if (i < rows.Count)
            {
                var process = rows[i];
                item.SubItems[0].Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
                item.SubItems[1].Text = process.Name;
                item.SubItems[2].Text = process.CpuPercent.ToString("F1", CultureInfo.InvariantCulture);
                item.SubItems[3].Text = process.GpuPercent.ToString("F1", CultureInfo.InvariantCulture);
                item.SubItems[4].Text = FormatBytes(process.WorkingSetBytes);
                item.SubItems[5].Text = FormatRate(process.DiskBytesPerSecond);
                item.SubItems[6].Text = _processNetworkAvailable ? FormatRate(process.NetworkBytesPerSecond) : "N/A";
            }
            else
            {
                foreach (ListViewItem.ListViewSubItem subItem in item.SubItems)
                {
                    subItem.Text = "-";
                }
            }
        }
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
        // The X button sends the window to the tray; Windows shutdown and ExitApp still close it.
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            SendToTray();
            return;
        }

        _isClosing = true;

        // Closing (also via the updater) keeps the log. Saved on the thread pool so the UI thread can block on it without deadlocking.
        var session = Interlocked.Exchange(ref _loggingSession, null);
        if (session is not null)
        {
            try
            {
                Task.Run(() => session.SaveAsync()).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

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
        _tray.Visible = false;
        _tray.Dispose();
        _widget.Dispose();
    }

    private void SaveSettings()
    {
        _settings.LogRootPath = _logPathTextBox.Text.Trim();
        AppSettingsStore.Save(_settings);
    }

    private void SetModeStatus()
    {
        _statusLabel.Text = (_loggingSession is null ? "Monitoring" : "Monitoring + logging") +
                            (_isAdministrator ? " · Administrator" : "");
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

    // Sensors that can't be read often report 0 instead of nothing.
    private static string FormatTemperature(double? celsius) => celsius > 0 ? $"{celsius.Value:F0} °C" : "N/A";

    // CPU sensors go through LibreHardwareMonitor's PawnIO kernel driver, which needs both the driver and admin rights.
    private string FormatCpuTemperature(double? celsius) =>
        celsius > 0 ? $"{celsius.Value:F0} °C"
        : !_pawnIoInstalled ? "N/A (needs PawnIO driver)"
        : !_isAdministrator ? "N/A (run as admin)"
        : "N/A";

    private void ShowPawnIoHelp()
    {
        var answer = MessageBox.Show(
            this,
            "CPU temperature is read through PawnIO, a signed kernel driver used by LibreHardwareMonitor.\n\n" +
            "Install it from pawnio.eu (or run: winget install namazso.PawnIO), then restart this app as administrator.\n\n" +
            "Open pawnio.eu now?",
            "CPU temperature driver",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://pawnio.eu/", UseShellExecute = true });
        }
    }

    private async Task RestartAsAdministratorAsync()
    {
        if (_loggingSession is not null)
        {
            var answer = MessageBox.Show(
                this,
                "A log is being recorded. It will be saved now and stopped before restarting.",
                "Restart as administrator",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (answer != DialogResult.OK)
            {
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                Arguments = Program.RestartingArgument,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined: keep running as-is.
            return;
        }

        await SaveLoggingSessionAsync();
        ExitApp();
    }

    private Button CreateMenuButton()
    {
        var button = new ThemedButton
        {
            Text = "⋯",
            Width = 36,
            Height = 30,
            Margin = new Padding(0, 6, 0, 0),
            TabStop = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(Toggle("Always on top", () => _settings.AlwaysOnTop, on => { _settings.AlwaysOnTop = on; TopMost = on; }));
        menu.Items.Add(Toggle("Compact view (cards only)", () => _settings.CompactView, on => { _settings.CompactView = on; ApplyCompactView(); }));
        menu.Items.Add(Toggle("Minimize or close to tray", () => _settings.MinimizeToTray, on => _settings.MinimizeToTray = on));
        menu.Items.Add(Toggle("Show widget while in the tray", () => _settings.ShowTrayWidget, on => _settings.ShowTrayWidget = on));
        menu.Items.Add(new ToolStripSeparator());

        var interval = new ToolStripMenuItem("Update interval");
        foreach (var seconds in new[] { 1, 2, 5 })
        {
            interval.DropDownItems.Add(Toggle($"{seconds} s", () => _settings.UpdateIntervalSeconds == seconds, _ =>
            {
                _settings.UpdateIntervalSeconds = seconds;
                _monitorRuntime.Interval = TimeSpan.FromSeconds(seconds);
            }));
        }

        menu.Items.Add(interval);
        menu.Items.Add(Toggle(
            $"Alerts (CPU ≥ {_settings.AlertCpuPercent:F0}%, RAM ≥ {_settings.AlertMemoryPercent:F0}%, CPU ≥ {_settings.AlertCpuTempCelsius:F0} °C, GPU ≥ {_settings.AlertGpuTempCelsius:F0} °C)",
            () => _settings.AlertsEnabled,
            on => _settings.AlertsEnabled = on));

        if (!_isAdministrator || !_pawnIoInstalled)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        if (!_isAdministrator)
        {
            menu.Items.Add("Restart as administrator (CPU temperature, per-process network)", null, async (_, _) => await RestartAsAdministratorAsync());
        }

        if (!_pawnIoInstalled)
        {
            menu.Items.Add("Get the CPU temperature driver (PawnIO)...", null, (_, _) => ShowPawnIoHelp());
        }

        // Check marks are read from the settings each time, so the tray menu and this one never disagree.
        menu.Opening += (_, _) => RefreshChecks(menu.Items);
        button.Click += (_, _) => menu.Show(button, new Point(button.Width - menu.Width, button.Height));
        return button;
    }

    // A checkable item bound to a setting; every change is saved right away.
    private ToolStripMenuItem Toggle(string text, Func<bool> isOn, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Tag = isOn };
        item.Click += (_, _) =>
        {
            set(!isOn());
            AppSettingsStore.Save(_settings);
        };
        return item;
    }

    private static void RefreshChecks(ToolStripItemCollection items)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            if (item.Tag is Func<bool> isOn)
            {
                item.Checked = isOn();
            }

            RefreshChecks(item.DropDownItems);
        }
    }

    private void CreateTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Vitals", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.Icon = Icon;
        _tray.Text = "Vitals";
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _tray.Visible = true; // also carries the alert notifications
    }

    private void OnResized()
    {
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            SendToTray();
        }
    }

    private void SendToTray()
    {
        Hide();
        if (_settings.ShowTrayWidget)
        {
            _widget.ShowInCorner();
        }
    }

    // The only way out while "Minimize or close to tray" is on (tray menu Exit, updater, restart as admin).
    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    private void RestoreFromTray()
    {
        _widget.Hide();
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    private void UpdateTrayAndAlerts(SystemSnapshot snapshot)
    {
        // NotifyIcon.Text is limited to 127 characters.
        _tray.Text = $"Vitals · CPU {snapshot.Cpu.UsagePercent:F0}% · GPU {snapshot.Gpu.UsagePercent:F0}% · RAM {snapshot.Memory.UsagePercent:F0}%";
        _widget.Update(snapshot);

        var alerts = _alerts.Evaluate(snapshot, _settings);
        if (alerts.Count > 0)
        {
            _tray.ShowBalloonTip(10000, "Vitals alert", string.Join("\n", alerts), ToolTipIcon.Warning);
        }
    }

    private void ApplyCompactView()
    {
        var compact = _settings.CompactView;
        _bottom.Visible = !compact;
        _root.RowStyles[2] = compact ? new RowStyle(SizeType.Absolute, 0) : new RowStyle(SizeType.Percent, 100F);

        // Sizes set after startup are in physical pixels, so scale the design sizes by the DPI.
        var s = DeviceDpi / 96f;
        MinimumSize = compact ? new Size((int)(880 * s), (int)(470 * s)) : new Size((int)(880 * s), (int)(740 * s));
        if (WindowState == FormWindowState.Normal)
        {
            Height = compact ? MinimumSize.Height : Math.Max(Height, (int)(800 * s));
        }
    }

    private ContextMenuStrip CreateProcessMenu()
    {
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Open file location", null, (_, _) => OpenProcessLocation());
        var end = menu.Items.Add("End process...", null, (_, _) => EndSelectedProcess());

        // Right-click selects the row under the cursor first, and the menu only opens on a real row.
        _processList.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _processList.GetItemAt(e.X, e.Y) is { } item)
            {
                item.Selected = true;
            }
        };
        menu.Opening += (_, e) => e.Cancel = SelectedProcess() is null;
        return menu;
    }

    private ProcessMetrics? SelectedProcess()
    {
        var index = _processList.SelectedIndices.Count > 0 ? _processList.SelectedIndices[0] : -1;
        return index >= 0 && index < _shownProcesses.Count ? _shownProcesses[index] : null;
    }

    private void OpenProcessLocation()
    {
        if (SelectedProcess() is not { } process)
        {
            return;
        }

        var path = ProcessImagePath(process.ProcessId);
        if (path is null)
        {
            MessageBox.Show(this, $"Can't read the location of {process.Name}. It may belong to the system or another user.", "Open file location", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void EndSelectedProcess()
    {
        if (SelectedProcess() is not { } process)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"End {process.Name} (PID {process.ProcessId})?\nUnsaved work in that program will be lost.",
            "End process",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var target = Process.GetProcessById((int)process.ProcessId);
            target.Kill();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Couldn't end {process.Name}: {ex.Message}" + (_isAdministrator ? "" : "\nRestarting Vitals as administrator may help."),
                "End process",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // Process.MainModule needs more access than limited-information handles allow, so it fails for many processes.
    private static string? ProcessImagePath(uint processId)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

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

    private static string FormatDrive(DriveMetrics drive) =>
        (drive.ActivePercent.HasValue ? $"{drive.ActivePercent.Value:F0}% · " : "") + $"{FormatBytes(drive.FreeBytes)} free";

    private Label CreateProcessHeaderLabel(string text, ContentAlignment alignment)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextTitle,
            Font = new Font(Font.FontFamily, 8.75F, FontStyle.Bold),
            TextAlign = alignment,
            Margin = new Padding(0),
        };
    }

    private void UpdateProcessColumnLayout()
    {
        if (_processList.Columns.Count < ProcessColumns.Length || _processHeader.ColumnStyles.Count < ProcessColumns.Length)
        {
            return;
        }

        // The Process name column takes whatever width the fixed columns leave.
        var fixedWidth = ProcessColumns.Sum(column => column.Width);
        var processColumnWidth = Math.Max(140, _processList.ClientSize.Width - fixedWidth - 4);

        for (var column = 0; column < ProcessColumns.Length; column++)
        {
            var width = column == 1 ? processColumnWidth : ProcessColumns[column].Width;
            _processList.Columns[column].Width = width;
            _processHeader.ColumnStyles[column].SizeType = SizeType.Absolute;
            _processHeader.ColumnStyles[column].Width = width;
        }
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
