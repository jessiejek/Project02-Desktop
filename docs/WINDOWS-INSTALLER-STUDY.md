# Clinic Node — Windows installer: feasibility study

Date: 21 Nov 2026 · Scope: can we ship Clinic Node to clinic PCs as a Windows installer, and what does it take.

## 1. Answer

**Yes, it is feasible and most of the groundwork is already in the repo.** The app is .NET MAUI, which targets Windows natively, so this is not a rewrite.
Realistic effort to a first working installer on a real clinic PC: **about 4–6 working days**, and almost all of it is verification
on Windows, because the Windows build has never been compiled or run in this project (all work so far was on a Mac).

Hard constraint: **a Windows build cannot be produced on a Mac.** It needs a Windows machine, a Windows VM, or a cloud Windows build server (GitHub Actions `windows-latest`, no hardware needed).

## 2. What already exists

| Item | Where | State |
|---|---|---|
| Windows target framework | `ClinicNode.App.csproj` (`net10.0-windows10.0.19041.0`, min Windows 10 1809) | Declared, only active when building on Windows |
| MSIX package build script | `packaging/windows/build-msix.ps1` | Written, never run |
| Auto-update feed | `packaging/windows/ClinicNode.appinstaller` | Template (host name to fill in) |
| MSIX manifest + start-with-Windows task | `Platforms/Windows/Package.appxmanifest`, `WindowsStartup.cs` | Written, never run |
| Windows printing | `Platforms/Windows/Printing/` (PDF print, renderer, raw/thermal printer) | Written, never run |
| Windows branches in shared code | `MauiPrinter`, `FileSaver`, `AppRestart` (`#if WINDOWS`) | Written, never run |
| Encrypted-database test project | `packaging/sqlcipher-tests/` | Exists; encryption itself is still OFF |
| Mac installer (for comparison) | `packaging/mac/build-dmg.sh` | Works |

## 3. Gaps found (what actually has to be done)

1. **Never built on Windows.** Expect compile errors in the Windows-only code on first build (typically small).
2. **The server address is only read from an environment variable** (`CLINIC_NODE_CLOUD_URL`, `MauiProgram.cs`). A clinic PC installed from an installer has no such variable, so the app would start offline-only. Needs a default production URL (baked in, or an installer/config-file setting, or a field on the "This computer" settings screen). **Must fix for any installer, Windows or Mac.**
3. **Mac-only look fixes have no Windows equivalent.** Text-box borders and focus rings are customized in `#if MACCATALYST || IOS` handlers. On Windows, text boxes will show the default WinUI look until equivalent handlers are added.
4. **The sidebar is built for Mac Catalyst's Shell** (it lives in the flyout header because a custom flyout did not render there). WinUI's Shell flyout behaves differently; the sidebar needs checking and probably a small adjustment.
5. **Printing flow differs.** On the Mac, documents open in the browser and print automatically. On Windows the PDF/print-dialog path and thermal slip printer (ESC/POS) code exist but are unverified; the A5 pre-printed certificate form must be test-printed on the real printer.
6. **Window size.** The "zoom to fill the screen" fix is Mac-only; Windows needs a maximize-on-start equivalent (one line).
7. **Encryption.** The app currently uses the plain SQLite bundle. If patient data must be encrypted on the PC, the SQLCipher bundle has to be switched on for Windows (test project exists).
8. **Icons and branding.** The manifest uses `$placeholder$` logos; real tile/logo images are needed.
9. **Code signing** (see below) — required for a clean install experience.

## 4. Installer options

| | A. MSIX (+ auto-update) | B. Classic `Setup.exe` (Inno Setup, unpackaged) |
|---|---|---|
| Install experience | Double-click, clean install/uninstall | Familiar wizard, Start-menu shortcut, uninstaller |
| Auto-update | Built in (`.appinstaller`, checks on every launch) | Not built in; needs our own update check |
| Signing needed to run without scary warnings | **Yes, a trusted certificate is required** (self-signed must be imported on every PC) | Runs unsigned, but Windows SmartScreen warns; signing removes the warning |
| Script already in repo | Yes | No (new, small) |
| Self-contained (no .NET install on the PC) | Yes | Yes |
| Best for | Owner is willing to buy a signing certificate | Fastest path to the first clinic PC |

**Recommendation:** pilot with **B** (no certificate dependency, can be on a PC within days), then move to **A** once a code-signing
certificate is bought, because auto-update matters once there is more than one PC. Do **not** invest in both long term; pick one after the pilot.

## 5. Signing certificate (owner decision)

- Needed to avoid install warnings and to enable MSIX auto-update.
- Options: a code-signing certificate from a certificate authority (roughly US$100–400 per year, requires identity checks), or **Azure Trusted Signing**
  (about US$10 per month, requires business verification, which can take days to weeks). A self-signed certificate works only for test PCs.
- The manifest `Publisher` (`CN=Grace Medical Clinic`) must equal the certificate's subject exactly.

## 6. Build automation

Use **GitHub Actions on a `windows-latest` runner**: install .NET 10 + the MAUI workload, run `dotnet publish -f net10.0-windows10.0.19041.0`,
produce the installer, upload it as a build artifact (and later sign it with a certificate stored as a repository secret). This removes the need to own a
Windows PC for builds. A real Windows PC is still needed to **test** (install, printing, thermal printer, restart).

## 7. Plan

| Step | Work | Est. |
|---|---|---|
| 1 | Windows machine or VM available; build once; fix compile errors | 0.5–1 day |
| 2 | Default server address / config (also needed on Mac) | 0.5 day |
| 3 | Windows look fixes: text boxes, focus, sidebar/flyout, maximize on start | 1 day |
| 4 | Printing on Windows: prescription, A5 certificate, receipt, thermal slip on the clinic's real printers | 1 day |
| 5 | Installer (Setup.exe or MSIX) + GitHub Actions build | 0.5–1 day |
| 6 | Encryption on (if required) + data-backup/restore check | 0.5 day |
| 7 | Install on a clean PC, first-run walkthrough, fix findings | 1 day |
| **Total** | | **~4–6 days** |

## 8. Risks

- **Unknown unknowns on first Windows build** — the biggest uncertainty; the estimate above includes a buffer but not a rewrite of any screen.
- **Printers.** Real behaviour of the clinic's printers (paper size, thermal slip) can only be confirmed on site.
- **Certificate lead time** if Azure Trusted Signing verification is slow (mitigated by the unsigned pilot).
- **Windows 10 end of support** — target Windows 10 22H2 and Windows 11; both are supported by this design.

## 9. What we need from the owner

1. A Windows 10/11 PC (or VM) for testing, and the clinic's printer models.
2. Decision: pilot with `Setup.exe` first, or wait for a signing certificate and go straight to MSIX.
3. Whether patient data on the PC must be encrypted (changes step 6 from optional to required).
4. The production server address the installed app should connect to.
