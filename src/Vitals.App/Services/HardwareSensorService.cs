using LibreHardwareMonitor.Hardware;
using Vitals.App.Models;

namespace Vitals.App.Services;

public sealed class HardwareSensorService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private readonly UpdateVisitor _updateVisitor = new();
    private Computer? _computer;
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private double? _cachedCpuTemperature;
    private double? _cachedCpuClockMHz;
    private double? _cachedGpuTemperature;

    public bool TryInitialize()
    {
        lock (_sync)
        {
            if (_computer is not null)
            {
                return true;
            }

            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                };
                _computer.Open();
                RefreshSensors();
                return true;
            }
            catch
            {
                _computer?.Close();
                _computer = null;
                _cachedCpuTemperature = null;
                _cachedCpuClockMHz = null;
                _cachedGpuTemperature = null;
                return false;
            }
        }
    }

    public SystemSnapshot Enrich(SystemSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_computer is null)
            {
                return snapshot;
            }

            if (_lastRefreshUtc == DateTimeOffset.MinValue ||
                (DateTimeOffset.UtcNow - _lastRefreshUtc) >= RefreshInterval)
            {
                RefreshSensors();
            }

            var cpuTemperature = _cachedCpuTemperature ?? snapshot.Cpu.TemperatureCelsius;
            var cpuClockMHz = SelectCpuClock(snapshot.Cpu.CurrentClockSpeedMHz, _cachedCpuClockMHz);
            var gpuTemperature = _cachedGpuTemperature ?? snapshot.Gpu.TemperatureCelsius;

            return new SystemSnapshot
            {
                Timestamp = snapshot.Timestamp,
                Cpu = new CpuMetrics
                {
                    UsagePercent = snapshot.Cpu.UsagePercent,
                    TemperatureCelsius = cpuTemperature,
                    CurrentClockSpeedMHz = cpuClockMHz,
                },
                Memory = snapshot.Memory,
                Disk = snapshot.Disk,
                Gpu = new GpuMetrics
                {
                    UsagePercent = snapshot.Gpu.UsagePercent,
                    DedicatedMemoryBytes = snapshot.Gpu.DedicatedMemoryBytes,
                    TemperatureCelsius = gpuTemperature,
                },
                Network = snapshot.Network,
                TopProcesses = snapshot.TopProcesses,
            };
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_computer is null)
            {
                return;
            }

            try
            {
                _computer.Close();
            }
            catch
            {
            }
            finally
            {
                _computer = null;
            }
        }
    }

    private void RefreshSensors()
    {
        if (_computer is null)
        {
            return;
        }

        try
        {
            _computer.Accept(_updateVisitor);
            _cachedCpuTemperature = GetCpuTemperature(_computer.Hardware);
            _cachedCpuClockMHz = GetCpuClockMHz(_computer.Hardware);
            _cachedGpuTemperature = GetGpuTemperature(_computer.Hardware);
            _lastRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch
        {
            _cachedCpuTemperature = null;
            _cachedCpuClockMHz = null;
            _cachedGpuTemperature = null;
            _lastRefreshUtc = DateTimeOffset.UtcNow;
        }
    }

    private static double? GetCpuTemperature(IEnumerable<IHardware> hardwareItems)
    {
        var temperatures = EnumerateHardware(hardwareItems)
            .Where(hardware => hardware.HardwareType == HardwareType.Cpu)
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
            .Select(sensor => new SensorReading(sensor.Name, sensor.Value!.Value))
            .ToList();

        if (temperatures.Count == 0)
        {
            return null;
        }

        return TryGetPreferredTemperature(
                   temperatures,
                   "package",
                   "tctl",
                   "tdie",
                   "die",
                   "core average")
               ?? temperatures.Average(sensor => sensor.Value);
    }

    private static double? GetGpuTemperature(IEnumerable<IHardware> hardwareItems)
    {
        var temperatures = EnumerateHardware(hardwareItems)
            .Where(hardware =>
                hardware.HardwareType == HardwareType.GpuNvidia ||
                hardware.HardwareType == HardwareType.GpuAmd ||
                hardware.HardwareType == HardwareType.GpuIntel)
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
            .Select(sensor => new SensorReading(sensor.Name, sensor.Value!.Value))
            .ToList();

        if (temperatures.Count == 0)
        {
            return null;
        }

        return TryGetPreferredTemperature(
                   temperatures,
                   "gpu core",
                   "core",
                   "edge",
                   "hot spot",
                   "hotspot")
               ?? temperatures.Max(sensor => sensor.Value);
    }

    private static double? GetCpuClockMHz(IEnumerable<IHardware> hardwareItems)
    {
        var clocks = EnumerateHardware(hardwareItems)
            .Where(hardware => hardware.HardwareType == HardwareType.Cpu)
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Clock && sensor.Value.HasValue)
            .Select(sensor => new SensorReading(sensor.Name, sensor.Value!.Value))
            .ToList();

        if (clocks.Count == 0)
        {
            return null;
        }

        var coreClocks = clocks
            .Where(sensor => sensor.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return TryGetPreferredReading(
                   clocks,
                   "core average",
                   "average",
                   "effective clock")
               ?? (coreClocks.Count > 0
                   ? coreClocks.Average(sensor => sensor.Value)
                   : clocks.Average(sensor => sensor.Value));
    }

    private static double? SelectCpuClock(double? nativeClockMHz, double? sensorClockMHz)
    {
        if (nativeClockMHz.HasValue && nativeClockMHz.Value > 0.0)
        {
            return nativeClockMHz;
        }

        if (sensorClockMHz.HasValue && sensorClockMHz.Value > 0.0)
        {
            return sensorClockMHz;
        }

        return null;
    }

    private static double? TryGetPreferredTemperature(
        IEnumerable<SensorReading> temperatures,
        params string[] preferredNames) =>
        TryGetPreferredReading(temperatures, preferredNames);

    private static double? TryGetPreferredReading(
        IEnumerable<SensorReading> readings,
        params string[] preferredNames)
    {
        foreach (var preferredName in preferredNames)
        {
            var match = readings.FirstOrDefault(sensor =>
                sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match.Value;
            }
        }

        return null;
    }

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> hardwareItems)
    {
        foreach (var hardware in hardwareItems)
        {
            yield return hardware;

            foreach (var child in EnumerateHardware(hardware.SubHardware))
            {
                yield return child;
            }
        }
    }

    private sealed record SensorReading(string Name, float Value);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
