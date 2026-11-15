namespace PCMonitoring.App.Models;

public sealed class CpuMetrics
{
    public double UsagePercent { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? CurrentClockSpeedMHz { get; init; }
}

public sealed class MemoryMetrics
{
    public double UsagePercent { get; init; }
    public ulong UsedBytes { get; init; }
    public ulong TotalBytes { get; init; }
}

public sealed class DiskMetrics
{
    public double UsagePercent { get; init; }
    public ulong FreeBytes { get; init; }
    public ulong TotalBytes { get; init; }
    public double ReadBytesPerSecond { get; init; }
    public double WriteBytesPerSecond { get; init; }
}

public sealed class GpuMetrics
{
    public double UsagePercent { get; init; }
    public ulong DedicatedMemoryBytes { get; init; }
    public double? TemperatureCelsius { get; init; }
}

public sealed class NetworkMetrics
{
    public double ReceiveBytesPerSecond { get; init; }
    public double SendBytesPerSecond { get; init; }
    public ulong TotalReceivedBytes { get; init; }
    public ulong TotalSentBytes { get; init; }
}

public sealed class ProcessMetrics
{
    public uint ProcessId { get; init; }
    public required string Name { get; init; }
    public double CpuPercent { get; init; }
    public ulong WorkingSetBytes { get; init; }
    public double GpuPercent { get; init; }
    public double DiskBytesPerSecond { get; init; }
    public double NetworkBytesPerSecond { get; init; }
}

public sealed class SystemSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required CpuMetrics Cpu { get; init; }
    public required MemoryMetrics Memory { get; init; }
    public required DiskMetrics Disk { get; init; }
    public required GpuMetrics Gpu { get; init; }
    public required NetworkMetrics Network { get; init; }
    public required IReadOnlyList<ProcessMetrics> TopProcesses { get; init; }
}
