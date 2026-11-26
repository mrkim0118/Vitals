<p align="center">
  <img src="src/Vitals.App/Assets/AppIcon.png" width="128" alt="Vitals icon">
</p>

<h1 align="center">Vitals</h1>

<p align="center">A lightweight real-time PC monitor for Windows.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><b>English</b> | <a href="README.ko.md">한국어</a></p>

Vitals shows CPU, memory, disk, GPU and network activity with the same counters Task Manager uses. It is built to stay out of the way: a small C++ collector does the sampling, and the app shows its own CPU and memory use in the header so you can see what it costs.

## Features

- **Live cards**: Each card shows the current value, a usage bar and a 60-second trend graph. The value and bar turn orange above 60% and red above 85%.
- **Top Processes**: The top 10 processes, sortable by PID, name, CPU, GPU, memory, disk or network.
- **Logging**: Record for a set time and get a `summary.txt` with min, max and average for each metric.
- **Administrator mode**: Unlocks CPU temperature and per-process network usage.
- **Low overhead**: A native collector and owner-drawn cards keep the app's own load small.
- **Update check**: On launch, checks the latest GitHub release and asks whether to download and install the new version.

### Live cards

| Card | Shows |
|---|---|
| CPU | Usage, clock speed including turbo, temperature |
| Memory | Usage, used and total |
| Disk | Active time, read and write speed, free space |
| GPU | Busiest engine usage, dedicated VRAM, temperature |
| Network | Download and upload speed on physical adapters, totals |
| Log | Recording state, elapsed and remaining time |

### Keeping its own load low

- The native collector uses PDH counters, IP Helper and process APIs, and no WMI.
- GPU temperature comes straight from the graphics driver (D3DKMT), the same source as Task Manager.
- LibreHardwareMonitor starts only for CPU temperature, and only when the app runs as administrator with PawnIO installed. It was about half of the app's memory.
- Processes are sampled every 3 seconds; system metrics every second.
- The window stops redrawing while minimized.
- Each card is one owner-drawn control instead of a tree of WinForms controls.
- The ETW network session runs only in administrator mode and stops when the app closes.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime (x64); the installer installs it when it is missing
- For CPU temperature: administrator rights and the [PawnIO](https://pawnio.eu/) driver

## Usage

### Top Processes

- Click a column header to sort by PID, name, CPU, GPU, memory, disk or network. Click it again to reverse the order.
- Covers every active process, not only the busiest by CPU.
- Disk is file I/O per second, including reads served from cache, so it can be higher than Task Manager's disk column.
- Network per process needs administrator rights (see below) and shows N/A otherwise.

### Logging

1. Enter a duration (`h:mm:ss`; more than 24 hours is fine), or click `10 min`, `30 min` or `1 hour`.
2. Click `Start Log`.
3. When the timer ends, `summary.txt` is saved automatically. `Stop & Save` saves early. `Discard` deletes the session.

`summary.txt` holds min, max and average for each metric, plus the top 5 processes by CPU over the whole session.

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
| Logs and settings | `%LocalAppData%\Vitals` |

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

| Output | Contents |
|---|---|
| `artifacts\publish\<version>\win-x64` | Published app files |
| `artifacts\velopack\Releases` | Installer and packages |

vpk 0.0.1298 fails to pack when release notes contain `&`. Write "and" instead.

## Project Layout

| Path | Contents |
|---|---|
| `src\Vitals.App` | C# WinForms app (.NET 8, x64) |
| `src\Vitals.Native` | C++ collector DLL (`Vitals.Native.dll`) |
| `scripts\publish-velopack.ps1` | Builds a release and installer |
| `scripts\build-native.ps1` | Builds the native DLL (called by the app build) |
| `src\Vitals.App\GitHubUpdater.cs` | GitHub release update check |

## Libraries

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu/): CPU temperature
- [Velopack](https://velopack.io/): installer
