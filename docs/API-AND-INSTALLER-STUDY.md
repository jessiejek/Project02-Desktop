# Clinic Node — using your API, certifying an installed copy, and building the Windows installer from a Mac

Date: 22 Nov 2026 · Follows `WINDOWS-INSTALLER-STUDY.md` (what has to be done to ship on Windows). This document answers three questions.

---

## 1. If I have an API available, how does the desktop use it?

**The desktop already talks to your API. It is online-first by design** (see `ONLINE-FIRST.md`). What is missing is only *where the API lives* on an installed copy.

### How it works today
| Piece | What happens |
|---|---|
| Address | `MauiProgram.cs` reads **one environment variable**, `CLINIC_NODE_CLOUD_URL`. Empty = the app runs offline-only. |
| Sign-in | `OnlineFirstAuthService`: calls `POST /api/auth/login` first. Wrong password = rejected (no local fallback). Only an *unreachable* server falls back to the local password check. |
| Tokens | Access token about 1 hour. The refresh token is stored in the OS secure store (macOS Keychain / Windows Credential Vault). The password is never saved. |
| Reads / writes | Every screen calls the same endpoints as the web. If the server answers, that is final. If the network fails, the app uses the local SQLite copy and queues the change. |
| Live updates | A SignalR hub (queue, payments, check-ins) plus a catch-up pull every 60 seconds. |
| Sync | `/api/sync/*` pushes queued offline changes and pulls changes from the server. |

