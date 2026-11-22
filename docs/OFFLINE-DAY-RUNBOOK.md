# Offline day: one-page runbook (Grace Medical Clinic)

For the doctor and the admin. The app never switches to Offline by itself. You decide.

## The rule of the day
- Use **one** screen for the whole day: either the desktop **or** the tablet (web). Not both.
- To move from one to the other: finish the consultation, let the desktop sync until "changes waiting" is 0 (it does this by itself when you sign out), sign out, then use the other screen.

## Before the day (admin, once per computer)
1. Admin: **Admin > Sync computers > Register this node** on the clinic computer (once). Without this the computer cannot go Offline.
2. Doctor: sign in. The first sign-in of the day syncs by itself (press **Skip** only if you must).

## Switching to Offline (doctor only)
- Tap the pill at the top (the dot and "Online 12 ms"), then **Switch to Offline mode**.
- The app may *suggest* it ("Connection is poor...") when the internet is bad. Press **Not now** to stay Online.
- If you try to save while Online and the internet is down, you will see **"Not saved"** with a button to switch. Nothing is saved behind your back.
- In Offline mode **only the doctor** can use the computer. Staff and Admin cannot sign in until it is Online again.

## During the day (Offline)
1. **Find or register the patient** (New walk-in consult). Registering works Offline.
2. **Queue number:** a number comes from the numbers reserved earlier. If none is left the patient is **Paper queue**: staff keep the paper list, the doctor adds the patient when they enter the room. The server gives a number at sync (the slip may need reprinting: the app says so).
3. **Consult** as usual. Everything is saved on this computer.
4. **Payment:** on the visit, **Mark payment received**, choose the method, and **type the receipt number from the booklet**. The same number cannot be used twice on this computer.
5. **Printing** works Offline (slip, prescription, lab order, certificate, receipt), through the computer's own printer.

## End of the day
- Tap **Sync now** (top pill > Sync now), or just **sign out / quit**: the app syncs first.
- Read the result: *Sent*, *Accepted*, *Needs review*. If "Still waiting" is not 0, the server did not take everything. Your work is safe. Try again when the internet is better.
- Switching back to **Online** also syncs first.

## If something goes wrong
| What you see | What it means | What to do |
|---|---|---|
| Top bar says **Sync problem** | A change was refused or has failed 10 times | Tell the admin. Keep working. Nothing is lost. |
| **Needs review: N items** | The server accepted your work but wants a person to look | Admin: web > **Sync review** |
| Sign-in says "last synced on ... had to sync by ..." | More than 7 days without a good sync | Connect to the internet and sign in once so it can sync |
| "This computer is not registered" | Admin has not registered it | Admin registers it (see above) |
| Possible duplicate patient | Two records look like one person | Admin: **Sync review > Compare and merge**. Never merge by guessing. |
| Receipt number used twice | Two payments carry the same booklet number | Admin: check the booklet, then **Mark resolved** with a note |

## Admin: sync review (web)
Admin > System > **Sync review**. Open an item. For a duplicate: compare the two records, choose the one to **keep**, write a note, **Merge patients** (cannot be undone from the page). Everything the other record held moves to the kept one. For other items: do what the text says, then **Mark resolved** with a note.

## Limits to remember
- Closing the app with the red window button does not quit it. Use **Quit** (it syncs first).
- Macs shutting down or Dock > Quit cannot ask first: anything not yet synced stays safe on the computer and goes next time.
- Wrong clock? If the computer's date is off, the morning sync may run again. That is harmless.
