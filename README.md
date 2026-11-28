<p align="center">
  <img src="src/Vitals.App/Assets/AppIcon.png" width="128" alt="Vitals icon">
</p>

<h1 align="center">Vitals</h1>

<p align="center">A lightweight real-time PC monitor for Windows.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><b>English</b> | <a href="README.ko.md">한국어</a></p>

Vitals shows CPU, memory, disk, GPU and network activity with the same counters Task Manager uses. It is built to stay out of the way: a small C++ collector does the sampling, and the app shows its own CPU and memory use in the header so you can see what it costs.

## Download

Get the latest installer from the [Releases](../../releases/latest) page:

- `Vitals-Setup-<version>.exe`: installer (signed by Manggdy; installs the .NET 8 Desktop Runtime if needed)

## Features

- **Live cards**: Each card shows the current value, a usage bar and a trend graph of the last 60 samples. The value and bar turn orange above 60% and red above 85%.
- **Per-drive disk**: The Disk card lists each drive's active time and free space.
- **Top Processes**: The top 10 processes, sortable by PID, name, CPU, GPU, memory, disk or network. Right-click a row to open its file location or end it.
- **Tray mode**: Minimizing or closing hides the window to the tray and shows a small widget above the taskbar with CPU, GPU, RAM and drive C:.
- **Alerts**: A Windows notification when CPU or memory usage, or CPU or GPU temperature, stays above its limit.
- **Logging**: Record for a set time and get a `summary.txt` with min, max and average for each metric, plus `samples.csv` with one row per sample.
- **Window options**: Always on top, a compact cards-only view, and an update interval of 1, 2 or 5 seconds.
- **Administrator mode**: Unlocks CPU temperature and per-process network usage.
- **Update check**: On launch, checks the latest GitHub release and asks whether to download and install the new version. The setup only runs when it carries the same signature as the installed app.
- **Low overhead**: A native collector and owner-drawn cards keep the app's own load small.

### Live cards

| Card | Shows |
|---|---|
| CPU | Usage, clock speed including turbo, temperature |
| Memory | Usage, used and total |
| Disk | Active time, read and write speed, active time and free space per drive |
| GPU | Busiest engine usage, dedicated VRAM, temperature |
| Network | Download and upload speed on physical adapters, totals |
| Log | Recording state, elapsed and remaining time |

### Keeping its own load low

