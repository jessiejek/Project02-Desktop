<!-- JJQR - AI (Nov 21, 2026) - packaging + printing integration for Clinic Node (allen5) -->
# Clinic Node — packaging & printing

All commands run from `Project02-desktop/`.

## Mac (DMG)

```bash
packaging/mac/build-dmg.sh                       # ad-hoc signed → packaging/out/ClinicNode-<ver>-arm64.dmg (this Mac / testing)
RID=maccatalyst-x64 packaging/mac/build-dmg.sh   # Intel Macs
```

Distribution to other Macs (owner, once): Apple Developer Program → create a **Developer ID Application** certificate in Xcode
(Settings → Accounts → Manage Certificates), then store notary credentials:

```bash
xcrun notarytool store-credentials clinicnode --apple-id YOU@EXAMPLE.COM --team-id TEAMID --password <app-specific-password>
SIGN_ID="Developer ID Application: YOUR NAME (TEAMID)" NOTARY_PROFILE=clinicnode packaging/mac/build-dmg.sh
```

The script signs nested code + the app with hardened runtime and `Platforms/MacCatalyst/Entitlements.plist`, signs the DMG,
submits it to Apple, staples the ticket, and checks it with `spctl`. Without `SIGN_ID` the DMG only opens on this Mac
(other Macs need right-click → Open).

## Windows (MSIX) — on a Windows 10/11 PC

Once: install the .NET 10 SDK, `dotnet workload install maui`, and the Windows SDK (signtool). Copy `Project02-desktop` and `Project02-be`.

```powershell
.\packaging\windows\build-msix.ps1 -DevCert                                           # test PCs (self-signed)
.\packaging\windows\build-msix.ps1 -CertPath C:\certs\clinic.pfx -CertPassword (Read-Host -AsSecureString)   # real cert
```

- Output: `packaging\out\msix\...\ClinicNode.App_<ver>_x64.msix` — double-click to install. Self-contained: .NET runtime and Windows App SDK are inside; WebView2 runtime ships with Windows 10/11.
- **Signing:** the `Publisher` in `ClinicNode.App/Platforms/Windows/Package.appxmanifest` (`CN=Grace Medical Clinic`) must equal the certificate Subject exactly. A self-signed cert must be imported into `LocalMachine\TrustedPeople` on each PC (the script prints the command). For no-warning installs buy a code-signing cert (or use Azure Trusted Signing) and change `Publisher` to its Subject.
- **Auto-start:** `StartupTask` `ClinicNodeStartup` is declared (off by default). Turn on from the app with `await ClinicNode.App.WinUI.WindowsStartup.EnableAsync();` (e.g. an admin "Start with Windows" switch) or via Settings → Apps → Startup.
- **Updates:** host `packaging/windows/ClinicNode.appinstaller` + the .msix on HTTPS or a UNC share (edit `HOST`/version), install once from the `.appinstaller`; Windows checks on every launch and in the background. Bump `ApplicationDisplayVersion`/`ApplicationVersion` and the appinstaller `Version` each release. The cloud version handshake (§9) still decides whether sync is allowed.

## Required shared-file changes (for allen to apply)

**MauiProgram.cs** — after `builder.Services.AddClinicNodeCore(options, ...)`, replace `builder.Services.AddSingleton<IPrinter, MauiPrinter>();` with:

```csharp
// JJQR - AI (Nov 21, 2026) - web-identical print forms + printer settings + receipt printing (allen5)
builder.Services.AddSingleton(ClinicNode.Core.Printing.PrintingOptions.Load(dataDir));
builder.Services.AddSingleton<ClinicNode.Core.Print.IPrintDocumentFactory, ClinicNode.Core.Printing.ClinicFormsPrintDocumentFactory>();
builder.Services.AddSingleton<MauiPrinter>();
builder.Services.AddSingleton<IPrinter>(sp => sp.GetRequiredService<MauiPrinter>());
builder.Services.AddSingleton<ClinicNode.Core.Printing.IReceiptPrinter>(sp => sp.GetRequiredService<MauiPrinter>());
// JJQR - END
```

**ClinicNode.App.csproj** — nothing is required for the scripts (they pass the MSIX properties on the command line). Optional, so
Visual Studio "Publish" behaves the same; replace the `WindowsPackageType ... None` line with:

```xml
<!-- JJQR - AI (Nov 21, 2026) - MSIX, self-contained (allen5) -->
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">
  <WindowsPackageType>MSIX</WindowsPackageType>
  <SelfContained>true</SelfContained>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
  <AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
  <AppxAutoIncrementPackageRevision>false</AppxAutoIncrementPackageRevision>
  <AppxSymbolPackageEnabled>false</AppxSymbolPackageEnabled>
</PropertyGroup>
<!-- JJQR - END -->
```

**IPrintDocumentFactory** (Core/Print/PrintDocumentFactory.cs, not my file) — add to the interface:
`Task<PrintDocument> PaymentReceiptAsync(Guid bookingId, CancellationToken ct = default);`
`ClinicFormsPrintDocumentFactory` already implements it; the old `PrintDocumentFactory` needs a stub
(`=> throw new NotSupportedException()`) or the same body, or the interface won't compile.

**Optional IPrinter change** (Core/PrintSpec.cs, not my file): if you prefer one interface, add
`Task PrintReceiptAsync(ClinicNode.Core.Printing.ReceiptPrintModel model, CancellationToken ct = default);` to `IPrinter` —
`MauiPrinter` already implements it.

## Printer settings (optional) — `{AppData}/ClinicNode/printing.json`

```json
{ "queueSlipStyle": "Thermal80", "thermalTransport": "EscPosNetwork", "thermalPrinterHost": "192.168.1.50", "thermalPrinterPort": 9100 }
```

`queueSlipStyle`: `Web` (default, the web slip) | `Thermal80` | `Thermal58`. `thermalTransport`: `Driver` (default, print dialog) |
`EscPosNetwork` (Mac + Windows) | `EscPosWindowsSpooler` (+ `thermalPrinterName`, USB printers). `showDialog: false` + `formsPrinterName` /
`thermalPrinterName` prints without a dialog (Mac: printer URL `ipp://…`; Windows: printer name).

## Print smoke test (Mac)

```bash
cd packaging/print-smoke && dotnet build && "bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/ClinicNode Print Smoke.app/Contents/MacOS/PrintSmoke" out
# writes out/*.pdf (rx, lab, medcert, slips 80/58/web, receipt ± pending sync) + sizes.txt; add --print to open the print dialog for each
```
<!-- JJQR - END -->
