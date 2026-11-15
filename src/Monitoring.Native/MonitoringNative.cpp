#define _WIN32_WINNT 0x0A00
#define NOMINMAX

#include "MonitoringNativeExports.h"

// winsock2/ws2ipdef must precede iphlpapi so netioapi.h (GetIfTable2) gets its types.
#include <winsock2.h>
#include <ws2ipdef.h>
#include <windows.h>
#include <iphlpapi.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <evntrace.h>
#include <evntcons.h>

#include <algorithm>
#include <cmath>
#include <cwchar>
#include <mutex>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <vector>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "pdh.lib")
#pragma comment(lib, "psapi.lib")

namespace
{
    struct NetworkState
    {
        uint64_t InOctets = 0;
        uint64_t OutOctets = 0;
        uint64_t TimestampMilliseconds = 0;
        bool HasBaseline = false;
    };

    struct ProcessState
    {
        uint64_t LastTotalTime100Ns = 0;
        uint64_t LastIoBytes = 0;
    };

    struct MonitorState
    {
        FILETIME LastIdleTime{};
        FILETIME LastKernelTime{};
        FILETIME LastUserTime{};
        bool CpuBaselineReady = false;

        PDH_HQUERY CpuQuery = nullptr;
        PDH_HCOUNTER CpuPerformanceCounter = nullptr;
        PDH_HCOUNTER CpuBaseFrequencyCounter = nullptr;

        PDH_HQUERY DiskQuery = nullptr;
        PDH_HCOUNTER DiskIdleCounter = nullptr;
        PDH_HCOUNTER DiskReadCounter = nullptr;
        PDH_HCOUNTER DiskWriteCounter = nullptr;

        PDH_HQUERY GpuQuery = nullptr;
        PDH_HCOUNTER GpuUtilizationCounter = nullptr;
        PDH_HCOUNTER GpuDedicatedMemoryCounter = nullptr;
        bool GpuCountersAvailable = false;
        // Busiest engine per process from the last GPU sample (Task Manager's per-process GPU %).
        std::unordered_map<uint32_t, double> GpuByProcess;

        NetworkState Network{};
        std::unordered_map<uint32_t, ProcessState> ProcessTimes;
        uint64_t LastProcessSampleTick = 0;
        DWORD ProcessorCount = 1;
        bool Initialized = false;
    };

    MonitorState g_state;
    std::mutex g_mutex;

    // Per-process network bytes from ETW (Microsoft-Windows-Kernel-Network). Starting a trace session
    // needs admin rights, so this stays off for a normal user and the UI shows N/A.
    namespace ProcessNetwork
    {
        constexpr wchar_t SessionName[] = L"TechArchivePCMonitoringNet";
        constexpr GUID KernelNetworkProvider =
            { 0x7dd42a49, 0x5329, 0x4832, { 0x8d, 0xfd, 0x43, 0xd9, 0x79, 0x15, 0x3a, 0x88 } };

        std::mutex BytesMutex;
        std::unordered_map<uint32_t, uint64_t> BytesByProcess;
        TRACEHANDLE Session = 0;
        TRACEHANDLE Consumer = INVALID_PROCESSTRACE_HANDLE;
        std::thread Worker;
        bool Running = false;

        struct SessionProperties
        {
            EVENT_TRACE_PROPERTIES Properties;
            wchar_t Name[64];
        };

        SessionProperties MakeProperties()
        {
            SessionProperties value{};
            value.Properties.Wnode.BufferSize = sizeof(SessionProperties);
            value.Properties.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            value.Properties.Wnode.ClientContext = 1; // QPC timestamps
            value.Properties.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
            value.Properties.BufferSize = 64;          // KB
            value.Properties.FlushTimer = 1;           // seconds
            value.Properties.LoggerNameOffset = offsetof(SessionProperties, Name);
            return value;
        }

