using LibreHardwareMonitor.Hardware;
using Vitals.App.Models;

namespace Vitals.App.Services;

public sealed class HardwareSensorService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private UpdateVisitor? _updateVisitor;
    private Computer? _computer;
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private double? _cachedCpuTemperature;

    // CPU sensors are reachable only through the PawnIO kernel driver, which admits administrators only.
    public static bool IsPawnIoInstalled()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }

    public bool TryInitialize()
    {
        lock (_sync)
        {
            if (_computer is not null)
            {
                return true;
            }

            // LibreHardwareMonitor is about half of this app's memory, and without admin + PawnIO it reads nothing,
            // so it only starts when it can deliver the CPU temperature. GPU temperature comes from the native collector.
            if (!Environment.IsPrivilegedProcess || !IsPawnIoInstalled())
            {
                return false;
            }

            try
            {
                _updateVisitor = new UpdateVisitor();
                _computer = new Computer
                {
                    IsCpuEnabled = true,
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

            return new SystemSnapshot
            {
                Timestamp = snapshot.Timestamp,
                Cpu = new CpuMetrics
                {
                    UsagePercent = snapshot.Cpu.UsagePercent,
                    TemperatureCelsius = cpuTemperature,
                    CurrentClockSpeedMHz = snapshot.Cpu.CurrentClockSpeedMHz,
                },
                Memory = snapshot.Memory,
                Disk = snapshot.Disk,
                Gpu = snapshot.Gpu,
                Network = snapshot.Network,
                TopProcesses = snapshot.TopProcesses,
                Drives = snapshot.Drives,
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
        if (_computer is null || _updateVisitor is null)
        {
            return;
        }

        try
        {
            _computer.Accept(_updateVisitor);
            _cachedCpuTemperature = GetCpuTemperature(_computer.Hardware);
            _lastRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch
        {
            _cachedCpuTemperature = null;
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
