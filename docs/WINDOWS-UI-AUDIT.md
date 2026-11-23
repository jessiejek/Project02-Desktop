# Windows UI audit (draft, 23 Nov 2026)

Branch: `windows-ui`. Draft from **reading the code only**. Nothing here comes from screenshots yet.
Anything marked **NOT VERIFIED** still needs the owner to run the app on Windows, sign in themselves, and take screenshots at 100% / 125% / 150%.

Rule for every fix: **the Mac must not change.** Every fix goes in `Platforms/Windows/` (compiled only for Windows) or inside `#if WINDOWS`.

## 0. This laptop
- The screen is 1920x1200 at **150%** scaling (AppliedDPI 144). Windows lays out to **1280 x 800**, and the usable area above the taskbar is **1280 x 752**.
- The per-monitor DPI manifest is already present (`Platforms/Windows/app.manifest`, `PerMonitorV2`). That should keep text sharp. NOT VERIFIED on screen.
- The Windows build from the workflow command succeeds (Release, win-x64, `-p:ClinicWindowsOnly=true`).

## 1. Why Windows looks bigger (root cause)
- `Platforms/MacCatalyst/Info.plist:9-12` sets `UIDeviceFamily` = `2` (the iPad look). In that mode macOS draws the whole app at **77%** ("Scaled to match iPad"). So FontSize 14 in the code shows as about 10.8 pt on the Mac.
- Windows draws the same 14 at full size, then makes it bigger again with display scaling (125% / 150%).
- So every size in the code was tuned for a picture that macOS shrinks to 0.77. Windows has no shrink.
- On this laptop at 150% the window can show about **1280 units across**. A full-screen MacBook window shows about 1440 pt / 0.77, roughly 1870 units. **Windows shows about two thirds of what the Mac shows** (estimate, NOT VERIFIED with a Mac side by side).
- There is no scale mechanism in the app today. No UiScale, FontScale or zoom setting exists in `Controls/ClinicTheme.cs` or `Resources/Styles/Styles.xaml`.

## 2. Where the sizes live
- **Shared styles** (`Resources/Styles/Styles.xaml`):
  - Headline font sizes: 24/22/18/15 at lines 11, 16, 21, 26; DisplaySm 30 at line 68.
  - Body font sizes: 15/14/13 at lines 31, 36, 41; labels 12 at lines 77 and 83.
  - Buttons: padding 16,8 and min height 36 at lines 139-141, 153-154, 164-165, 174-175.
  - Entry, Picker and DatePicker: min height 38 at lines 194, 211, 218. Editor: min height 80 at line 203.
  - Cards: padding 16 at line 234, AuthCard 32 at line 237.
  - Tables: header row 16,9 and min height 36 at lines 382-383; body row 16,10 and min height 42 at lines 387-388.
  - DialogCard: max width 560 at line 409. Toast: min width 320 and max width 480 at lines 419-420.
- **Theme constants** (`Controls/ClinicTheme.cs`): SidebarWidth 220 at line 12, MinTouch 38 at line 13.
- **Hard-coded sizes in C#:** about 520 inline FontSize, Width, Height and Padding values across about 40 files, which the styles don't reach.
  - Biggest files: `Views/Doctor/ConsultationPage.xaml.cs` (80), `Views/Doctor/Extra/DoctorPatientChartPage.cs` (35), `Controls/ClinicPopup.cs` (32), `Views/Shared/TopBar/TopBarView.cs` (24), `Views/Staff/WalkInWizardView.cs` (21), `Views/Admin/AdminSettingsPage.xaml.cs` (16), `Controls/ClinicSidebar.cs` (16), `Views/Doctor/TodayPage.cs` (15).
  - Inline font sizes used most often: 14 (33 times), 13 (31), 11 (22), 15 (21), 12 (20).
  - **Conclusion:** editing styles alone, or screens one by one, will not fix this. A whole-window Windows scale is the only single switch that reaches every screen.

## 3. Findings (Windows vs the intended Mac look)

