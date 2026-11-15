#pragma once

#include <stdint.h>

#ifdef MONITORING_NATIVE_EXPORTS
#define MONITORING_API extern "C" __declspec(dllexport)
#else
#define MONITORING_API extern "C" __declspec(dllimport)
#endif

struct CpuMetricsNative
{
    double UsagePercent;
    double CurrentClockSpeedMHz;
    double TemperatureCelsius;
    int TemperatureAvailable;
};

struct MemoryMetricsNative
{
    double UsagePercent;
    uint64_t UsedBytes;
    uint64_t TotalBytes;
};

struct DiskMetricsNative
{
    double UsagePercent;
    uint64_t FreeBytes;
    uint64_t TotalBytes;
    double ReadBytesPerSecond;
    double WriteBytesPerSecond;
};

struct GpuMetricsNative
{
    double UsagePercent;
    uint64_t DedicatedMemoryBytes;
    double TemperatureCelsius;
    int TemperatureAvailable;
};

struct NetworkMetricsNative
{
    double ReceiveBytesPerSecond;
    double SendBytesPerSecond;
    uint64_t TotalReceivedBytes;
    uint64_t TotalSentBytes;
};

struct SystemSnapshotNative
{
    CpuMetricsNative Cpu;
    MemoryMetricsNative Memory;
    DiskMetricsNative Disk;
    GpuMetricsNative Gpu;
    NetworkMetricsNative Network;
    int64_t TimestampUtcMilliseconds;
};

struct ProcessSnapshotNative
{
    uint32_t ProcessId;
    wchar_t Name[260];
    double CpuPercent;
    uint64_t WorkingSetBytes;
    double GpuPercent;
    double DiskBytesPerSecond;
    double NetworkBytesPerSecond;
};

MONITORING_API int InitializeMonitor();
MONITORING_API void ShutdownMonitor();
MONITORING_API int SampleSystemSnapshot(SystemSnapshotNative* snapshot);
MONITORING_API int SampleTopProcesses(ProcessSnapshotNative* buffer, int capacity, int* written);
MONITORING_API int IsProcessNetworkAvailable();
