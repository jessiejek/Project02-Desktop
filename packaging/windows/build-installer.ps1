<#
 build-installer.ps1 - build Clinic Node on a WINDOWS computer and make ClinicNode-Setup-<version>.exe
 JJQR - AI (Nov 22, 2026) - owner: the clinic computers are Windows and the owner has a Windows work laptop. Free: no certificate, no subscription.

 Run (PowerShell), from the Project02-Desktop folder:
     powershell -ExecutionPolicy Bypass -File packaging\windows\build-installer.ps1

 It installs what is missing (.NET 10 SDK, the MAUI workload, Inno Setup) using winget, publishes the app (Windows x64, self-contained, release, live server address built in),
 and builds the installer into packaging\out\. Everything it prints is also saved in packaging\out\build-installer.log - send that file if something fails.
 Needs Project02-be cloned NEXT TO Project02-Desktop (the projects reference it):  ...\Project02-be   and   ...\Project02-Desktop
#>
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path          # Project02-Desktop
$be = Join-Path (Split-Path $root -Parent) "Project02-be"
$out = Join-Path $root "packaging\out"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Start-Transcript -Path (Join-Path $out "build-installer.log") -Force | Out-Null
try {
    function Step($t) { Write-Host "`n==> $t" -ForegroundColor Cyan }
    function Have($cmd) { return [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }

    Step "Checking folders"
    if (-not (Test-Path (Join-Path $be "src\ClinicApp.Domain"))) { throw "Project02-be was not found next to this folder: expected $be. Clone it there:  git clone https://github.com/jessiejek/Project02-be.git" }

    Step "Checking .NET 10 SDK"
    $needDotnet = $true
    if (Have "dotnet") { $needDotnet = -not ((& dotnet --list-sdks) -match "^10\.") }
    if ($needDotnet) {
        if (-not (Have "winget")) { throw "winget is missing. Install 'App Installer' from the Microsoft Store, or install the .NET 10 SDK by hand from https://dotnet.microsoft.com/download" }
        winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    }
    dotnet --version

    Step "Installing the MAUI workload (a few minutes the first time)"
    dotnet workload install maui

    Step "Checking Inno Setup (makes the installer)"
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
        $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw "Inno Setup was not found after install. Install it from https://jrsoftware.org/isdl.php and run this script again." }

    $proj = Join-Path $root "ClinicNode\ClinicNode.App\ClinicNode.App.csproj"
    $version = ([regex]::Match((Get-Content $proj -Raw), "<ApplicationDisplayVersion>(.*?)</ApplicationDisplayVersion>")).Groups[1].Value
    if (-not $version) { $version = "0.1.0" }

    Step "Building Clinic Node $version (Release, Windows x64, self-contained)"
    $app = Join-Path $out "ClinicNode"
    if (Test-Path $app) { Remove-Item $app -Recurse -Force }
    dotnet publish $proj -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:ClinicWindowsOnly=true -o $app
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE). The first Windows build of this project is expected to need a few fixes: send packaging\out\build-installer.log." }
    $exe = Get-ChildItem $app -Filter "*.exe" | Where-Object { $_.Name -like "ClinicNode*" } | Select-Object -First 1
    if (-not $exe) { throw "The published folder has no ClinicNode*.exe: $app" }

    Step "Making the installer"
    & $iscc "/DAppVersion=$version" "/DSourceDir=$app" "/DExeName=$($exe.Name)" (Join-Path $PSScriptRoot "ClinicNode.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }

    $setup = Join-Path $out "ClinicNode-Setup-$version.exe"
    Write-Host "`nDONE. Installer: $setup" -ForegroundColor Green
    Write-Host "Copy it to any Windows PC and double-click. Windows may say 'Windows protected your PC' (no paid certificate): click 'More info', then 'Run anyway'."
}
catch {
    Write-Host "`nFAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Send the file packaging\out\build-installer.log to your developer."
    exit 1
}
finally { Stop-Transcript | Out-Null }
