# TechArchive PC Monitoring

Compact Windows desktop monitor from TechArchive built around a low-overhead native collector with a small WinForms dashboard.

## Stack

- `src\PCMonitoring.App`
  - C# WinForms UI
  - real-time dashboard
  - timed summary logging
  - Velopack update integration
- `src\Monitoring.Native`
  - C++ native collector DLL
  - Windows APIs and PDH-based sampling

## Real-time metrics

- CPU usage
- CPU temperature when exposed by the system
- Memory usage percent and used capacity
- Disk usage percent, free space, read speed, and write speed
- GPU usage percent and dedicated VRAM usage
- GPU temperature when exposed by the system
- Network receive and send speed
- Top 5 processes by CPU usage

## Logging

Enter a duration in `hh:mm:ss` format and click `Start Log`.

The app creates one session folder per run under the configured log root and saves:

- `summary.txt`

The summary file contains:

- min / max / average values for the tracked resource metrics
- the top 5 processes ranked by cumulative CPU usage over the entire logging session

The default log root is:

```text
%LocalAppData%\PCMonitoring\logs
```

You can change the log root inside the app and open it directly with the `Open Folder` button.

## Low-memory design

The current build keeps overhead down with:

- WinForms instead of the earlier WPF UI
- a compact `ListView` for top processes
- a bounded single-item snapshot channel to avoid UI backlog
- slower top-process refresh than the core sampler
- cached temperature reads on the native side
- configuration stored under `%LocalAppData%` so logs and settings survive app updates

## Build

```powershell
dotnet build .\src\PCMonitoring.App\PCMonitoring.App.csproj -c Debug
```

## Run

```powershell
.\src\PCMonitoring.App\bin\Debug\net8.0-windows\PCMonitoring.App.exe
```

## Velopack Auto Update

The app can check for updates from either:

- an `https://...` feed URL
- a local or network folder path that contains the Velopack release feed

Important notes:

- auto-update only works when the app was installed from a Velopack installer
- a plain `dotnet publish` folder or copied `.exe` will show the UI but cannot self-update

Inside the app:

- set `Update source (URL or folder)`
- optionally keep `Auto check on startup` enabled
- click `Check Updates` to test immediately

If a default update source is embedded at build time, the installed app will automatically check for updates shortly after startup. When a newer version exists, it asks the user whether to continue. If the user confirms, the app downloads the update and restarts automatically.

### Package a release

Install the Velopack CLI once:

```powershell
dotnet tool install -g vpk
```

Then create a release package:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0
```

To embed the production update feed directly into the app:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0 -DefaultUpdateSource "https://tech.trendarchive.io/Utility/PC_Monitoring/"
```

This script will:

- publish a framework-dependent `win-x64` build
- exclude `.pdb` debug symbols from the release package
- create a Velopack installer and release feed
- optionally stamp a default update source into the build

The target PC must have the x64 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed.

Default output locations:

- publish files: `artifacts\publish\<version>\win-x64`
- Velopack feed and installers: `artifacts\velopack\Releases`

### Test updates locally

1. Build version `1.0.0` with the script above.
2. Install the generated setup from `artifacts\velopack\Releases`.
3. In the app, set `Update source (URL or folder)` to:

```text
C:\Users\mrkim\Desktop\codex\PC Monitoring\artifacts\velopack\Releases
```

4. Build a higher version, for example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.1
```

5. Run `Check Updates` in the installed app.

### Host updates remotely

Upload the contents of `artifacts\velopack\Releases` to a static file host or release store and point the app to that URL. Velopack will read the channel feed file such as `releases.win.json` and download the matching packages.

### Release an update as the distributor

1. Pick the next version number such as `1.0.1`.
2. Run `publish-velopack.ps1` with the same `-DefaultUpdateSource` you used for the installed build.
3. Upload the new contents of `artifacts\velopack\Releases` to the same update location.
4. Existing installed users will be prompted on next startup and can update in-place.