| # | Area | Problem on Windows | Where | Status |
|---|------|--------------------|-------|--------|
| 1 | Everything | Drawn at 100% plus display scale, while the Mac draws at 77% | `Platforms/MacCatalyst/Info.plist:9-12` | Code, certain |
| 2 | Window size | Opens at 1360 x 860, bigger than this laptop's 1280 x 752 usable area, so the bottom and right edges fall off screen. The minimum of 1100 x 700 leaves only 52 units spare. | `App.xaml.cs:71` | Code, certain for 150% |
| 3 | Window size | The Mac zooms the first window to fill the screen. Windows has no equivalent. | `App.xaml.cs:73-82`, `Platforms/MacCatalyst/WindowZoom.cs:14-26` | Code |
| 4 | Dialogs | The body has a fixed max height (520 by default). Add the header, footer and 28 margin, and dialogs reach 640-900 tall, more than the 752 available, so the footer buttons get cut off. Examples: Register patient body 640 (`Views/Staff/QuickRegisterPage.cs:145`), Connection 760 (`Views/Shared/TopBar/ConnectionPanelPage.cs:113`), Developer log 900 x 620 (`Views/Shared/TopBar/DevLogPage.cs:35`). The width already adapts at line 79; the height does not. | `Controls/ClinicModal.cs:11,55,72,79` | Code; cut-off NOT VERIFIED |
| 5 | Pop-ups | The Mac asks the native view where a field is (line 29 is `#if MACCATALYST`). Windows falls back to adding up offsets, which the code comment says gives about 0 inside dialogs, so the date, time and picker pop-ups may open at the wrong place on Windows. | `Controls/ClinicPopup.cs:26-50` | Code; NOT VERIFIED on screen |
| 6 | Double clicks | The Mac blocks a second quick click on any button (800 ms). Windows has **no** such guard, so a double click can save twice. | `Platforms/MacCatalyst/NativeUiEnhancements.cs:381,428-437`; Mac only, registered at `MauiProgram.cs:94-96` | Code, certain |
| 7 | Hand cursor | The Mac shows a hand over every button and clickable row. Windows has no app-wide hand; only 6 files add their own hover colour. | `NativeUiEnhancements.cs:383-445` (Mac only) | Code |
| 8 | Esc key | Nothing closes a dialog or pop-up with Esc. There are no Escape or KeyDown handlers anywhere. The only keyboard shortcuts are the consultation menu (`Views/Doctor/ConsultationPage.Gaps.cs:133-155`). | whole app | Code, certain |
| 9 | Enter key | Login already handles Enter: Enter in the email field moves to the password, and Enter in the password signs in (`Views/LoginPage.xaml:67,101`). Beyond that, only 4 C# files react to Enter: ConsultationPage.Ui, TopBarView search, ForgotPasswordPage, and the Windows print service. Dialog forms (Register patient, Block a date, admin forms) don't submit on Enter. | grep | Code |
| 10 | Scrollbars | The sidebar hides its horizontal bar (`Controls/ClinicSidebar.cs:115`). Everywhere else uses the WinUI default (thin bar that shows on hover). | | NOT VERIFIED |
| 11 | Focus ring | The Mac turns the system focus ring off (`MauiProgram.cs:92,97`, Mac only). Windows keeps the WinUI default ring. That's probably fine, but whether it shows on cards and rows with only a tap gesture is NOT VERIFIED. | | NOT VERIFIED |
| 12 | Sidebar | Fixed 220 wide (`ClinicTheme.cs:12`, `AppShell.xaml.cs:62,153`). At 150% that's 17% of the screen. The height fix is already Windows-only (`ClinicSidebar.cs:124-128`). | | Code |
| 13 | Top bar | Search is up to 288 wide (`TopBarView.cs:70`), the sync chip up to 220 (`:100`), the name up to 420 (`:131`), and the name menu 330 (`:236`). Next to the 220 sidebar that's about 1060 units, so the bar may crowd or clip. | | NOT VERIFIED |
| 14 | Consultation | Fixed 262 left column plus padding 24 (`Views/Doctor/ConsultationPage.xaml:12`), a footer column gap of 48 (`:51`), and 230-wide history rows (`ConsultationPage.xaml.cs:2121`). | | NOT VERIFIED |
| 15 | Tables | Several tables force a minimum width inside a horizontal scroll: 672 (`Views/Staff/WalkInWizardView.cs:254`) and 760 (`Views/Doctor/Extra/DoctorAppointmentsPage.cs:318`). Fixed columns: chart 300 (`DoctorPatientChartPage.cs:56`), admin bookings 160/150 (`AdminBookingsPage.xaml.cs:170-171`), admin home 160 (`AdminHomePage.xaml.cs:95`). These should scroll rather than clip. | | NOT VERIFIED |
| 16 | Fonts | Inter is registered for MAUI text (`MauiProgram.cs:48-50`). Windows-native pieces (DisplayAlert dialogs, the native Picker drop-down) will use Segoe UI. | | NOT VERIFIED |
| 17 | Native alerts | `DisplayAlert` on Windows is a WinUI ContentDialog. A whole-window scale will **not** shrink it, because it sits outside the page. | `App.xaml.cs:89,104,122...` | Expected |

