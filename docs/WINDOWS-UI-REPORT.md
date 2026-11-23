# Windows UI report (23 Nov 2026, branch `windows-ui`)

Short version: on Windows the app now draws about 20% smaller, opens maximised, keeps tall dialogs on the screen, blocks double clicks, shows a hand cursor, and supports Esc and Enter.
**The Mac build compiles exactly the same code as before.** Every new line is inside `#if WINDOWS` / `#elif WINDOWS` or in `Platforms/Windows/`.

## What was wrong on Windows
- **Everything was too big.** The Mac draws the whole app at 77% (`Platforms/MacCatalyst/Info.plist` UIDeviceFamily 2, the iPad look). Windows drew it at 100%, and display scaling (125% / 150%) made it bigger again. About 520 sizes are written directly into the screens' code, so no shared style could fix it.
- **The window was bigger than the screen.** It opened at 1360 x 860, but this laptop at 150% only has 1280 x 752 usable.
- **Tall dialogs ran off the bottom.** Register patient, Connection and the developer log have fixed body heights, so their buttons could be hidden.
- **Pop-ups could open in the wrong place inside dialogs.** Only the Mac asked the system where the field was.
- **No double-click guard on Windows.** The Mac ignores a second click within 800 ms; Windows did not, so a double click could save twice.
- **No hand cursor, no Esc, no Enter in dialogs.**

## What changed (file by file)
| File | Change | Lines added |
|------|--------|-------------|
| `Platforms/Windows/WindowsUiScale.cs` (new) | One zoom for the page area of every window (main screen and every dialog). The page is laid out bigger and drawn smaller, so every font, padding, column and table shrinks together. The Windows title bar and native Windows alerts are not scaled. | 64 |
| `Platforms/Windows/NativeUiEnhancements.cs` (new) | The Windows twin of the Mac file. It adds an 800 ms double-click guard (blocks the click, no grey flash), a hand cursor over buttons and anything clickable, and a light hover tint on buttons. It also hooks the scale and the keys to each window. | 61 |
| `Platforms/Windows/WindowsKeys.cs` (new) | **Esc** closes the open pop-up first; otherwise it closes the top dialog through its own close button, so "no closing while saving" still works. **Enter** in a one-line field of a dialog presses the dialog's only blue button. It never fires in multi-line boxes, never presses red (Danger) buttons, and does nothing when there are two blue buttons or the button is disabled. | 119 |
| `Controls/ClinicTheme.cs` | `WindowsUiScale(displayScale)` inside `#if WINDOWS` (lines 17-23). | 7 |
| `Controls/ClinicPopup.cs` | `#elif WINDOWS` branch in `PositionIn` (lines 39-51) asks WinUI for the field's real position. `IsOpen` helper for Esc inside `#if WINDOWS` (lines 120-125). | 18 |
| `Controls/ClinicModal.cs` | `#if WINDOWS` (lines 81-95). The dialog body is capped to the room the window has, so the body scrolls and the footer buttons stay visible. Each dialog is also registered so Esc and Enter can find it. | 15 |
| `App.xaml.cs` | `#if WINDOWS` (lines 73-92), after the untouched line 71. Minimum window is 1024 x 640. The window opens maximised (like the Mac zoom); the restore size is 90% of the screen, centred. | 20 |
| `MauiProgram.cs` | One line inside `#if WINDOWS` (lines 100-102) that registers the Windows file above. | 3 |

No existing line was changed, moved or reformatted. `git diff --numstat main...windows-ui` shows **0 deleted lines** in every file.

## Scale values and why
- The setting is `ClinicTheme.WindowsUiScale` (`Controls/ClinicTheme.cs:21`): **100% -> 0.9, 125% -> 0.85, 150% and above -> 0.8.**
- The Mac uses 0.77. Windows scaling already enlarges the app, so the higher the Windows scale, the more we shrink.
- On this laptop (1920x1200 at 150%), 0.8 gives about the same physical text size as a MacBook while staying a little easier to read.
- To tune, change only these three numbers.