        void WINAPI OnEvent(PEVENT_RECORD record)
        {
            // TCP send/recv v4/v6 = 10/11/26/27, UDP = 42/43/58/59; payload starts with PID, size (both UINT32).
            const USHORT id = record->EventHeader.EventDescriptor.Id;
            const bool transfer = id == 10 || id == 11 || id == 26 || id == 27 ||
                                  id == 42 || id == 43 || id == 58 || id == 59;
            if (!transfer || record->UserDataLength < 8 ||
                !IsEqualGUID(record->EventHeader.ProviderId, KernelNetworkProvider))
            {
                return;
            }

            const auto* payload = static_cast<const uint32_t*>(record->UserData);
            std::scoped_lock lock(BytesMutex);
            BytesByProcess[payload[0]] += payload[1];
        }

        void Start()
        {
            // A session left over from a crashed run would make StartTrace fail with ALREADY_EXISTS.
            auto stale = MakeProperties();
            ControlTraceW(0, SessionName, &stale.Properties, EVENT_TRACE_CONTROL_STOP);

            auto properties = MakeProperties();
            if (StartTraceW(&Session, SessionName, &properties.Properties) != ERROR_SUCCESS)
            {
                Session = 0;
                return; // not elevated (ERROR_ACCESS_DENIED) or ETW unavailable
            }

            if (EnableTraceEx2(Session, &KernelNetworkProvider, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                               TRACE_LEVEL_INFORMATION, 0x30 /* IPv4 | IPv6 */, 0, 0, nullptr) != ERROR_SUCCESS)
            {
                auto stop = MakeProperties();
                ControlTraceW(Session, nullptr, &stop.Properties, EVENT_TRACE_CONTROL_STOP);
                Session = 0;
                return;
            }

            EVENT_TRACE_LOGFILEW logFile{};
            logFile.LoggerName = const_cast<LPWSTR>(SessionName);
            logFile.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;
            logFile.EventRecordCallback = OnEvent;
            Consumer = OpenTraceW(&logFile);
            if (Consumer == INVALID_PROCESSTRACE_HANDLE)
            {
                auto stop = MakeProperties();
                ControlTraceW(Session, nullptr, &stop.Properties, EVENT_TRACE_CONTROL_STOP);
                Session = 0;
                return;
            }

            Worker = std::thread([] { ProcessTrace(&Consumer, 1, nullptr, nullptr); });
            Running = true;
        }

        void Stop()
        {
            if (Session != 0)
            {
                auto stop = MakeProperties();
                ControlTraceW(Session, nullptr, &stop.Properties, EVENT_TRACE_CONTROL_STOP);
                Session = 0;
            }

            if (Consumer != INVALID_PROCESSTRACE_HANDLE)
            {
                CloseTrace(Consumer); // makes ProcessTrace return
                Consumer = INVALID_PROCESSTRACE_HANDLE;
            }

            if (Worker.joinable())
            {
                Worker.join();
            }

            Running = false;
            std::scoped_lock lock(BytesMutex);
            BytesByProcess.clear();
        }

        // Bytes per process since the previous call.
        std::unordered_map<uint32_t, uint64_t> TakeBytes()
        {
            std::unordered_map<uint32_t, uint64_t> taken;
            std::scoped_lock lock(BytesMutex);
            taken.swap(BytesByProcess);
            return taken;
        }
    }

    uint64_t FileTimeToUInt64(const FILETIME& fileTime)
    {
        ULARGE_INTEGER value{};
        value.LowPart = fileTime.dwLowDateTime;
        value.HighPart = fileTime.dwHighDateTime;
        return value.QuadPart;
    }

    uint64_t GetUtcMilliseconds()
    {
        FILETIME systemTime{};
        GetSystemTimeAsFileTime(&systemTime);
        constexpr uint64_t fileTimeToUnixEpochOffset = 116444736000000000ULL;
        return (FileTimeToUInt64(systemTime) - fileTimeToUnixEpochOffset) / 10000ULL;
    }

