# Builds Vitals (Release, framework-dependent) and its Inno Setup installer: artifacts\installer\Vitals-Setup-<version>.exe
# The version comes from <Version> in src\Vitals.App\Vitals.App.csproj.
#   -NoSign  skip code signing (no certificate needed)
param([switch]$NoSign)
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Vitals.App\Vitals.App.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$outputDir = Join-Path $repoRoot 'artifacts\installer'
$sign = Join-Path $repoRoot 'installer\sign.ps1'
$version = [regex]::Match((Get-Content -Raw -LiteralPath $project), '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw "No <Version> in $project" }

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishDir -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
if (-not $NoSign) {
    powershell -NoProfile -ExecutionPolicy Bypass -File $sign (Join-Path $publishDir 'Vitals.exe')
    if ($LASTEXITCODE) { throw 'Signing failed.' }
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }
$isccArgs = @("/DAppVersion=$version", "/DSourceDir=$publishDir", "/DOutputDir=$outputDir")
if (-not $NoSign) { $isccArgs += '/DSign', "/Smanggdysign=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `$q$sign`$q `$f" }
& $iscc /Q @isccArgs (Join-Path $repoRoot 'installer\Vitals.iss')
if ($LASTEXITCODE) { throw 'ISCC failed.' }
Get-Item -LiteralPath (Join-Path $outputDir "Vitals-Setup-$version.exe")
