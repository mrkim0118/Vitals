using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Vitals.App;

// Startup update check against the latest GitHub release: finds its Setup .exe, downloads it and starts it.
internal static class GitHubUpdater
{
    private const string Repository = "mrkim0118/Vitals";

    public sealed record Release(string Version, string SetupUrl, string SetupName);

    public static Version CurrentVersion { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    // Returns the latest release when it is newer than this build; null when up to date, offline or rate-limited.
    public static async Task<Release?> FindNewerReleaseAsync()
    {
        try
        {
            using var http = CreateClient(TimeSpan.FromSeconds(10));
            using var json = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest"));
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= CurrentVersion)
            {
                return null;
            }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    return new Release(Normalize(latest).ToString(), asset.GetProperty("browser_download_url").GetString() ?? "", name);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // The check is best effort: never bother the user when it cannot run.
        }

        return null;
    }

    // Downloads the setup to %TEMP% and starts it. The caller closes the app so the installer can replace its files.
    public static async Task DownloadAndStartSetupAsync(Release release, IProgress<int>? progress = null)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetFileName(release.SetupName));
        using (var http = CreateClient(TimeSpan.FromMinutes(30)))
        using (var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                if (total > 0)
                {
                    progress?.Report((int)(received * 100 / total));
                }
            }
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vitals-Updater");
        return http;
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
