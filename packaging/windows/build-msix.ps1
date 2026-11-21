# JJQR - AI (Nov 21, 2026) - build a signed, self-contained MSIX of Clinic Node on a Windows PC.
# Prereqs (once): .NET 10 SDK, `dotnet workload install maui`, Windows SDK (signtool), the csproj MSIX properties from packaging/README.md.
# Usage (PowerShell, from Project02-desktop):
#   .\packaging\windows\build-msix.ps1 -CertPath C:\certs\clinicnode.pfx -CertPassword (Read-Host -AsSecureString)
#   .\packaging\windows\build-msix.ps1 -Thumbprint 0123ABCD...            # cert already in CurrentUser\My
#   .\packaging\windows\build-msix.ps1 -DevCert                            # create + trust a self-signed test cert (test PCs only)
param(
  [string]$CertPath,
  [securestring]$CertPassword,
  [string]$Thumbprint,
  [switch]$DevCert,
  [string]$Rid = "win-x64",
  [string]$Publisher = "CN=Grace Medical Clinic"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
$proj = "$root\ClinicNode\ClinicNode.App\ClinicNode.App.csproj"
$out  = "$root\packaging\out"
New-Item -ItemType Directory -Force $out | Out-Null

if ($DevCert) {
  $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName "Clinic Node dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
  $Thumbprint = $cert.Thumbprint
  Export-Certificate -Cert $cert -FilePath "$out\ClinicNode-dev.cer" | Out-Null
  Write-Host "Dev cert $Thumbprint. On each test PC (admin): Import-Certificate -FilePath ClinicNode-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
}

$props = @(
  "-f", "net10.0-windows10.0.19041.0", "-c", "Release", "-r", $Rid,
  "-p:WindowsPackageType=MSIX", "-p:SelfContained=true", "-p:WindowsAppSDKSelfContained=true",
  "-p:GenerateAppxPackageOnBuild=true", "-p:AppxPackageSigningEnabled=true", "-p:AppxPackageDir=$out\msix\"
)
if ($Thumbprint) { $props += "-p:PackageCertificateThumbprint=$Thumbprint" }
elseif ($CertPath) {
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertPassword))
  $props += "-p:PackageCertificateKeyFile=$CertPath", "-p:PackageCertificatePassword=$plain"
}
else { throw "Pass -Thumbprint, -CertPath/-CertPassword, or -DevCert." }

dotnet publish $proj @props
$msix = Get-ChildItem "$out\msix" -Recurse -Filter *.msix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No .msix produced." }
Write-Host "ok: $($msix.FullName)"
Write-Host "Install: double-click it (or Add-AppxPackage '$($msix.FullName)')."
# JJQR - END
