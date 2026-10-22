using System.IO;
using System.Text.Json;

namespace PCMonitoring.App.Services;

internal static class AppRuntimeDefaults
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string GetDefaultUpdateSource()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "app-defaults.json");
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var json = File.ReadAllText(path);
            var defaults = JsonSerializer.Deserialize<AppRuntimeDefaultsFile>(json, JsonOptions);
            return defaults?.DefaultUpdateSource?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class AppRuntimeDefaultsFile
    {
        public string? DefaultUpdateSource { get; set; }
    }
}
