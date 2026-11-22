# Clinic Node: how we hand the app to clinic computers, and how it updates itself (OTA)

Date: 22 Nov 2026. Decisions are made here so work can start; the owner can change any of them.

## 1. The decision in one paragraph
Build both installers in **GitHub Actions** whenever a version tag is pushed (`v1.4.0`). Publish the files to **GitHub Releases** (free, fast CDN, HTTPS) and a small signed `latest.json` to **GitHub Pages** under a free subdomain of the owner's domain (e.g. `updates.<domain>`). The app checks `latest.json`, downloads the new version in the background, **verifies it**, and installs it only at a safe moment (after sign-out or Quit, when sync is finished). The clinic servers (myASP) are NOT used for downloads: they are slow from the Philippines and should only serve the API.

## 2. Why not the myASP server for downloads
Measured: 0.19 s per round trip and 0.6 s per new connection to the US server. A 100 MB installer would be slow and would load the same server the clinic needs for patients. GitHub's CDN has Asian edge locations and costs nothing.

## 3. Pieces

| Piece | What it is | Where |
|---|---|---|
| Release workflow | Builds Mac + Windows on a tag, signs, uploads, writes the manifest | `.github/workflows/release-desktop.yml` (Project02-desktop) |
| Binaries | `ClinicNode-1.4.0.dmg` / `.zip` (Mac), `ClinicNode-1.4.0.msix` or `Setup.exe` (Windows) | GitHub Releases (public releases repo, or this repo if public) |
| Manifest | `latest.json` + signature | GitHub Pages, custom subdomain |
| In-app updater | Checks, downloads, verifies, installs, relaunches | `ClinicNode.Core/Update/*`, UI in the connection panel and a banner |
| Server guard (exists) | API answers `426 protocol_unsupported` to an app that is too old | backend, already built |

### The manifest (`latest.json`), signed with an Ed25519 key
```json
{
  "channel": "stable",
  "version": "1.4.0",
  "min_version": "1.2.0",
  "released_at": "2026-12-01T09:00:00+08:00",
  "notes": ["Faster consultation", "Fix: ..."],
  "mac":   { "url": "https://.../ClinicNode-1.4.0.zip",  "sha256": "...", "size": 98000000 },
  "win":   { "url": "https://.../ClinicNode-1.4.0.msix", "sha256": "...", "size": 91000000 },
  "rollout_percent": 100
}
```
The app has the PUBLIC key built in and refuses a manifest whose signature does not match. So even if the download host were hacked, a fake update is rejected.