### What your API must provide
- **HTTPS** with a normal certificate (Let's Encrypt is fine). Plain `http://` should only be used on a LAN or for testing.
- The same endpoints the web uses (`docs/API-SURFACE.md`), the SignalR hub, and `/api/sync/*`.
- No CORS setup is needed: a native app is not a browser.
- A stable address that does not change. Use a domain name, not an IP, so the server can move later.

### What has to be built (about 0.5–1 day)
1. **Put the server address into the installed app.** Choose one:
   - **Baked in** at build time (simplest: one clinic, one server).
   - **A field on a first-run / "This computer" settings screen** (best if the address may change or several clinics use the app).
   - **A config file next to the app**, written by the installer.
   The environment variable stays as a developer override.
2. **Register the computer with the server** once (section 2 below). Until then the top bar says "Online · not registered for sync". Everything works, but offline changes cannot sync.
3. **Health check on first run:** show "Cannot reach the clinic server" with the address, instead of silently working offline.

### Checklist for go-live
- [ ] API reachable over HTTPS from the clinic's internet.
- [ ] Default address built in or settable.
- [ ] One doctor, one staff and one admin account created on the server.
- [ ] Each clinic computer registered.
- [ ] Offline test: unplug the internet, do a visit, plug back in, confirm it syncs.
- [ ] Backup of the server database.

---

## 2. If we can generate an installer, how do we certify that copy to be installed and use our API?

"Certify" means four different things. Each has a different tool.

| Question | Answer | Tool |
|---|---|---|
| Is this installer **really from us** and not tampered with? | Sign the installer. | **Code-signing certificate** |
| Is this **computer allowed** to use our API? | Register it; the server issues a secret for that computer. | **Node registration** (already built) |
| Can we **switch off** a lost or stolen computer? | Revoke its registration; the server refuses it. | **Node revocation** (already built) |
| Is this **version still supported**? | The server rejects too-old versions. | **Protocol version check** (already built) |

### 2.1 Proving the installer is genuine (code signing)
- **Windows:** sign the installer with a code-signing certificate. Without it, Windows SmartScreen shows "Windows protected your PC" and some antivirus tools block it. Options:
  - A certificate from a certificate authority (roughly US$100–400 per year, identity check required).
  - **Azure Trusted Signing** (about US$10 per month; requires business verification, which can take days to weeks).
  - A self-signed certificate works only on test computers you set up yourself.
  - For MSIX, the manifest `Publisher` (`CN=Grace Medical Clinic`) must match the certificate subject exactly.
- **Mac:** Apple Developer ID certificate (US$99 per year) plus **notarization**. Without it, macOS Gatekeeper refuses to open the app on other Macs.

### 2.2 Allowing only approved computers (already built)
The flow in `HttpCloudSyncClient.RegisterAsync`:
1. An **administrator** signs in on the new computer and chooses "register this computer".
2. The app sends `POST /api/sync/nodes` with the admin's token, the computer's id, a name and the app version.
3. The server returns a **node secret**. The app stores it (`NodeCredentialStore`; on a real install it uses the OS secure store).
4. From then on every sync call proves itself with that secret. An unregistered copy of the installer can still sign someone in, but **it cannot sync**.

So the installer does not need to be "licensed". Anyone can install it, but only computers an admin has registered take part in sync.

**To make this stronger (optional):**
- Show the list of registered computers to the admin, with last-seen time and a Revoke button.
- Require an admin approval step before a new computer syncs.
- Add an install code (a one-time code shown on the admin screen) if you want the *login* itself restricted to approved computers. This needs a small server change.

### 2.3 Switching off a computer
The server answers `403 node_revoked`. The app already shows "Revoked" and stops syncing. Staff passwords can also be reset from the admin side.

### 2.4 Forcing updates
The server answers `426 protocol_unsupported` to an app that is too old. The app already shows "Needs update". Pair this with an auto-update path (MSIX `.appinstaller` on Windows, or a "download the new installer" link).

### 2.5 What certification cannot do
- It cannot stop someone copying the app files. Protect the *data* with accounts, the registration secret and HTTPS, not by hiding the program.
- The local database on the computer is **not encrypted today** (the encrypted-database project exists in `packaging/sqlcipher-tests/` but is not switched on). If a clinic laptop could be stolen, turn this on, together with Windows BitLocker / Mac FileVault.

---

## 3. How can I generate a Microsoft (Windows) installer using my Mac?

**You cannot build the Windows version of a .NET MAUI app on a Mac.** The Windows build needs the Windows SDK and WinUI tools, which only run on Windows. You need a Windows machine somewhere. Three ways:

| Option | What you need | Cost | Best for |
|---|---|---|---|
| **A. GitHub Actions (`windows-latest`)** | Your GitHub repo. A workflow builds on a Microsoft-hosted Windows machine and gives you the installer to download. | Free minutes included for most repos; paid beyond that (check current GitHub pricing). | **Recommended.** No Windows license or hardware. Repeatable builds. |
| **B. Windows 11 in a virtual machine on your Mac** | Parallels Desktop, VMware Fusion or UTM, plus a Windows 11 license. On Apple Silicon this is Windows 11 **ARM**; it builds x64 and ARM64 apps. | VM software (Parallels is paid, UTM is free) plus a Windows license. | Testing the install, printing and the thermal printer on a "real" Windows; debugging. |
| **C. A rented Windows PC in the cloud** (Azure, AWS or a Mac-in-cloud-style Windows rental) | A browser or Microsoft Remote Desktop. | Pay per hour. | Occasional builds without a VM. |

### Recommended setup
1. **Builds:** GitHub Actions on `windows-latest`:
   - install .NET 10 and the MAUI workload;
   - `dotnet publish -f net10.0-windows10.0.19041.0` (self-contained, so the PC needs no .NET installed);
   - make the installer (below);
   - upload it as a build artifact.
   You press a button on GitHub from your Mac and download the `.exe` or `.msix`.
2. **Testing:** a Windows 11 VM on the Mac (option B), or one real Windows PC at the clinic. A real Windows PC is still needed to test **printing** (the A5 certificate form, the thermal slip printer) because printers behave differently from one machine to another.

### Which installer file
| | **MSIX** | **Setup.exe (Inno Setup, unpackaged)** |
|---|---|---|
| Already in the repo | Yes: `packaging/windows/build-msix.ps1`, `Package.appxmanifest`, `.appinstaller` | No (small new script) |
| Install experience | Double-click, clean uninstall | Familiar wizard, Start menu shortcut |
| Auto-update | Built in | Needs our own update check |
| Signing | **Required** (a trusted certificate) | Optional, but SmartScreen warns without it |
| Runs on the Mac? | No, the tools run on Windows only | No, Inno Setup is Windows-only (it can run in the GitHub Actions machine) |
| Best for | Once a signing certificate is bought | A fast pilot on the first clinic PC |

**Suggested path:** pilot with `Setup.exe` (no certificate needed to start), then move to a signed MSIX with auto-update once a certificate is bought.

### Honest limits
- The Windows version has **never been built or run** in this project, so expect compile errors and look-and-feel fixes on the first build (see `WINDOWS-INSTALLER-STUDY.md`, about 4–6 days to a working install).
- The Mac installer (`packaging/mac/build-dmg.sh`) works today and does not need Windows.

---

## 4. Decisions needed from the owner
1. **Where will the API be hosted**, and what is its permanent address?
2. **One clinic or several?** One: bake the address in. Several: add a first-run address screen.
3. **Signing:** buy a code-signing certificate (or Azure Trusted Signing) now, or pilot unsigned on one PC first?
4. **Must the local patient data be encrypted** on the clinic computer (changes the work from optional to required)?
5. **Who registers a new computer?** Only the admin, using the existing "register this computer" flow?
6. A Windows PC or VM for testing, and the clinic's printer models.

## 5. Suggested order of work
| Step | Work | Estimate |
|---|---|---|
| 1 | Built-in or first-run API address; "cannot reach server" message | 0.5–1 day |
| 2 | Registered-computers list with Revoke on the admin screen (optional) | 0.5–1 day |
| 3 | GitHub Actions Windows build; first Windows build and fix compile errors | 1–2 days |
| 4 | Windows look-and-feel fixes, printing test on real printers | 1–2 days |
| 5 | Installer (Setup.exe pilot, then signed MSIX) | 0.5–1 day |
| 6 | Mac: Developer ID signing and notarization | 0.5 day |
| 7 | Install on a clean PC, full walkthrough, fix findings | 1 day |