    bool AddEnglishCounter(PDH_HQUERY query, const wchar_t* path, PDH_HCOUNTER* counter)
    {
        PDH_STATUS status = PdhAddEnglishCounterW(query, path, 0, counter);
        return status == ERROR_SUCCESS;
    }

    bool OpenCounterQuery(PDH_HQUERY* query, const std::vector<std::pair<const wchar_t*, PDH_HCOUNTER*>>& counters)
    {
        if (PdhOpenQueryW(nullptr, 0, query) != ERROR_SUCCESS)
        {
            return false;
        }

        for (const auto& entry : counters)
        {
            if (!AddEnglishCounter(*query, entry.first, entry.second))
            {
                PdhCloseQuery(*query);
                *query = nullptr;
                return false;
            }
        }

        PdhCollectQueryData(*query);
        return true;
    }

    double QuerySingleCounter(PDH_HCOUNTER counter)
    {
        PDH_FMT_COUNTERVALUE value{};
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, nullptr, &value) != ERROR_SUCCESS)
        {
            return 0.0;
        }

        if (value.CStatus != ERROR_SUCCESS)
        {
            return 0.0;
        }

        return value.doubleValue;
    }

    double QueryWildcardCounterTotal(PDH_HCOUNTER counter)
    {
        DWORD bufferSize = 0;
        DWORD itemCount = 0;
        PDH_STATUS status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bufferSize, &itemCount, nullptr);
        if (status != PDH_MORE_DATA || bufferSize == 0 || itemCount == 0)
        {
            return 0.0;
        }

        std::vector<BYTE> buffer(bufferSize);
        auto* values = reinterpret_cast<PDH_FMT_COUNTERVALUE_ITEM_W*>(buffer.data());
        status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bufferSize, &itemCount, values);
        if (status != ERROR_SUCCESS)
        {
            return 0.0;
        }

        double total = 0.0;
        for (DWORD i = 0; i < itemCount; ++i)
        {
            if (values[i].FmtValue.CStatus == ERROR_SUCCESS)
            {
                total += values[i].FmtValue.doubleValue;
            }
        }

        return total;
    }

    // Instances look like "pid_1234_luid_0x0_0xC5E5_phys_0_eng_3_engtype_3D".
    // Summing every instance double-counts engines that run in parallel, so group by engine.
    double QueryGpuBusiestEngine(PDH_HCOUNTER counter)
    {
        DWORD bufferSize = 0;
        DWORD itemCount = 0;
        PDH_STATUS status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bufferSize, &itemCount, nullptr);
        if (status != PDH_MORE_DATA || bufferSize == 0 || itemCount == 0)
        {
            return 0.0;
        }

        std::vector<BYTE> buffer(bufferSize);
        auto* values = reinterpret_cast<PDH_FMT_COUNTERVALUE_ITEM_W*>(buffer.data());
        status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bufferSize, &itemCount, values);
        if (status != ERROR_SUCCESS)
        {
            return 0.0;
        }

        std::unordered_map<std::wstring, double> engineTotals;
        g_state.GpuByProcess.clear();
        double busiest = 0.0;
        for (DWORD i = 0; i < itemCount; ++i)
        {
            if (values[i].FmtValue.CStatus != ERROR_SUCCESS || values[i].szName == nullptr)
            {
                continue;
            }

            const std::wstring_view name(values[i].szName);
            if (name.rfind(L"pid_", 0) == 0)
            {
                const uint32_t pid = static_cast<uint32_t>(wcstoul(values[i].szName + 4, nullptr, 10));
                double& processBusiest = g_state.GpuByProcess[pid];
                processBusiest = std::max(processBusiest, values[i].FmtValue.doubleValue);
            }

            const size_t luid = name.find(L"luid_");
            const size_t type = name.find(L"_engtype_");
            const std::wstring key(luid == std::wstring_view::npos
                ? name
                : name.substr(luid, type == std::wstring_view::npos ? std::wstring_view::npos : type - luid));

            double& total = engineTotals[key];
            total += values[i].FmtValue.doubleValue;
            busiest = std::max(busiest, total);
        }

        return busiest;
    }

    void QueryDriveSpace(uint64_t& totalBytes, uint64_t& freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;

        DWORD length = GetLogicalDriveStringsW(0, nullptr);
        if (length == 0)
        {
            return;
        }

        std::vector<wchar_t> drives(length + 1, L'\0');
        if (GetLogicalDriveStringsW(length, drives.data()) == 0)
        {
            return;
        }

        for (const wchar_t* current = drives.data(); *current != L'\0'; current += wcslen(current) + 1)
        {
            if (GetDriveTypeW(current) != DRIVE_FIXED)
            {
                continue;
            }

            ULARGE_INTEGER freeAvailable{};
            ULARGE_INTEGER total{};
            ULARGE_INTEGER free{};
            if (GetDiskFreeSpaceExW(current, &freeAvailable, &total, &free))
            {
                totalBytes += total.QuadPart;
                freeBytes += free.QuadPart;
            }
        }
    }

    void SampleNetwork(NetworkMetricsNative& network)
    {
        network.ReceiveBytesPerSecond = 0.0;
        network.SendBytesPerSecond = 0.0;
        network.TotalReceivedBytes = 0;
        network.TotalSentBytes = 0;

        MIB_IF_TABLE2* table = nullptr;
        if (GetIfTable2(&table) != NO_ERROR || table == nullptr)
        {
            return;
        }

        uint64_t totalIn = 0;
        uint64_t totalOut = 0;

        for (ULONG i = 0; i < table->NumEntries; ++i)
        {
            const MIB_IF_ROW2& row = table->Table[i];
            if (row.OperStatus != IfOperStatusUp ||
                !row.InterfaceAndOperStatusFlags.HardwareInterface ||
                row.InterfaceAndOperStatusFlags.FilterInterface)
            {
                continue;
            }

            totalIn += row.InOctets;
            totalOut += row.OutOctets;
        }

        FreeMibTable(table);

        const uint64_t now = GetTickCount64();
        network.TotalReceivedBytes = totalIn;
        network.TotalSentBytes = totalOut;

        // Totals drop when an adapter goes away or resets; skip that sample instead of reporting garbage.
        if (g_state.Network.HasBaseline && totalIn >= g_state.Network.InOctets && totalOut >= g_state.Network.OutOctets)
        {
            const double elapsedSeconds = static_cast<double>(now - g_state.Network.TimestampMilliseconds) / 1000.0;
            if (elapsedSeconds > 0.0)
            {
                network.ReceiveBytesPerSecond =
                    static_cast<double>(totalIn - g_state.Network.InOctets) / elapsedSeconds;
                network.SendBytesPerSecond =
                    static_cast<double>(totalOut - g_state.Network.OutOctets) / elapsedSeconds;
            }
        }

        g_state.Network.InOctets = totalIn;
        g_state.Network.OutOctets = totalOut;
        g_state.Network.TimestampMilliseconds = now;
        g_state.Network.HasBaseline = true;
    }

    double QueryCpuUsage()
    {
        FILETIME idleTime{};
        FILETIME kernelTime{};
        FILETIME userTime{};
        if (!GetSystemTimes(&idleTime, &kernelTime, &userTime))
        {
            return 0.0;
        }

        if (!g_state.CpuBaselineReady)
        {
            g_state.LastIdleTime = idleTime;
            g_state.LastKernelTime = kernelTime;
            g_state.LastUserTime = userTime;
            g_state.CpuBaselineReady = true;
            return 0.0;
        }

        const uint64_t idleDelta = FileTimeToUInt64(idleTime) - FileTimeToUInt64(g_state.LastIdleTime);
        const uint64_t kernelDelta = FileTimeToUInt64(kernelTime) - FileTimeToUInt64(g_state.LastKernelTime);
        const uint64_t userDelta = FileTimeToUInt64(userTime) - FileTimeToUInt64(g_state.LastUserTime);
        const uint64_t totalDelta = kernelDelta + userDelta;

        g_state.LastIdleTime = idleTime;
        g_state.LastKernelTime = kernelTime;
        g_state.LastUserTime = userTime;

        if (totalDelta == 0)
        {
            return 0.0;
        }

        const double usage = (1.0 - (static_cast<double>(idleDelta) / static_cast<double>(totalDelta))) * 100.0;
        return std::clamp(usage, 0.0, 100.0);
    }

    // Same formula as Task Manager "Speed": nominal MHz x % Processor Performance (includes turbo).
    double QueryCpuClockSpeedMHz()
    {
        if (g_state.CpuQuery == nullptr)
        {
            return 0.0;
        }

        PdhCollectQueryData(g_state.CpuQuery);
        const double baseMHz = QuerySingleCounter(g_state.CpuBaseFrequencyCounter);
        const double performancePercent = QuerySingleCounter(g_state.CpuPerformanceCounter);
        const double mhz = baseMHz * performancePercent / 100.0;
        return (mhz > 0.0 && mhz < 20000.0) ? mhz : 0.0;
    }

    void SampleMemory(MemoryMetricsNative& memory)
    {
        MEMORYSTATUSEX status{};
        status.dwLength = sizeof(status);
        if (!GlobalMemoryStatusEx(&status))
        {
            return;
        }

        memory.TotalBytes = status.ullTotalPhys;
        memory.UsedBytes = status.ullTotalPhys - status.ullAvailPhys;
        memory.UsagePercent = status.dwMemoryLoad;
    }

    void SampleDisk(DiskMetricsNative& disk)
    {
        if (g_state.DiskQuery != nullptr)
        {
            PdhCollectQueryData(g_state.DiskQuery);
            disk.UsagePercent = std::clamp(100.0 - QuerySingleCounter(g_state.DiskIdleCounter), 0.0, 100.0);
            disk.ReadBytesPerSecond = std::max(0.0, QuerySingleCounter(g_state.DiskReadCounter));
            disk.WriteBytesPerSecond = std::max(0.0, QuerySingleCounter(g_state.DiskWriteCounter));
        }

        QueryDriveSpace(disk.TotalBytes, disk.FreeBytes);
    }

    void SampleGpu(GpuMetricsNative& gpu)
    {
        gpu.TemperatureAvailable = 0;
        gpu.TemperatureCelsius = 0.0;

        if (!g_state.GpuCountersAvailable || g_state.GpuQuery == nullptr)
        {
            return;
        }

        PdhCollectQueryData(g_state.GpuQuery);
        const double utilization = QueryGpuBusiestEngine(g_state.GpuUtilizationCounter);
        gpu.UsagePercent = std::clamp(utilization, 0.0, 100.0);
        gpu.DedicatedMemoryBytes = static_cast<uint64_t>(std::max(0.0, QueryWildcardCounterTotal(g_state.GpuDedicatedMemoryCounter)));
    }

    bool TryInitializePdh()
    {
        const bool diskReady = OpenCounterQuery(
            &g_state.DiskQuery,
            {
                {L"\\PhysicalDisk(_Total)\\% Idle Time", &g_state.DiskIdleCounter},
                {L"\\PhysicalDisk(_Total)\\Disk Read Bytes/sec", &g_state.DiskReadCounter},
                {L"\\PhysicalDisk(_Total)\\Disk Write Bytes/sec", &g_state.DiskWriteCounter},
            });

        const bool gpuReady = OpenCounterQuery(
            &g_state.GpuQuery,
            {
                {L"\\GPU Engine(*)\\Utilization Percentage", &g_state.GpuUtilizationCounter},
                {L"\\GPU Adapter Memory(*)\\Dedicated Usage", &g_state.GpuDedicatedMemoryCounter},
            });

        g_state.GpuCountersAvailable = gpuReady;

        // Optional: clock shows N/A if this counter set is missing.
        OpenCounterQuery(
            &g_state.CpuQuery,
            {
                {L"\\Processor Information(_Total)\\% Processor Performance", &g_state.CpuPerformanceCounter},
                {L"\\Processor Information(_Total)\\Processor Frequency", &g_state.CpuBaseFrequencyCounter},
            });

        return diskReady;
    }

    void ClosePdh()
    {
        if (g_state.CpuQuery != nullptr)
        {
            PdhCloseQuery(g_state.CpuQuery);
            g_state.CpuQuery = nullptr;
        }

        if (g_state.DiskQuery != nullptr)
        {
            PdhCloseQuery(g_state.DiskQuery);
            g_state.DiskQuery = nullptr;
        }

        if (g_state.GpuQuery != nullptr)
        {
            PdhCloseQuery(g_state.GpuQuery);
            g_state.GpuQuery = nullptr;
        }
    }
}

