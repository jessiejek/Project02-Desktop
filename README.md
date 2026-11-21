# Project02-Desktop

Clinic Node: the native .NET MAUI desktop app for the clinic (doctor, staff and admin portals), online-first with a local SQLite copy that syncs to the backend.

- `ClinicNode/` the MAUI solution (App, Core, Data, Sync, Api, tests)
- `docs/` architecture, API surface, sync contract, parity audits, `HANDOVER.md`
- `packaging/` Mac and Windows packaging scripts
- `ClinicDesktop/` the earlier web-wrapper spike (superseded)

Run on a Mac: `cd ClinicNode && scripts/run-mac.sh`. Set `CLINIC_NODE_CLOUD_URL=http://localhost:5055` to connect to the local backend.
