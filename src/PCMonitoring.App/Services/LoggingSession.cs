using System.Globalization;
using System.IO;
using System.Text;
using PCMonitoring.App.Models;

namespace PCMonitoring.App.Services;

public sealed class LoggingSession
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly MetricAccumulator _cpuUsage = new();
    private readonly MetricAccumulator _cpuTemp = new();
    private readonly MetricAccumulator _memoryUsage = new();
    private readonly MetricAccumulator _memoryUsedGb = new();
    private readonly MetricAccumulator _diskUsage = new();
    private readonly MetricAccumulator _diskReadMb = new();
    private readonly MetricAccumulator _diskWriteMb = new();
    private readonly MetricAccumulator _gpuUsage = new();
    private readonly MetricAccumulator _gpuVramMb = new();
    private readonly MetricAccumulator _gpuTemp = new();
    private readonly MetricAccumulator _networkRxMb = new();
    private readonly MetricAccumulator _networkTxMb = new();
    private readonly Dictionary<ProcessKey, ProcessAccumulator> _processTotals = [];
    private bool _discarded;

    public LoggingSession(TimeSpan duration, string rootDirectory)
    {
        Duration = duration;
        RootDirectory = rootDirectory;
        SessionDirectory = Path.Combine(RootDirectory, $"session_{_startedAt:yyyyMMdd_HHmmss}");
    }

    public TimeSpan Duration { get; }

    public string RootDirectory { get; }

    public string SessionDirectory { get; }

    public int SampleCount { get; private set; }

    public DateTimeOffset StartedAt => _startedAt;

    public DateTimeOffset EndTime => _startedAt.Add(Duration);

    public TimeSpan Elapsed
    {
        get
        {
            var elapsed = DateTimeOffset.Now - _startedAt;
            if (elapsed < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return elapsed > Duration ? Duration : elapsed;
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = EndTime - DateTimeOffset.Now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool HasSamples => SampleCount > 0;

    public void AddSample(SystemSnapshot snapshot)
    {
        // Called from the sampling thread while Discard runs on the UI thread.
        lock (_processTotals)
        {
            AddSampleCore(snapshot);
        }
    }

    private void AddSampleCore(SystemSnapshot snapshot)
    {
        if (_discarded)
        {
            return;
        }

        SampleCount++;

        _cpuUsage.Add(snapshot.Cpu.UsagePercent);
        AddIfHasValue(_cpuTemp, snapshot.Cpu.TemperatureCelsius);
        _memoryUsage.Add(snapshot.Memory.UsagePercent);
        _memoryUsedGb.Add(snapshot.Memory.UsedBytes / 1024d / 1024d / 1024d);
        _diskUsage.Add(snapshot.Disk.UsagePercent);
        _diskReadMb.Add(snapshot.Disk.ReadBytesPerSecond / 1024d / 1024d);
        _diskWriteMb.Add(snapshot.Disk.WriteBytesPerSecond / 1024d / 1024d);
        _gpuUsage.Add(snapshot.Gpu.UsagePercent);
        _gpuVramMb.Add(snapshot.Gpu.DedicatedMemoryBytes / 1024d / 1024d);
        AddIfHasValue(_gpuTemp, snapshot.Gpu.TemperatureCelsius);
        _networkRxMb.Add(snapshot.Network.ReceiveBytesPerSecond / 1024d / 1024d);
        _networkTxMb.Add(snapshot.Network.SendBytesPerSecond / 1024d / 1024d);

        foreach (var process in snapshot.TopProcesses)
        {
            var key = new ProcessKey(process.ProcessId, process.Name);
            if (!_processTotals.TryGetValue(key, out var processAccumulator))
            {
                processAccumulator = new ProcessAccumulator();
                _processTotals.Add(key, processAccumulator);
            }

            processAccumulator.Add(process.CpuPercent);
        }
    }

    public void Discard()
    {
        lock (_processTotals)
        {
            _discarded = true;
            SampleCount = 0;
            _processTotals.Clear();
        }

        if (Directory.Exists(SessionDirectory))
        {
            Directory.Delete(SessionDirectory, recursive: true);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_discarded || SampleCount == 0)
        {
            return;
        }

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(SessionDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(SessionDirectory, "summary.txt"),
            BuildSummaryText(),
            Encoding.UTF8,
            cancellationToken);
    }

    private string BuildSummaryText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Started: {_startedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Duration: {Duration:c}");
        builder.AppendLine($"Sample Count: {SampleCount}");
        builder.AppendLine($"Log Folder: {SessionDirectory}");
        builder.AppendLine();
        builder.AppendLine("Metric Summary");
        builder.AppendLine("------------------------------");

        AppendMetricSummary(builder, "CPU Usage (%)", _cpuUsage);
        AppendMetricSummary(builder, "CPU Temp (C)", _cpuTemp);
        AppendMetricSummary(builder, "Memory Usage (%)", _memoryUsage);
        AppendMetricSummary(builder, "Memory Used (GB)", _memoryUsedGb);
        AppendMetricSummary(builder, "Disk Usage (%)", _diskUsage);
        AppendMetricSummary(builder, "Disk Read (MB/s)", _diskReadMb);
        AppendMetricSummary(builder, "Disk Write (MB/s)", _diskWriteMb);
        AppendMetricSummary(builder, "GPU Usage (%)", _gpuUsage);
        AppendMetricSummary(builder, "GPU VRAM (MB)", _gpuVramMb);
        AppendMetricSummary(builder, "GPU Temp (C)", _gpuTemp);
        AppendMetricSummary(builder, "Network RX (MB/s)", _networkRxMb);
        AppendMetricSummary(builder, "Network TX (MB/s)", _networkTxMb);

        builder.AppendLine();
        builder.AppendLine("Top Processes By Total CPU Score");
        builder.AppendLine("------------------------------");

        var topProcesses = _processTotals
            .OrderByDescending(entry => entry.Value.TotalCpuScore)
            .ThenByDescending(entry => entry.Value.PeakCpuPercent)
            .Take(5)
            .ToList();

        if (topProcesses.Count == 0)
        {
            builder.AppendLine("No process data collected.");
        }
        else
        {
            for (var index = 0; index < topProcesses.Count; index++)
            {
                var process = topProcesses[index];
                builder.AppendLine(
                    $"{index + 1}. {process.Key.Name} (PID {process.Key.ProcessId}) | total={process.Value.TotalCpuScore:F2} | peak={process.Value.PeakCpuPercent:F2}% | samples={process.Value.Samples.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return builder.ToString();
    }

    private static void AddIfHasValue(MetricAccumulator accumulator, double? value)
    {
        if (value.HasValue)
        {
            accumulator.Add(value.Value);
        }
    }

    private static void AppendMetricSummary(StringBuilder builder, string title, MetricAccumulator accumulator)
    {
        if (!accumulator.HasValues)
        {
            builder.AppendLine($"{title}: N/A");
            return;
        }

        builder.AppendLine(
            $"{title}: min={accumulator.Min:F2}, max={accumulator.Max:F2}, avg={accumulator.Average:F2}");
    }

    private readonly record struct ProcessKey(uint ProcessId, string Name);

    private sealed class ProcessAccumulator
    {
        public double TotalCpuScore { get; private set; }

        public double PeakCpuPercent { get; private set; }

        public int Samples { get; private set; }

        public void Add(double cpuPercent)
        {
            TotalCpuScore += cpuPercent;
            PeakCpuPercent = Math.Max(PeakCpuPercent, cpuPercent);
            Samples++;
        }
    }

    private sealed class MetricAccumulator
    {
        private double _sum;

        public bool HasValues { get; private set; }

        public int Count { get; private set; }

        public double Min { get; private set; }

        public double Max { get; private set; }

        public double Average => Count == 0 ? 0d : _sum / Count;

        public void Add(double value)
        {
            if (!HasValues)
            {
                Min = value;
                Max = value;
                HasValues = true;
            }
            else
            {
                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
            }

            Count++;
            _sum += value;
        }
    }
}
