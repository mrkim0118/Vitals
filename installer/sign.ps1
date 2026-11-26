# Signs a file with the "Manggdy" code signing certificate (Authenticode, SHA-256).
# Used by scripts\Build-Installer.ps1 for the app exe and by Inno Setup (SignTool) for the setup and uninstaller.
#   powershell -ExecutionPolicy Bypass -File installer\sign.ps1 <file>
# The certificate is self-signed and lives in the current user's store (Cert:\CurrentUser\My). A self-signed certificate is not trusted by other PCs,
# so Windows still warns there; replace it with a certificate from a code signing CA to remove the warning.
param([Parameter(Mandatory)][string]$File)
$ErrorActionPreference = 'Stop'
# Pinned: the in-app updater (GitHubUpdater.cs) only runs a setup signed by the same certificate as the
# running Vitals.exe, so every release must use this one.
$thumbprint = '58DE52FE1384185D65535947555DEC11C76FC995'
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Thumbprint -eq $thumbprint -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) { throw "Code signing certificate $thumbprint was not found in Cert:\CurrentUser\My (or it has expired)." }
# A timestamp keeps the signature valid after the certificate expires; sign without one when offline.
$sig = $null
foreach ($ts in 'http://timestamp.digicert.com', 'http://timestamp.sectigo.com') {
    try { $sig = Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $ts; break } catch { }
}
if (-not $sig) { $sig = Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 }
# Status is UnknownError for a self-signed chain this PC does not trust; the signature itself is in place.
if (-not $sig.SignerCertificate) { throw "Signing failed: $File ($($sig.StatusMessage))" }
Write-Host "Signed $File"