## 4. Proposed fixes (all Windows-only; no code written yet)
1. **One Windows scale, applied to the whole window** (closest match to how the Mac's 0.77 works):
   - New file `Platforms/Windows/WindowsUiScale.cs`. Folders under `Platforms/Windows` are compiled for the Windows target only.
   - Hook it in with one line inside a new `#if WINDOWS` block in `MauiProgram.cs`, next to the existing `#if MACCATALYST` at line 94.
   - On Windows it sets a ScaleTransform of `ClinicTheme.WindowsUiScale` on the window's root and lays the root out at window size divided by the scale, which acts as a layout zoom.
   - The constant goes in `ClinicTheme.cs` inside `#if WINDOWS`, so the Mac never sees it.
   - Start at **0.85**, then tune with screenshots. It may need 0.9 at 100% and 0.8 at 150%; if so, it stays one setting that reads the display scale.
   - Fallback if the transform blurs text or breaks clicks: Windows-only handler mappers that multiply FontSize, plus a Windows-only pass over the shared Styles.
2. **Window size:**
   - New `#if WINDOWS` block after `App.xaml.cs:71`, leaving line 71 untouched.
   - On first start, open maximised. This mirrors the Mac zoom.
   - Minimum 1024 x 640, which fits the 1280 x 752 usable area at 150%.
3. **Dialogs:** `#if WINDOWS` in `ClinicModal.cs`, inside the existing SizeChanged at line 79. Cap the body height to the available height so long dialogs scroll and the footer buttons stay visible.
4. **Pop-up position:** add a `#elif WINDOWS` branch in `ClinicPopup.cs:29-39` that uses the native `TransformToVisual`.
5. **Click guard and hand cursor:** new `Platforms/Windows/NativeUiEnhancements.cs`, the Windows twin of the Mac file:
   - Button mapper with the same 800 ms guard.
   - Hand cursor on buttons and tap views.
   - Registered from the same `#if WINDOWS` block in MauiProgram.
6. **Esc and Enter:**
   - Windows-only key handler on the window root.
   - Esc closes an open ClinicPopup first, otherwise the top dialog, through its own close action. That keeps the Register-patient cancel logic.
   - Enter in a dialog's single-line field presses its primary button only when exactly one is enabled. This changes behaviour, so it needs the owner's OK first.
7. **Not touched:** business logic, sync, printing, updates, permissions, the backend. No Mac file is edited except for added `#if WINDOWS` / `#elif WINDOWS` blocks.

## 5. Still to do
- The owner runs the app, signs in, and takes screenshots at 100 / 125 / 150% (before) into `docs/windows-ui/`.
- Run the tests: `dotnet test ClinicNode/ClinicNode.Tests --filter "FullyQualifiedName~Sync|FullyQualifiedName~Update"`. Not run yet.
- The Mac build can't run on Windows; the reviewer on the Mac must run it.
