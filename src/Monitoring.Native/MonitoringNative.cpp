#define _WIN32_WINNT 0x0A00
#define NOMINMAX
#define _WIN32_DCOM

#include "MonitoringNativeExports.h"

#include <windows.h>
#include <iphlpapi.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <wbemidl.h>
#include <comdef.h>

#include <algorithm>
#include <cmath>
#include <cwchar>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "pdh.lib")
#pragma comment(lib, "wbemuuid.lib")
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
    };

    struct MonitorState
    {
        FILETIME LastIdleTime{};
        FILETIME LastKernelTime{};
        FILETIME LastUserTime{};
        bool CpuBaselineReady = false;

        PDH_HQUERY DiskQuery = nullptr;
        PDH_HCOUNTER DiskUsageCounter = nullptr;
        PDH_HCOUNTER DiskReadCounter = nullptr;
        PDH_HCOUNTER DiskWriteCounter = nullptr;

        PDH_HQUERY GpuQuery = nullptr;
        PDH_HCOUNTER GpuUtilizationCounter = nullptr;
        PDH_HCOUNTER GpuDedicatedMemoryCounter = nullptr;
        bool GpuCountersAvailable = false;

        NetworkState Network{};
        std::unordered_map<uint32_t, ProcessState> ProcessTimes;
        uint64_t LastProcessSampleMilliseconds = 0;
        uint64_t LastCpuTemperatureQueryMilliseconds = 0;
        double CachedCpuTemperatureCelsius = 0.0;
        bool CachedCpuTemperatureAvailable = false;
        uint64_t LastCpuClockQueryMilliseconds = 0;
        double CachedCpuClockMHz = 0.0;
        bool CachedCpuClockAvailable = false;
        SYSTEM_INFO SystemInfo{};
        bool Initialized = false;
    };

    MonitorState g_state;
    std::mutex g_mutex;

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

        ULONG tableSize = 0;
        GetIfTable(nullptr, &tableSize, FALSE);
        if (tableSize == 0)
        {
            return;
        }

        std::vector<BYTE> tableBuffer(tableSize);
        auto* table = reinterpret_cast<MIB_IFTABLE*>(tableBuffer.data());
        if (GetIfTable(table, &tableSize, FALSE) != NO_ERROR)
        {
            return;
        }

        uint64_t totalIn = 0;
        uint64_t totalOut = 0;

        for (DWORD i = 0; i < table->dwNumEntries; ++i)
        {
            const MIB_IFROW& row = table->table[i];
            if (row.dwOperStatus != IF_OPER_STATUS_OPERATIONAL)
            {
                continue;
            }

            if (row.dwType == IF_TYPE_SOFTWARE_LOOPBACK || row.dwType == IF_TYPE_TUNNEL)
            {
                continue;
            }

            totalIn += row.dwInOctets;
            totalOut += row.dwOutOctets;
        }

        const uint64_t now = GetUtcMilliseconds();
        network.TotalReceivedBytes = totalIn;
        network.TotalSentBytes = totalOut;

        if (g_state.Network.HasBaseline)
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

    double QueryCpuTemperature(bool& available)
    {
        available = false;

        HRESULT initResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        const bool shouldUninitialize = SUCCEEDED(initResult);
        if (FAILED(initResult) && initResult != RPC_E_CHANGED_MODE)
        {
            return 0.0;
        }

        HRESULT hr = CoInitializeSecurity(
            nullptr,
            -1,
            nullptr,
            nullptr,
            RPC_C_AUTHN_LEVEL_DEFAULT,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr,
            EOAC_NONE,
            nullptr);

        if (FAILED(hr) && hr != RPC_E_TOO_LATE)
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        IWbemLocator* locator = nullptr;
        hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER, IID_IWbemLocator, reinterpret_cast<void**>(&locator));
        if (FAILED(hr) || locator == nullptr)
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        IWbemServices* services = nullptr;
        hr = locator->ConnectServer(
            _bstr_t(L"ROOT\\WMI"),
            nullptr,
            nullptr,
            nullptr,
            0,
            nullptr,
            nullptr,
            &services);

        if (FAILED(hr) || services == nullptr)
        {
            locator->Release();
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        CoSetProxyBlanket(
            services,
            RPC_C_AUTHN_WINNT,
            RPC_C_AUTHZ_NONE,
            nullptr,
            RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr,
            EOAC_NONE);

        IEnumWbemClassObject* enumerator = nullptr;
        hr = services->ExecQuery(
            _bstr_t(L"WQL"),
            _bstr_t(L"SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"),
            WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
            nullptr,
            &enumerator);

        double totalTemperature = 0.0;
        double count = 0.0;

        if (SUCCEEDED(hr) && enumerator != nullptr)
        {
            while (true)
            {
                IWbemClassObject* instance = nullptr;
                ULONG returned = 0;
                hr = enumerator->Next(2000, 1, &instance, &returned);
                if (FAILED(hr) || returned == 0 || instance == nullptr)
                {
                    break;
                }

                VARIANT value{};
                VariantInit(&value);
                if (SUCCEEDED(instance->Get(L"CurrentTemperature", 0, &value, nullptr, nullptr)))
                {
                    if (value.vt == VT_I4 || value.vt == VT_UI4)
                    {
                        const double celsius = (static_cast<double>(value.ulVal) / 10.0) - 273.15;
                        if (celsius > -40.0 && celsius < 150.0)
                        {
                            totalTemperature += celsius;
                            count += 1.0;
                        }
                    }
                }

                VariantClear(&value);
                instance->Release();
            }

            enumerator->Release();
        }

        services->Release();
        locator->Release();

        if (shouldUninitialize)
        {
            CoUninitialize();
        }

        if (count > 0.0)
        {
            available = true;
            return totalTemperature / count;
        }

        return 0.0;
    }

    double QueryCpuClockSpeedMHz(bool& available)
    {
        available = false;

        HRESULT initResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        const bool shouldUninitialize = SUCCEEDED(initResult);
        if (FAILED(initResult) && initResult != RPC_E_CHANGED_MODE)
        {
            return 0.0;
        }

        HRESULT hr = CoInitializeSecurity(
            nullptr,
            -1,
            nullptr,
            nullptr,
            RPC_C_AUTHN_LEVEL_DEFAULT,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr,
            EOAC_NONE,
            nullptr);

        if (FAILED(hr) && hr != RPC_E_TOO_LATE)
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        IWbemLocator* locator = nullptr;
        hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER, IID_IWbemLocator, reinterpret_cast<void**>(&locator));
        if (FAILED(hr) || locator == nullptr)
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        IWbemServices* services = nullptr;
        hr = locator->ConnectServer(
            _bstr_t(L"ROOT\\CIMV2"),
            nullptr,
            nullptr,
            nullptr,
            0,
            nullptr,
            nullptr,
            &services);

        if (FAILED(hr) || services == nullptr)
        {
            locator->Release();
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
            return 0.0;
        }

        CoSetProxyBlanket(
            services,
            RPC_C_AUTHN_WINNT,
            RPC_C_AUTHZ_NONE,
            nullptr,
            RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr,
            EOAC_NONE);

        IEnumWbemClassObject* enumerator = nullptr;
        hr = services->ExecQuery(
            _bstr_t(L"WQL"),
            _bstr_t(L"SELECT CurrentClockSpeed FROM Win32_Processor"),
            WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
            nullptr,
            &enumerator);

        double totalClock = 0.0;
        double count = 0.0;

        if (SUCCEEDED(hr) && enumerator != nullptr)
        {
            while (true)
            {
                IWbemClassObject* instance = nullptr;
                ULONG returned = 0;
                hr = enumerator->Next(2000, 1, &instance, &returned);
                if (FAILED(hr) || returned == 0 || instance == nullptr)
                {
                    break;
                }

                VARIANT value{};
                VariantInit(&value);
                if (SUCCEEDED(instance->Get(L"CurrentClockSpeed", 0, &value, nullptr, nullptr)))
                {
                    if (value.vt == VT_I4 || value.vt == VT_UI4)
                    {
                        const double mhz = static_cast<double>(value.ulVal);
                        if (mhz > 0.0 && mhz < 20000.0)
                        {
                            totalClock += mhz;
                            count += 1.0;
                        }
                    }
                }

                VariantClear(&value);
                instance->Release();
            }

            enumerator->Release();
        }

        services->Release();
        locator->Release();

        if (shouldUninitialize)
        {
            CoUninitialize();
        }

        if (count > 0.0)
        {
            available = true;
            return totalClock / count;
        }

        return 0.0;
    }

    double QueryCachedCpuTemperature(bool& available)
    {
        const uint64_t now = GetUtcMilliseconds();
        if (g_state.LastCpuTemperatureQueryMilliseconds == 0 ||
            (now - g_state.LastCpuTemperatureQueryMilliseconds) >= 5000ULL)
        {
            g_state.CachedCpuTemperatureCelsius = QueryCpuTemperature(g_state.CachedCpuTemperatureAvailable);
            g_state.LastCpuTemperatureQueryMilliseconds = now;
        }

        available = g_state.CachedCpuTemperatureAvailable;
        return g_state.CachedCpuTemperatureCelsius;
    }

    double QueryCachedCpuClockSpeedMHz(bool& available)
    {
        const uint64_t now = GetUtcMilliseconds();
        if (g_state.LastCpuClockQueryMilliseconds == 0 ||
            (now - g_state.LastCpuClockQueryMilliseconds) >= 3000ULL)
        {
            g_state.CachedCpuClockMHz = QueryCpuClockSpeedMHz(g_state.CachedCpuClockAvailable);
            g_state.LastCpuClockQueryMilliseconds = now;
        }

        available = g_state.CachedCpuClockAvailable;
        return g_state.CachedCpuClockMHz;
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
            disk.UsagePercent = std::clamp(QuerySingleCounter(g_state.DiskUsageCounter), 0.0, 100.0);
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
        const double utilization = QueryWildcardCounterTotal(g_state.GpuUtilizationCounter);
        gpu.UsagePercent = std::clamp(utilization, 0.0, 100.0);
        gpu.DedicatedMemoryBytes = static_cast<uint64_t>(std::max(0.0, QueryWildcardCounterTotal(g_state.GpuDedicatedMemoryCounter)));
    }

    bool TryInitializePdh()
    {
        const bool diskReady = OpenCounterQuery(
            &g_state.DiskQuery,
            {
                {L"\\PhysicalDisk(_Total)\\% Disk Time", &g_state.DiskUsageCounter},
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
        return diskReady;
    }

    void ClosePdh()
    {
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
    GetSystemInfo(&g_state.SystemInfo);

    if (!TryInitializePdh())
    {
        ClosePdh();
        return 0;
    }

    g_state.Initialized = true;
    return 1;
}

void ShutdownMonitor()
{
    std::scoped_lock lock(g_mutex);
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
    bool cpuClockAvailable = false;
    snapshot->Cpu.CurrentClockSpeedMHz = QueryCachedCpuClockSpeedMHz(cpuClockAvailable);
    if (!cpuClockAvailable)
    {
        snapshot->Cpu.CurrentClockSpeedMHz = 0.0;
    }
    bool cpuTemperatureAvailable = false;
    snapshot->Cpu.TemperatureCelsius = QueryCachedCpuTemperature(cpuTemperatureAvailable);
    snapshot->Cpu.TemperatureAvailable = cpuTemperatureAvailable ? 1 : 0;
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

    const uint64_t now = GetUtcMilliseconds();
    const bool hasBaseline = g_state.LastProcessSampleMilliseconds != 0;
    const double elapsedSeconds =
        hasBaseline ? static_cast<double>(now - g_state.LastProcessSampleMilliseconds) / 1000.0 : 0.0;
    g_state.LastProcessSampleMilliseconds = now;

    std::unordered_map<uint32_t, ProcessState> nextState;
    std::vector<ProcessSnapshotNative> candidates;

    if (Process32FirstW(snapshotHandle, &entry))
    {
        do
        {
            HANDLE processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, entry.th32ProcessID);
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
            nextState[entry.th32ProcessID] = ProcessState{ totalTime };

            PROCESS_MEMORY_COUNTERS_EX memoryCounters{};
            memoryCounters.cb = sizeof(memoryCounters);
            GetProcessMemoryInfo(processHandle, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memoryCounters), sizeof(memoryCounters));

            double cpuPercent = 0.0;
            const auto previous = g_state.ProcessTimes.find(entry.th32ProcessID);
            if (hasBaseline && elapsedSeconds > 0.0 && previous != g_state.ProcessTimes.end())
            {
                const uint64_t delta = totalTime >= previous->second.LastTotalTime100Ns
                    ? (totalTime - previous->second.LastTotalTime100Ns)
                    : 0;

                const double maxTime = elapsedSeconds * 10000000.0 * static_cast<double>(g_state.SystemInfo.dwNumberOfProcessors);
                if (maxTime > 0.0)
                {
                    cpuPercent = std::clamp((static_cast<double>(delta) / maxTime) * 100.0, 0.0, 100.0);
                }
            }

            if (cpuPercent > 0.1 || memoryCounters.WorkingSetSize > (10ULL * 1024ULL * 1024ULL))
            {
                ProcessSnapshotNative process{};
                process.ProcessId = entry.th32ProcessID;
                wcsncpy_s(process.Name, entry.szExeFile, _TRUNCATE);
                process.CpuPercent = cpuPercent;
                process.WorkingSetBytes = static_cast<uint64_t>(memoryCounters.WorkingSetSize);
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
