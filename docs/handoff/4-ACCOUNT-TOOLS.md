# accounts-tools: small one-off tools for the LIVE database

These talk straight to the live database (host, database and login are built in; you type only passwords, hidden).
They use the backend's own password hashing, so accounts behave exactly like ones made in the app.
Needs: the .NET 10 SDK, and the `Project02-be` folder next to `accounts-tools` (they reference its projects).

| Command | What it does |
|---|---|
| `bash accounts-tools/create-staff.sh` | creates the staff login `staff@gmail.com` |
| `bash accounts-tools/create-account.sh Staff "email" "Full name"` | creates any Staff login |
| `bash accounts-tools/create-account.sh Doctor "email" "Dr. Full Name"` | creates a Doctor (asks specialization + fee) |
| `bash accounts-tools/reset-password.sh "email"` | sets a new password for an existing account and clears any lock-out |
| `bash accounts-tools/reset-doctor-password.sh` / `reset-staff-password.sh` | shortcuts for the two accounts above |
| `purge-ONE-TIME-already-run/` | the purge that deleted every patient except 8 (22 Nov 2026). Kept as a record. **Do not run again.** It has a dry-run by default and needs the typed word PURGE |

Passwords must be 12+ characters. The tools never print or store what you type.
The admin login (`jessiejayr@gmail.com`) was made with the same tool (`make-account`, role Admin was added at the time; to make another Admin, edit `MK_ROLE` handling in `make-account/Program.cs`).
