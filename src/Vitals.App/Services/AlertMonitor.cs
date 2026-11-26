using Vitals.App.Models;

namespace Vitals.App.Services;

// Decides when to warn: a value must stay at or above its limit for the whole sustain window,
// and the same metric stays quiet for the cooldown afterwards. Returns the messages to show.
public sealed class AlertMonitor
{
    private readonly Dictionary<string, DateTimeOffset> _aboveSince = [];
    private readonly Dictionary<string, DateTimeOffset> _lastAlert = [];

    public IReadOnlyList<string> Evaluate(SystemSnapshot snapshot, AppSettings settings)
    {
        var messages = new List<string>();
        if (!settings.AlertsEnabled)
        {
            _aboveSince.Clear();
            return messages;
        }

        Check("CPU usage", snapshot.Cpu.UsagePercent, settings.AlertCpuPercent, "%");
        Check("Memory usage", snapshot.Memory.UsagePercent, settings.AlertMemoryPercent, "%");
        Check("CPU temperature", snapshot.Cpu.TemperatureCelsius, settings.AlertCpuTempCelsius, " °C");
        Check("GPU temperature", snapshot.Gpu.TemperatureCelsius, settings.AlertGpuTempCelsius, " °C");
        return messages;

        void Check(string name, double? value, double limit, string unit)
        {
            var now = snapshot.Timestamp;
            if (value is null || value < limit)
            {
                _aboveSince.Remove(name);
                return;
            }

            if (!_aboveSince.TryGetValue(name, out var since))
            {
                _aboveSince[name] = now;
                return;
            }

            var sustained = now - since >= TimeSpan.FromSeconds(settings.AlertSustainSeconds);
            var quiet = !_lastAlert.TryGetValue(name, out var last) || now - last >= TimeSpan.FromMinutes(settings.AlertCooldownMinutes);
            if (sustained && quiet)
            {
                _lastAlert[name] = now;
                messages.Add($"{name} is {value:F0}{unit} (limit {limit:F0}{unit}) for {settings.AlertSustainSeconds} s.");
            }
        }
    }
}
