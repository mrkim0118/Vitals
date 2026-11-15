#pragma once

#include <stdint.h>

#ifdef VITALS_NATIVE_EXPORTS
#define VITALS_API extern "C" __declspec(dllexport)
#else
#define VITALS_API extern "C" __declspec(dllimport)
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

VITALS_API int InitializeMonitor();
VITALS_API void ShutdownMonitor();
VITALS_API int SampleSystemSnapshot(SystemSnapshotNative* snapshot);
VITALS_API int SampleTopProcesses(ProcessSnapshotNative* buffer, int capacity, int* written);
VITALS_API int IsProcessNetworkAvailable();
