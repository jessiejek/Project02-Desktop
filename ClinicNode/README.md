<!-- JJQR - AI (Nov 21, 2026) -->

# Clinic Node (native .NET MAUI)

Offline-first desktop app for the Philippine family-medicine clinic. Native controls — no embedded browser, no Node, no View Source.

See `../docs/MAUI-ARCHITECTURE.md`, `../docs/NATIVE-SCREEN-INVENTORY.md`, and `../docs/API-SURFACE.md`.

The superseded Photino spike remains in `../ClinicDesktop/` (do not delete).

## Build (Mac)

```bash
cd Project02-desktop/ClinicNode
dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj
dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst
```

Run (Debug seeds demo accounts: `doctor@clinic.test` / `staff@clinic.test` / `admin@clinic.test`; password is `DevDataSeeder.DevPassword`). Disable demo with `CLINIC_NODE_DEMO=0`.

```bash
dotnet run --project ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst
```

The owner builds the Mac DMG themselves. The Windows TFM (`net10.0-windows10.0.19041.0`) is included when the solution is built on Windows (MSIX comes later).

Do not commit, push, or deploy the backend to `main` without an explicit "push" — a backend push auto-deploys to production.
