using System.Runtime.InteropServices;
using Vitals.App.Models;

namespace Vitals.App.Native;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CpuMetricsNative
    {
        public double UsagePercent;
        public double CurrentClockSpeedMHz;
        public double TemperatureCelsius;
        public int TemperatureAvailable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryMetricsNative
    {
        public double UsagePercent;
        public ulong UsedBytes;
        public ulong TotalBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskMetricsNative
    {
        public double UsagePercent;
        public ulong FreeBytes;
        public ulong TotalBytes;
        public double ReadBytesPerSecond;
        public double WriteBytesPerSecond;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuMetricsNative
    {
        public double UsagePercent;
        public ulong DedicatedMemoryBytes;
        public double TemperatureCelsius;
        public int TemperatureAvailable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NetworkMetricsNative
    {
        public double ReceiveBytesPerSecond;
        public double SendBytesPerSecond;
        public ulong TotalReceivedBytes;
        public ulong TotalSentBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemSnapshotNative
    {
        public CpuMetricsNative Cpu;
        public MemoryMetricsNative Memory;
        public DiskMetricsNative Disk;
        public GpuMetricsNative Gpu;
        public NetworkMetricsNative Network;
        public long TimestampUtcMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessSnapshotNative
    {
        public uint ProcessId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Name;

        public double CpuPercent;
        public ulong WorkingSetBytes;
        public double GpuPercent;
        public double DiskBytesPerSecond;
        public double NetworkBytesPerSecond;
    }

    [DllImport("Vitals.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeMonitor();

    [DllImport("Vitals.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShutdownMonitor();

    [DllImport("Vitals.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SampleSystemSnapshot(out SystemSnapshotNative snapshot);

    [DllImport("Vitals.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SampleTopProcesses([Out] ProcessSnapshotNative[] buffer, int capacity, out int written);

    [DllImport("Vitals.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int IsProcessNetworkAvailable();

    public static bool Initialize() => InitializeMonitor() == 1;

    // True only when the ETW network session started, which needs admin rights.
    public static bool ProcessNetworkAvailable => IsProcessNetworkAvailable() == 1;

    public static void Shutdown() => ShutdownMonitor();

    public static SystemSnapshot? TrySampleSystem(IReadOnlyList<ProcessMetrics> topProcesses)
    {
        if (SampleSystemSnapshot(out var snapshot) != 1)
        {
            return null;
        }

        return new SystemSnapshot
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.TimestampUtcMilliseconds),
            Cpu = new CpuMetrics
            {
                UsagePercent = snapshot.Cpu.UsagePercent,
                TemperatureCelsius = snapshot.Cpu.TemperatureAvailable == 1 ? snapshot.Cpu.TemperatureCelsius : null,
                CurrentClockSpeedMHz = snapshot.Cpu.CurrentClockSpeedMHz > 0 ? snapshot.Cpu.CurrentClockSpeedMHz : null,
            },
            Memory = new MemoryMetrics
            {
                UsagePercent = snapshot.Memory.UsagePercent,
                UsedBytes = snapshot.Memory.UsedBytes,
                TotalBytes = snapshot.Memory.TotalBytes,
            },
            Disk = new DiskMetrics
            {
                UsagePercent = snapshot.Disk.UsagePercent,
                FreeBytes = snapshot.Disk.FreeBytes,
                TotalBytes = snapshot.Disk.TotalBytes,
                ReadBytesPerSecond = snapshot.Disk.ReadBytesPerSecond,
                WriteBytesPerSecond = snapshot.Disk.WriteBytesPerSecond,
            },
            Gpu = new GpuMetrics
            {
                UsagePercent = snapshot.Gpu.UsagePercent,
                DedicatedMemoryBytes = snapshot.Gpu.DedicatedMemoryBytes,
                TemperatureCelsius = snapshot.Gpu.TemperatureAvailable == 1 ? snapshot.Gpu.TemperatureCelsius : null,
            },
            Network = new NetworkMetrics
            {
                ReceiveBytesPerSecond = snapshot.Network.ReceiveBytesPerSecond,
                SendBytesPerSecond = snapshot.Network.SendBytesPerSecond,
                TotalReceivedBytes = snapshot.Network.TotalReceivedBytes,
                TotalSentBytes = snapshot.Network.TotalSentBytes,
            },
            TopProcesses = topProcesses,
        };
    }

    public static IReadOnlyList<ProcessMetrics> TrySampleTopProcesses()
    {
        // Native already drops idle tiny processes (<0.1% CPU and <10 MB); take all the rest so the UI can sort by any column.
        // shortcut: a PC with more than 512 such processes loses the lowest-CPU ones; raise the size if that ever matters.
        var buffer = new ProcessSnapshotNative[512];
        if (SampleTopProcesses(buffer, buffer.Length, out var written) != 1 || written <= 0)
        {
            return Array.Empty<ProcessMetrics>();
        }

        var items = new List<ProcessMetrics>(written);
        for (var index = 0; index < written; index++)
        {
            items.Add(new ProcessMetrics
            {
                ProcessId = buffer[index].ProcessId,
                Name = buffer[index].Name,
                CpuPercent = buffer[index].CpuPercent,
                WorkingSetBytes = buffer[index].WorkingSetBytes,
                GpuPercent = buffer[index].GpuPercent,
                DiskBytesPerSecond = buffer[index].DiskBytesPerSecond,
                NetworkBytesPerSecond = buffer[index].NetworkBytesPerSecond,
            });
        }

        return items;
    }
}
