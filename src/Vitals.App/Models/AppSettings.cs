namespace Vitals.App.Models;

// Properties missing from an older settings.json keep these defaults.
public sealed class AppSettings
{
    public string LogRootPath { get; set; } = string.Empty;

    // Window
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowTrayWidget { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool CompactView { get; set; }

    // Sampling: 1, 2 or 5 seconds.
    public int UpdateIntervalSeconds { get; set; } = 1;

    // Alerts fire when a value stays at or above its limit for AlertSustainSeconds, then stay quiet for AlertCooldownMinutes.
    public bool AlertsEnabled { get; set; } = true;
    public double AlertCpuPercent { get; set; } = 95;
    public double AlertMemoryPercent { get; set; } = 90;
    public double AlertCpuTempCelsius { get; set; } = 90;
    public double AlertGpuTempCelsius { get; set; } = 85;
    public int AlertSustainSeconds { get; set; } = 30;
    public int AlertCooldownMinutes { get; set; } = 10;
}
