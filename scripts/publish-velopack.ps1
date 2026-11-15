param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$PackId = "TechArchive.Vitals",
    [string]$PackTitle = "TechArchive Vitals",
    [string]$Channel = "win",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$MainExe = "Vitals.exe",
    [string]$Authors = "Manggdy",
    [string]$DefaultUpdateSource = "https://tech.trendarchive.io/Utility/PC_Monitoring/",
    [string]$PublishDir = "",
    [string]$OutputDir = "",
    [string]$ReleaseNotes = "",
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Path value cannot be empty."
    }

    if ([System.IO.Path]::IsPathRooted($Value)) {
        return [System.IO.Path]::GetFullPath($Value)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Value))
}

function Convert-ToAssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SemanticVersion
    )

    $coreVersion = $SemanticVersion.Split('-', 2)[0]
    $parts = @($coreVersion.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries))

    if ($parts.Count -eq 0) {
        throw "Version '$SemanticVersion' is not valid."
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    if ($parts.Count -gt 4) {
        $parts = $parts[0..3]
    }

    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "Version '$SemanticVersion' contains a non-numeric core segment."
        }
    }

    return ($parts -join '.')
}

function Convert-ToFileSafeName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $safe = $Value -replace '[^A-Za-z0-9]+', '-'
    return $safe.Trim('-')
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-FullPath -BasePath $scriptRoot -Value ".."
$projectPath = Resolve-FullPath -BasePath $repoRoot -Value "src\Vitals.App\Vitals.App.csproj"

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "artifacts\publish\$Version\$Runtime"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = "artifacts\velopack\Releases"
}

$publishDirPath = Resolve-FullPath -BasePath $repoRoot -Value $PublishDir
$outputDirPath = Resolve-FullPath -BasePath $repoRoot -Value $OutputDir

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "artifacts\velopack\release-notes-$Version.md"
}

$releaseNotesPath = Resolve-FullPath -BasePath $repoRoot -Value $ReleaseNotes
$assemblyVersion = Convert-ToAssemblyVersion -SemanticVersion $Version
$defaultsFilePath = Join-Path $publishDirPath "app-defaults.json"

New-Item -ItemType Directory -Force -Path $publishDirPath | Out-Null
New-Item -ItemType Directory -Force -Path $outputDirPath | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $releaseNotesPath) | Out-Null

if (-not (Test-Path $releaseNotesPath)) {
    @(
        "# Vitals $Version"
        ""
        "- Add release notes here before shipping."
    ) | Set-Content -Path $releaseNotesPath -Encoding UTF8
}

Write-Host "Publishing version $Version to $publishDirPath"
dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $publishDirPath `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

# Release packages must not contain managed or native debug symbols.
Get-ChildItem -Path $publishDirPath -Filter "*.pdb" -File -Recurse | Remove-Item -Force

@{
    DefaultUpdateSource = $DefaultUpdateSource
} | ConvertTo-Json | Set-Content -Path $defaultsFilePath -Encoding UTF8

if ($SkipPack) {
    Write-Host "Publish completed. Skipping Velopack packaging because -SkipPack was specified."
    return
}

$vpkCommand = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpkCommand) {
    throw "The 'vpk' CLI tool is not installed. Install it first with: dotnet tool install -g vpk"
}

Write-Host "Packing Velopack release into $outputDirPath"
& $vpkCommand.Source pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $publishDirPath `
    --mainExe $MainExe `
    --packAuthors $Authors `
    --packTitle $PackTitle `
    --channel $Channel `
    --runtime $Runtime `
    --framework net8.0-x64-desktop `
    --icon (Join-Path $repoRoot "src\Vitals.App\Assets\AppIcon.ico") `
    --releaseNotes $releaseNotesPath `
    --outputDir $outputDirPath

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed."
}

$setupFileName = "$PackId-$Channel-Setup.exe"
$setupFilePath = Join-Path $outputDirPath $setupFileName
if (Test-Path $setupFilePath) {
    $versionedSetupName = "{0}-Setup-{1}.exe" -f (Convert-ToFileSafeName -Value $PackTitle), $Version
    $versionedSetupPath = Join-Path $outputDirPath $versionedSetupName
    Copy-Item -LiteralPath $setupFilePath -Destination $versionedSetupPath -Force
    Write-Host "Versioned setup : $versionedSetupPath"
}

Write-Host ""
Write-Host "Velopack release complete."
Write-Host "Publish files : $publishDirPath"
Write-Host "Release feed  : $outputDirPath"
