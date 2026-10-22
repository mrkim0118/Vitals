using System.IO;
using System.Text.Json;
using PCMonitoring.App.Models;

namespace PCMonitoring.App.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCMonitoring");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
            MigrateDefaults(settings);
            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            LogRootPath = Path.Combine(SettingsDirectory, "logs"),
        };
    }

    private static void MigrateDefaults(AppSettings settings)
    {
        var oldDefault = Path.Combine(AppContext.BaseDirectory, "logs");
        if (string.IsNullOrWhiteSpace(settings.LogRootPath) ||
            string.Equals(settings.LogRootPath, oldDefault, StringComparison.OrdinalIgnoreCase))
        {
            settings.LogRootPath = Path.Combine(SettingsDirectory, "logs");
        }
    }
}