## THE MAC-SAFETY PROOF
| Changed file | Why the Mac cannot see it |
|--------------|---------------------------|
| `ClinicNode.App/Platforms/Windows/WindowsUiScale.cs` | It lives in `Platforms/Windows/`. MAUI compiles that folder for the Windows target only; the Mac and `net10.0` builds never include it. |
| `ClinicNode.App/Platforms/Windows/NativeUiEnhancements.cs` | Same: `Platforms/Windows/`. |
| `ClinicNode.App/Platforms/Windows/WindowsKeys.cs` | Same: `Platforms/Windows/`. |
| `ClinicNode.App/Controls/ClinicTheme.cs` | All 7 new lines sit between `#if WINDOWS` (line 17) and `#endif` (line 23). |
| `ClinicNode.App/Controls/ClinicPopup.cs` | The new branch starts with `#elif WINDOWS` (line 39), after the existing `#if MACCATALYST` branch. On the Mac, `MACCATALYST` is true, so the `#elif` is skipped and the Mac code is the same as before. The helper is inside `#if WINDOWS` ... `#endif` (lines 120-125). |
| `ClinicNode.App/Controls/ClinicModal.cs` | All 15 new lines sit between `#if WINDOWS` (line 81) and `#endif` (line 95). |
| `ClinicNode.App/App.xaml.cs` | All 20 new lines sit between `#if WINDOWS` (line 73) and `#endif` (line 92). The shared line 71 (`window.Width = 1360; ...`) is unchanged. |
| `ClinicNode.App/MauiProgram.cs` | The one new line sits between `#if WINDOWS` (line 100) and `#endif` (line 102). |
| `docs/*` | Documents and screenshots only. |

- Shared-line changes: **none.**
- Business logic, data, sync, printing, updates, permissions and the backend are not touched.

## Verified on this Windows laptop (150%)
- The Windows publish command from the workflow (`-p:ClinicWindowsOnly=true`) succeeds.
- `dotnet test ClinicNode/ClinicNode.Tests --filter "FullyQualifiedName~Sync|FullyQualifiedName~Update"`: **160 passed, 0 failed, 3 skipped.**
- The app starts without crashing, opens maximised and draws smaller. Text is sharp (`docs/windows-ui/after-150-header.png`).
- **Clicks land in the right place under the scale.** A real mouse click on the top-bar search box opened the Search dialog. The dialog is scaled too (`docs/windows-ui/after-150-search-dialog.png`).
- **Esc closed that dialog.**
- Screenshots are cropped so they show no patient data. The app was already signed in from an earlier session; nobody signed in, and nothing was saved or changed.

## Not verified / still not good
- **Not verified:**
  - Every screen for doctor, staff and admin at 100% and 125%. Only 150% was checked, and only the doctor's Today screen plus the Search dialog.
  - Before/after pairs for all screens. The owner needs to take them (Settings > System > Display > Scale).
  - The Enter key in a real dialog.
  - The hand cursor and the double-click guard by hand.
  - A pop-up date picker inside a dialog.
  - Very tall dialogs on a short window.
- **Still not good (seen at 150%):** in the doctor's Today queue, the "Print Lab" button in a row is cut at the right edge of the table. It's a fixed column width on that screen, not fixed here; it needs a per-screen look.
- **Mac build:** not possible on Windows (Mac Catalyst needs a Mac with Xcode). The reviewer must run `dotnet build ClinicNode/ClinicNode.App -f net10.0-maccatalyst` on the Mac.
- **Native Windows alerts:** DisplayAlert dialogs, the system drop-downs and the consultation menu are drawn by Windows and are not scaled. That's normal Windows size.

## For the reviewer
```
git fetch && git diff main...windows-ui
git diff --numstat main...windows-ui     # every code file: N added, 0 deleted
```
Do not merge until the Mac build has passed and the owner has looked at the screens.
