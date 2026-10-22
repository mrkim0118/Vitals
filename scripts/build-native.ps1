param(
    [Parameter(Mandatory = $true)]
    [string]$MsBuildPath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$Configuration
)

$pathValue = $env:Path
Remove-Item Env:PATH -ErrorAction SilentlyContinue
$env:Path = $pathValue

& $MsBuildPath $ProjectPath "/p:Configuration=$Configuration" "/p:Platform=x64" "/m"
exit $LASTEXITCODE