- The native collector uses PDH counters, IP Helper and process APIs, and no WMI.
- GPU temperature comes straight from the graphics driver (D3DKMT), the same source as Task Manager.
- LibreHardwareMonitor starts only for CPU temperature, and only when the app runs as administrator with PawnIO installed. It was about half of the app's memory.
- Processes are sampled every 3 seconds; system metrics at the chosen interval (1, 2 or 5 seconds).
- The window stops redrawing while minimized or in the tray.
- Each card is one owner-drawn control instead of a tree of WinForms controls.
- The ETW network session runs only in administrator mode and stops when the app closes.
- Only one copy runs at a time; starting it again brings the open window back.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime (x64); the installer installs it when it is missing
- For CPU temperature: administrator rights and the [PawnIO](https://pawnio.eu/) driver

## Usage

The `⋯` button in the header holds the options: Always on top, Compact view, Minimize or close to tray, Show widget while in the tray, Update interval and Alerts. Each choice is saved right away.

### Tray mode

- Minimize or close (X) the window to send it to the tray. A widget appears above the taskbar's corner with CPU, GPU, RAM and drive C: (how full it is, and the free space).
- Click the widget, double-click the tray icon, or start Vitals again to bring the window back.
- Right-click the tray icon for `Open Vitals` and `Exit`. `Exit` is how you quit while tray mode is on.
- Turn off `Minimize or close to tray` to minimize to the taskbar and quit with X as usual, or `Show widget while in the tray` to hide the widget.

### Alerts

A notification appears when a value stays at or above its limit for 30 seconds. The same metric then stays quiet for 10 minutes.

| Metric | Default limit |
|---|---|
| CPU usage | 95% |
| Memory usage | 90% |
| CPU temperature | 90 °C |
| GPU temperature | 85 °C |

Change the limits and timing in `settings.json` (see Data & Settings): `AlertCpuPercent`, `AlertMemoryPercent`, `AlertCpuTempCelsius`, `AlertGpuTempCelsius`, `AlertSustainSeconds`, `AlertCooldownMinutes`.

### Top Processes

- Click a column header to sort by PID, name, CPU, GPU, memory, disk or network. Click it again to reverse the order.
- Right-click a row: `Open file location` selects the program in Explorer; `End process...` closes it after you confirm.
- Covers every active process, not only the busiest by CPU.
- Disk is file I/O per second, including reads served from cache, so it can be higher than Task Manager's disk column.
- Network per process needs administrator rights (see below) and shows N/A otherwise.

### Logging

1. Enter a duration (`h:mm:ss`; more than 24 hours is fine), or click `10 min`, `30 min` or `1 hour`.
2. Click `Start Log`.
3. When the timer ends, `summary.txt` is saved automatically. `Stop & Save` saves early, and closing the app saves too. `Discard` deletes the session.

| File | Contents |
|---|---|
| `summary.txt` | Min, max and average for each metric, plus the top 5 processes by CPU over the whole session |
| `samples.csv` | One row per sample: time, CPU, clock, temperatures, memory, disk, GPU and network |

### Administrator mode

Some data needs administrator rights. Use `⋯` → `Restart as administrator`. A running log is saved before the restart.

| Data | Needs |
|---|---|
| CPU temperature | Administrator rights and the [PawnIO](https://pawnio.eu/) driver |
| Network per process | Administrator rights (it uses an ETW trace session) |

CPU temperature is read by LibreHardwareMonitor through PawnIO, a signed kernel driver. Install it once per PC:

```powershell
winget install namazso.PawnIO
```

When something is missing, the CPU card says which: `N/A (needs PawnIO driver)` or `N/A (run as admin)`.

## Data & Settings

| Data | Location |
|---|---|
| Settings | `%LocalAppData%\Vitals\settings.json` |
| Logs (default) | `%LocalAppData%\Vitals\logs\session_<date>_<time>` |
| Downloaded update | `%TEMP%\Vitals-Setup-<version>.exe` |

## Limitations

- The Disk card shows up to three drives.
- `Open file location` and `End process` can't reach system processes or other users' processes without administrator rights.
- The installer is signed with a self-signed certificate, so Windows SmartScreen still warns on first run.

## Build from Source

Needs the .NET 8 SDK and Visual Studio 2022 with the C++ desktop workload. The app build compiles the native DLL first.

### Build

```powershell
dotnet build .\src\Vitals.App\Vitals.App.csproj -c Release
.\src\Vitals.App\bin\Release\net8.0-windows\Vitals.exe
```

Build the app project, not `Vitals.sln`: the `dotnet` CLI cannot build the C++ project directly.

### Release

Releases use [Velopack](https://velopack.io/). Install the CLI once:

```powershell
dotnet tool install -g vpk
```

Create a release (write `artifacts\velopack\release-notes-<version>.md` first):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0
```

The script:

- publishes a framework-dependent `win-x64` build without `.pdb` files
- builds the installer with the app icon
- makes the installer install the x64 .NET 8 Desktop Runtime when it is missing
- signs everything with the Manggdy certificate pinned by `-SignThumbprint`, and stops if that certificate is missing (the in-app update check only runs setups with the same signature)

| Output | Contents |
|---|---|
| `artifacts\publish\<version>\win-x64` | Published app files |
| `artifacts\velopack\Releases` | Installer and packages |

vpk 0.0.1298 fails to pack when release notes contain `&`. Write "and" instead.

## Project Layout

| Path | Contents |
|---|---|
| `src\Vitals.App` | C# WinForms app (.NET 8, x64) |
| `src\Vitals.App\TrayWidget.cs` | Widget shown above the taskbar in tray mode |
| `src\Vitals.App\GitHubUpdater.cs` | GitHub release update check and signature check |
| `src\Vitals.App\Services\AlertMonitor.cs` | When to raise an alert |
| `src\Vitals.Native` | C++ collector DLL (`Vitals.Native.dll`) |
| `scripts\publish-velopack.ps1` | Builds a release and installer |
| `scripts\build-native.ps1` | Builds the native DLL (called by the app build) |

## Libraries

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu/): CPU temperature
- [Velopack](https://velopack.io/): installer