int InitializeMonitor()
{
    std::scoped_lock lock(g_mutex);
    if (g_state.Initialized)
    {
        return 1;
    }

    g_state = MonitorState{};
    g_state.ProcessorCount = std::max<DWORD>(1, GetActiveProcessorCount(ALL_PROCESSOR_GROUPS));

    if (!TryInitializePdh())
    {
        ClosePdh();
        return 0;
    }

    ProcessNetwork::Start();
    g_state.Initialized = true;
    return 1;
}

void ShutdownMonitor()
{
    std::scoped_lock lock(g_mutex);
    ProcessNetwork::Stop();
    ClosePdh();
    g_state.ProcessTimes.clear();
    g_state.Initialized = false;
}

int SampleSystemSnapshot(SystemSnapshotNative* snapshot)
{
    if (snapshot == nullptr)
    {
        return 0;
    }

    std::scoped_lock lock(g_mutex);
    if (!g_state.Initialized)
    {
        return 0;
    }

    ZeroMemory(snapshot, sizeof(SystemSnapshotNative));
    snapshot->TimestampUtcMilliseconds = static_cast<int64_t>(GetUtcMilliseconds());
    snapshot->Cpu.UsagePercent = QueryCpuUsage();
    snapshot->Cpu.CurrentClockSpeedMHz = QueryCpuClockSpeedMHz();
    SampleMemory(snapshot->Memory);
    SampleDisk(snapshot->Disk);
    SampleGpu(snapshot->Gpu);
    SampleNetwork(snapshot->Network);
    return 1;
}