## 4. The update flow inside the app
1. **Check**: at start, then every 4 hours, and when the user presses "Check for updates" (connection panel). Quiet failure when offline.
2. **Decide**: newer than this version? Is this computer inside the rollout percent (hash of its node id)? Is the channel right?
3. **Download** in the background to a temp folder; resume if interrupted; check size and **SHA-256**; then check the platform signature (Mac: `codesign --verify` + team id; Windows: package signature).
4. **Offer**: a calm banner "Version 1.4.0 is ready. It installs the next time you sign out or quit." Never in the middle of a consultation, a payment or a sync.
5. **Install** at a safe moment: (a) the normal sign-out / Quit path (already syncs first), then (b) a tiny helper replaces the app and reopens it.
6. **After start**: the app repairs its local database (already exists: SchemaRepair), runs a sync, and reports its version to the server (so the admin sees every computer's version on "Sync computers").
7. **Mandatory**: if `min_version` is above this version, or the server says 426, the banner becomes a blocking "Update required" screen after the current work is saved; Offline mode keeps working until then.

## 5. Platform mechanics
- **Mac (the current clinic computer):** Developer ID signing + notarization (Apple Developer, US$99/year) is required, otherwise macOS Gatekeeper blocks other Macs. Updater: download the signed ZIP, verify, quit, a helper script waits for the app to exit, swaps the `.app` in `~/Applications` (per-user install, so no admin password is needed), and reopens it. Sparkle is the usual Mac tool, but it does not fit a Mac Catalyst app cleanly, so we use the small custom updater above.
- **Windows (if clinic PCs are Windows):** MSIX with `.appinstaller` has automatic update built in (checks at every launch); this needs a signing certificate (Azure Trusted Signing about US$10/month, or a standard certificate). Pilot with a plain `Setup.exe` first if the certificate is not ready, using the same custom updater.

## 6. Safety rules
- Keep the previous version on disk; one click "Go back" if the new one fails to start twice in a row (automatic rollback after two failed starts).
- **Staged rollout**: first 10 %, then 50 %, then 100 % (the manifest field `rollout_percent`).
- **Channels**: `stable` for clinics, `beta` for the owner's own Mac.
- Database changes are always backward compatible for one version (new columns are added before the code that needs them), so a computer that updates late still syncs.
- Never auto-install while there are unsynced changes the user has not been told about.

## 7. Cost
| Item | Cost |
|---|---|
| Domain / subdomain | free (owner already has) |
| GitHub Releases + Pages + Actions | free for this size |
| Apple Developer Program (Mac signing + notarization) | US$99 / year |
| Windows signing (only if Windows PCs) | about US$10 / month (Azure Trusted Signing) |

## 7b. GitHub secrets to add later (owner types them, never in chat)
`APPLE_CERT_P12` (base64), `APPLE_CERT_PASSWORD`, `APPLE_API_KEY` (notarization), `UPDATE_SIGNING_KEY` (Ed25519 private key), `WINDOWS_CERT` (when needed).

## 8. Phases (work order)
| Phase | What | Result | Needs from owner |
|---|---|---|---|
| 1 | Release workflow builds the Mac app on a tag, makes a ZIP/DMG, writes `latest.json`, publishes to Releases + Pages. The app shows "Version X is available" (notify only) | You see the whole chain work end to end | A public releases repo name (I will create `Project02-releases` instructions), the subdomain |
| 2 | Download + verify (hash, signature) + install at sign-out + relaunch + rollback | Real OTA on the Mac | Apple Developer account (US$99) for notarization |
| 3 | Windows build in Actions (`windows-latest`), MSIX/Setup.exe, same manifest | Windows clinics supported | A Windows test PC or VM; signing choice |
| 4 | Staged rollout, channels, version column on "Sync computers", forced minimum version | Safe operations | none |

## 9. First install on a new computer
1. Download the installer from the update page (`https://updates.<domain>`): one big button for the right system.
2. Open it. The app starts with the live server address already set (no environment variable).
3. An admin signs in and presses **Register this computer** (already built).
4. First sync fills the computer (about 12,700 reference rows plus the clinic's data).


## 10. What was actually built (22 Nov 2026) and what we learned
- **Version** is `ApplicationDisplayVersion` in `ClinicNode.App.csproj` (0.2.0 now). It shows in the sidebar footer and in the Connection panel ("App version").
- **Feed:** `latest.json` + `latest.json.sig` (ECDSA P-256, signed with the owner's private key in `_secrets/`, never in a repo). The app and `install.sh` carry the public key and refuse an unsigned or altered feed. Hosted in a small PUBLIC GitHub repo `Project02-releases` (raw files, free).
- **In the app:** `AppUpdateService` checks 20 s after start and every 6 h; a chip "Update X.Y.Z" appears in the top bar; Connection panel has "Check for updates". The download is verified against the SHA-256 in the signed feed.
- **Sandbox finding:** the Mac app is sandboxed, so it CANNOT replace itself in Applications. The flow is: download + verify + open the DMG, the person drags it onto Applications (Replace). Or paste the one-line installer (`install.sh`), which installs with no warning. On Windows (not sandboxed) a silent in-place update is possible later.
- **Release build:** a true Release (trimmed + AOT) Mac build crashes at start today, so the DMG is made from the normal (JIT) build with production defaults (`-p:ClinicProd=true`). Making Release safe is a separate job.
- **To publish a version:** bump the version, then `UPDATE_KEY=../_secrets/update-signing-private.pem REL_DIR=../Project02-releases packaging/release/make-release.sh "note 1" "note 2"`, then commit and push `Project02-releases`.
