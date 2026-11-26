using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

        // Never run what we can't tie to this app's own signer: a replaced release asset would otherwise run as the user.
        if (!IsSignedBySameCertificate(path, Environment.ProcessPath ?? ""))
        {
            File.Delete(path);
            throw new InvalidOperationException("The downloaded setup is not signed by the same certificate as this app, so it was not run.");
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // True when `file` has an intact Authenticode signature from the same certificate that signed `reference`.
    // The certificate is self-signed, so an untrusted root is accepted; a broken or missing signature is not.
    internal static bool IsSignedBySameCertificate(string file, string reference)
    {
        var expected = SignerThumbprint(reference);
        return expected is not null && HasIntactSignature(file) && SignerThumbprint(file) == expected;
    }

    private static string? SignerThumbprint(string path)
    {
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return certificate.Thumbprint;
        }
        catch (CryptographicException)
        {
            return null; // not signed
        }
    }

    private static bool HasIntactSignature(string path)
    {
        const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
        const uint CERT_E_CHAINING = 0x800B010A;

        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = Marshal.StringToCoTaskMemUni(path) };
        var fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 1,         // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x1000,      // WTD_CACHE_ONLY_URL_RETRIEVAL: no network
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            var result = (uint)WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result is 0 or CERT_E_UNTRUSTEDROOT or CERT_E_CHAINING;
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
            Marshal.FreeCoTaskMem(fileInfoPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vitals-Updater");
        return http;
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