int SampleTopProcesses(ProcessSnapshotNative* buffer, int capacity, int* written)
{
    if (buffer == nullptr || capacity <= 0 || written == nullptr)
    {
        return 0;
    }

    std::scoped_lock lock(g_mutex);
    if (!g_state.Initialized)
    {
        return 0;
    }

    HANDLE snapshotHandle = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshotHandle == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);

    const uint64_t now = GetTickCount64();
    const bool hasBaseline = g_state.LastProcessSampleTick != 0;
    const double elapsedSeconds =
        hasBaseline ? static_cast<double>(now - g_state.LastProcessSampleTick) / 1000.0 : 0.0;
    g_state.LastProcessSampleTick = now;

    std::unordered_map<uint32_t, ProcessState> nextState;
    std::vector<ProcessSnapshotNative> candidates;
    const auto networkBytes = ProcessNetwork::TakeBytes();

    if (Process32FirstW(snapshotHandle, &entry))
    {
        do
        {
            HANDLE processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
            if (processHandle == nullptr)
            {
                continue;
            }

            FILETIME createTime{};
            FILETIME exitTime{};
            FILETIME kernelTime{};
            FILETIME userTime{};
            if (!GetProcessTimes(processHandle, &createTime, &exitTime, &kernelTime, &userTime))
            {
                CloseHandle(processHandle);
                continue;
            }

            const uint64_t totalTime = FileTimeToUInt64(kernelTime) + FileTimeToUInt64(userTime);
            // Read+write transfer bytes: file I/O including cache hits, the closest unprivileged stand-in for disk use.
            IO_COUNTERS io{};
            const uint64_t ioBytes = GetProcessIoCounters(processHandle, &io)
                ? io.ReadTransferCount + io.WriteTransferCount
                : 0;
            nextState[entry.th32ProcessID] = ProcessState{ totalTime, ioBytes };

            PROCESS_MEMORY_COUNTERS_EX memoryCounters{};
            memoryCounters.cb = sizeof(memoryCounters);
            GetProcessMemoryInfo(processHandle, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memoryCounters), sizeof(memoryCounters));

            double cpuPercent = 0.0;
            double diskBytesPerSecond = 0.0;
            const auto previous = g_state.ProcessTimes.find(entry.th32ProcessID);
            if (hasBaseline && elapsedSeconds > 0.0 && previous != g_state.ProcessTimes.end())
            {
                const uint64_t delta = totalTime >= previous->second.LastTotalTime100Ns
                    ? (totalTime - previous->second.LastTotalTime100Ns)
                    : 0;

                const double maxTime = elapsedSeconds * 10000000.0 * static_cast<double>(g_state.ProcessorCount);
                if (maxTime > 0.0)
                {
                    cpuPercent = std::clamp((static_cast<double>(delta) / maxTime) * 100.0, 0.0, 100.0);
                }

                if (ioBytes >= previous->second.LastIoBytes)
                {
                    diskBytesPerSecond = static_cast<double>(ioBytes - previous->second.LastIoBytes) / elapsedSeconds;
                }
            }

            double networkBytesPerSecond = 0.0;
            const auto network = networkBytes.find(entry.th32ProcessID);
            if (hasBaseline && elapsedSeconds > 0.0 && network != networkBytes.end())
            {
                networkBytesPerSecond = static_cast<double>(network->second) / elapsedSeconds;
            }

            const auto gpu = g_state.GpuByProcess.find(entry.th32ProcessID);
            const double gpuPercent = gpu == g_state.GpuByProcess.end() ? 0.0 : std::clamp(gpu->second, 0.0, 100.0);

            if (cpuPercent > 0.1 || gpuPercent > 0.1 || diskBytesPerSecond > 0.0 || networkBytesPerSecond > 0.0 ||
                memoryCounters.WorkingSetSize > (10ULL * 1024ULL * 1024ULL))
            {
                ProcessSnapshotNative process{};
                process.ProcessId = entry.th32ProcessID;
                wcsncpy_s(process.Name, entry.szExeFile, _TRUNCATE);
                process.CpuPercent = cpuPercent;
                process.WorkingSetBytes = static_cast<uint64_t>(memoryCounters.WorkingSetSize);
                process.GpuPercent = gpuPercent;
                process.DiskBytesPerSecond = diskBytesPerSecond;
                process.NetworkBytesPerSecond = networkBytesPerSecond;
                candidates.push_back(process);
            }

            CloseHandle(processHandle);
        } while (Process32NextW(snapshotHandle, &entry));
    }

    CloseHandle(snapshotHandle);
    g_state.ProcessTimes = std::move(nextState);

    std::sort(
        candidates.begin(),
        candidates.end(),
        [](const ProcessSnapshotNative& left, const ProcessSnapshotNative& right)
        {
            if (std::fabs(left.CpuPercent - right.CpuPercent) > 0.001)
            {
                return left.CpuPercent > right.CpuPercent;
            }

            return left.WorkingSetBytes > right.WorkingSetBytes;
        });

    const int count = std::min(capacity, static_cast<int>(candidates.size()));
    for (int i = 0; i < count; ++i)
    {
        buffer[i] = candidates[i];
    }

    *written = count;
    return 1;
}

int IsProcessNetworkAvailable()
{
    std::scoped_lock lock(g_mutex);
    return ProcessNetwork::Running ? 1 : 0;
}
