<!-- JJQR - AI (Nov 21, 2026) -->
# Clinic Node — shared style guide

**Use this for every new staff, admin, and doctor screen.** The owner's bar is a near 1:1 look with `Project02-fe`. Mirror the corresponding TSX: the same layout, the same visible text, Tailwind spacing converted to px (below), the same type sizes and weights, the exact colour tokens, the same radii and shadows, and the **same icons** (web `Icon name="…"` names).

Tokens come from `Project02-fe/src/app/globals.css` `@theme` (clinical_precision). Do not invent a second palette, type scale, or icon set.

If a key is missing, ask the shared-app bot (or the parent) to add it here and in `Colors.xaml` / `Styles.xaml`. Do not hard-code hex in a page.

## Theme: Light only

The web app has **no dark-theme toggle**. The desktop app is always Light:

- `Application.UserAppTheme = AppTheme.Light` (ignores macOS / Windows dark mode)
- Mac Catalyst `Info.plist`: `UIUserInterfaceStyle = Light`
- Windows: `RequestedTheme="Light"`

Content stays on `#F8F9FF`. The staff/admin **sidebar** uses inverse-surface `#213145` as a brand accent (same as the web Sidebar). That is not system dark mode. The doctor sidebar is light (`SurfaceContainerLowest`).

## Files

| What | Where |
|---|---|
| Colours | `ClinicNode.App/Resources/Styles/Colors.xaml` |
| Control + typography styles | `ClinicNode.App/Resources/Styles/Styles.xaml` |
| C# helpers (pills, lane badges, role subtitles) | `ClinicNode.App/Controls/ClinicTheme.cs` |
| Icons (`ClinicIcon`, `ClinicIcons`) | `ClinicNode.App/Controls/ClinicIcons.cs` |
| 260px role sidebar | `ClinicNode.App/Controls/ClinicSidebar.cs` |
| Fonts | `Resources/Fonts/Inter-*.ttf`, `fa-solid-900.ttf`, `fa-regular-400.ttf` |
| Registration | `MauiProgram.ConfigureFonts` + `<MauiFont>` in `ClinicNode.App.csproj` |

XAML: `{StaticResource Primary}`, `Style="{StaticResource SecondaryButton}"`, `xmlns:ctrl="clr-namespace:ClinicNode.App.Controls"`. C#: `ClinicTheme.Color("Primary")`, `ClinicTheme.Text("Hello", "HeadlineMd")`, `new ClinicIcon { Name = "payments" }`.

## Tailwind → px (use these, do not guess)

| Class / token | Pixels |
|---|---|
| `p-xs` / `gap-xs` / `space-y-xs` / `--spacing-xs` | **4** |
| `p-sm` / `gap-sm` / `--spacing-sm` | **8** |
| `p-md` / `gap-md` / `--spacing-md` | **16** |
| `p-lg` / `gap-lg` / `--spacing-lg` | **24** |
| `p-xl` / `--spacing-xl` | **32** |
| `--spacing-gutter` | **20** |
| `min-h-11` / `h-11` / `w-11` (touch target) | **44** |
| `h-4 w-4` (sidebar and button icons) | **16** |
| `h-5 w-5` (login checkbox) | **20** |
| `h-10 w-10` (StatCard icon well) | **40** |
| `text-[14px]` (status-pill icon) | **14** |
| `w-[3px]` (sidebar active bar) | **3** |
| `--spacing-sidebar` | **260** |
| `--spacing-sidebar-collapsed` | **72** |
| `--container-auth` (26rem) | **416** |
| `--container-auth-wide` (34rem) | **544** |
| `--container-content` | **1440** |
| `--container-page-heading` / doctor heading (48rem) | **768** |
| `--container-doctor-copy` (36rem) | **576** |

Page padding is **24** (`p-lg`). Card padding is **24** (`p-lg` on `.bento-card`). Auth card padding is **32** (`p-xl` on `sm`).

## Radius

| Class / token | Pixels | Use |
|---|---|---|
| `--radius-sm` / `rounded-sm` | **4** | tight chips |
| `--radius` / `--radius-nav` / `rounded-nav` | **8** | sidebar items |
| `--radius-md` / `rounded-md` | **12** | |
| `--radius-lg` / `rounded-lg` | **16** | buttons, inputs, queue rows, StatCard icon well |
| `--radius-xl` / `rounded-xl` | **24** | cards (`Card`, login card) |
| `rounded-full` | pill (20+ corner) | status pills, lane badges |

## Shadows

Web primary / danger buttons use `shadow-sm`. Interactive bento cards use `hover:shadow-md` and lift 2px (`translateY(-2px)`).

| Token | Approximate MAUI Shadow |
|---|---|
| `shadow-sm` | Offset `(0, 1)`, Radius `2`, Brush `#0D000000` (black 5%) |
| `shadow-md` | Offset `(0, 4)`, Radius `6`, Brush `#1A000000` (black 10%) |

Static cards are border-only (no resting shadow), like the web `bento-card`.

## Type styles (`Styles.xaml`)

Font aliases: `Inter` (400), `InterMedium` (500), `InterSemiBold` (600). Bundle Inter even though Mac Safari may render the live web in San Francisco — the documented token is Inter, and Windows must match.

| Key | Size | Weight | Color | Web |
|---|---|---|---|---|
| `HeadlineLg` | **30** | InterSemiBold | OnSurface | `text-headline-lg` (doctor page titles) |
| `HeadlineLgMobile` | **24** | InterSemiBold | OnSurface | `text-headline-lg-mobile` (login h1) |
| `HeadlineMd` | **20** | InterSemiBold | OnSurface | `text-headline-md` |
| `HeadlineSm` | **16** | InterSemiBold | OnSurface | `text-headline-sm` |
| `BodyLg` | **16** | Inter | OnSurface | `text-body-lg` (queue patient line is this + semibold) |
| `BodyMd` | **14** | Inter | OnSurface | `text-body-md` |
| `BodySm` | **13** | Inter | OnSurface | `text-body-sm` |
| `MutedBody` | **14** | Inter | OnSurfaceVariant | supporting copy / `DoctorPageHeader` description |
| `MutedSm` | **13** | Inter | OnSurfaceVariant | hints |
| `FieldLabel` | **13** | InterMedium | OnSurface | `labelStyles` (`text-label-md font-medium`) |
| `LabelLg` | **16** | InterSemiBold | OnSurface | `text-label-lg font-semibold` (section titles in a card, e.g. "Your patient queue") |
| `LabelSm` | **11** | InterSemiBold | OnSurfaceVariant | `text-label-sm` |
| `BrandLink` | **16** | InterSemiBold | Primary | AuthShell clinic name |
| `PageKicker` | **11** | InterSemiBold | Primary | use only when the web page has an eyebrow; doctor Today does **not** |
| `ErrorText` | **13** | Inter | Error | `FormAlert` |

Letter spacing: `text-label-sm` is `0.03em`; `text-headline-lg` is `-0.02em` (`tracking-tight`). Prefer the named style over ad-hoc `FontSize`.

## Colour tokens (`Colors.xaml`)

### Surfaces and ink

| Key | Hex | Use |
|---|---|---|
| `Background` / `Surface` / `SurfaceBright` | `#F8F9FF` | page (`bg-background`) |
| `OnBackground` / `OnSurface` | `#0B1C30` | primary text |
| `OnSurfaceVariant` | `#3D4947` | secondary text |
| `SurfaceContainerLowest` | `#FFFFFF` | cards, inputs, doctor sidebar, top bar |
| `SurfaceContainerLow` | `#EFF4FF` | login wash, row hover |
| `SurfaceContainer` | `#E5EEFF` | |
| `SurfaceContainerHigh` | `#DCE9FF` | secondary button hover |
| `SurfaceContainerHighest` / `SurfaceVariant` | `#D3E4FE` | |
| `SurfaceDim` | `#CBDBF5` | |
| `InverseSurface` | `#213145` | staff/admin sidebar only |
| `InverseOnSurface` | `#EAF1FF` | hover text on inverse |
| `Outline` | `#6D7A77` | stronger rules |
| `OutlineVariant` | `#BCC9C6` | card/input/secondary borders |
| `SurfaceTint` | `#006A61` | |

### Brand

| Key | Hex | Use |
|---|---|---|
| `Primary` | `#00685F` | primary buttons, doctor brand, links |
| `OnPrimary` | `#FFFFFF` | text on primary |
| `PrimaryContainer` | `#008378` | primary hover |
| `OnPrimaryContainer` | `#F4FFFC` | |
| `InversePrimary` | `#6BD8CB` | |
| `PrimaryFixed` | `#89F5E7` | clinic name on the dark sidebar |
| `PrimaryFixedDim` | `#6BD8CB` | |
| `OnPrimaryFixed` | `#00201D` | |
| `OnPrimaryFixedVariant` | `#005049` | |
| `PrimarySubtle` | `#1A00685F` | doctor active-nav fill and StatCard icon well (`bg-primary/10`) |
| `Secondary` | `#565E74` | staff sidebar 3px accent |
| `OnSecondary` | `#FFFFFF` | |
| `SecondaryContainer` / `SecondaryFixed` | `#DAE2FD` | staff active-nav text |
| `OnSecondaryContainer` | `#5C647A` | |
| `OnSecondaryFixed` | `#131B2E` | |
| `OnSecondaryFixedVariant` | `#3F465C` | staff active-nav fill |
| `Tertiary` | `#924628` | admin sidebar 3px accent |
| `OnTertiary` | `#FFFFFF` | |
| `TertiaryContainer` | `#B05E3D` | |
| `TertiaryFixed` | `#FFDBCE` | S- lane badge fill |
| `OnTertiaryFixed` | `#370E00` | S- lane badge text |
| `Error` | `#BA1A1A` | danger buttons, errors |
| `OnError` | `#FFFFFF` | |
| `ErrorContainer` | `#FFDAD6` | |
| `OnErrorContainer` | `#93000A` | |

### Status pills (do not use teal for “done”)

| Tone | Text | Surface | Border | Examples |
|---|---|---|---|---|
| Success | `StatusSuccess` `#166534` | `StatusSuccessSurface` `#DCFCE7` | `StatusSuccessBorder` `#BBF7D0` | Completed, Paid |
| Warning | `StatusWarning` `#854D0E` | `StatusWarningSurface` `#FEF3C7` | `StatusWarningBorder` `#FDE68A` | Pending, In progress, On hold |
| Danger | `StatusDanger` `#991B1B` | `StatusDangerSurface` `#FEE2E2` | `StatusDangerBorder` `#FECACA` | Cancelled, No-show, Unpaid |
| Info | `StatusInfo` `#1E40AF` | `StatusInfoSurface` `#DBEAFE` | `StatusInfoBorder` `#BFDBFE` | Confirmed, Checked in, Waived |
| Neutral | `StatusNeutral` `#475569` | `StatusNeutralSurface` `#F1F5F9` | `StatusNeutralBorder` `#CBD5E1` | unknown / inactive |

Completed is the only booking state that is success green. Confirmed / Checked in stay info (blue).

`ClinicTheme.MakeStatusPill("CheckedIn")` applies the map and the human label. Pair colour with an icon (`check_circle`, `schedule`, `cancel`, `info`, `radio_button_unchecked`) — the web StatusPill always does.

Aliases for older pages (prefer the names above on new work): `ClinicTeal` = Primary, `ClinicTealDark` = OnPrimaryFixedVariant, `ClinicSurface` = Background, `ClinicOnSurface` = OnSurface, `ClinicMuted` = OnSurfaceVariant, `ClinicWarning` = StatusWarning, `White` = `#FFFFFF`.

## Icons (same names as the web)

The web `Icon` component (`src/components/ui/Icon.tsx`) maps Material Symbols names (e.g. `dashboard`, `payments`) to **Font Awesome 7 Free** SVGs. The desktop app bundles the same FA 7 fonts (`fa-solid-900.ttf`, `fa-regular-400.ttf`; SIL OFL, license in `Resources/Fonts/FONT-AWESOME-LICENSE.txt`) and the same name map.

```xml
xmlns:ctrl="clr-namespace:ClinicNode.App.Controls"

<ctrl:ClinicIcon Name="payments" IconSize="16" IconColor="{StaticResource Primary}" />
```

```csharp
new ClinicIcon { Name = "print", IconSize = 16, IconColor = ClinicTheme.Color("OnSurface") };
// or as a button leading icon:
button.ImageSource = ClinicIcons.FontImage("print", ClinicIcons.SizeSm, ClinicTheme.Color("OnSurface"));
button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8);
```

Default size is **16** (`h-4 w-4`). Status pills use **14**. StatCard icon wells are a 40×40 (`h-10 w-10`) rounded-lg (`16`) square with `PrimarySubtle` fill and a `Primary` 16px icon.

Use the web name, not a FA class. Names currently in the map (plus `eye` / `eye_slash` for the login password toggle):

`menu`, `notifications`, `search`, `logout`, `chevron_left`, `chevron_right`, `expand_less`, `expand_more`, `calendar_today`, `calendar_month`, `event_available`, `event_note`, `event`, `event_busy`, `close`, `delete`, `lock`, `call`, `mail`, `check`, `check_circle`, `help`, `download`, `science`, `description`, `person`, `account_circle`, `star`, `add_circle`, `add`, `arrow_forward`, `gavel`, `folder_shared`, `folder_open`, `prescriptions`, `medication`, `schedule`, `directions_walk`, `warning`, `incomplete_circle`, `radio_button_unchecked` (regular), `analytics`, `monitoring`, `cancel`, `error`, `info`, `dashboard`, `medical_services`, `assignment`, `vaccines`, `payments`, `group`, `groups`, `campaign`, `manage_accounts`, `clinical_notes`, `badge`, `settings`, `history`, `pending_actions`, `hourglass_top`, `receipt_long`, `follow_the_signs`, `thermometer`, `heart_pulse`, `lungs`, `gauge`, `wind`, `ruler`, `weight_scale`, `view_list`, `table_rows`, `print`, `copy`, `edit`, `upload_file`, `eye`, `eye_slash`.

If you need a new name that already exists in `Icon.tsx` on the web, ask the shared-app bot to add it to `ClinicIcons`. Do not swap in a different icon family.

### Nav icons (copy these onto flyout items)

Staff: Dashboard `dashboard`, Payments `payments`, Walk-In `directions_walk`, Queue `view_list`, Patients `group`, Doctor Status `medical_services`, Announcements `campaign`, Profile `manage_accounts`.

Doctor: Today `dashboard`, Appointments `event_note`, Patient charts `groups`, Performance `monitoring`, Availability `schedule`, Templates `description`, Profile `account_circle`.

Admin: Dashboard `dashboard`, Bookings `calendar_today`, Walk-In `directions_walk`, Doctors `medical_services`, Patients `group`, Staff `badge`, Announcements `campaign`, Settings `settings`, Audit Logs `history`, Reports `monitoring`. Groups: Operations, Management, System.

Logout is always `logout`.

## Control styles

Buttons (web `buttonStyles`: `min-h-11`, `rounded-lg` = 16, `px-lg` = 24, `py-md` = 16 visually tightened to a 44px height, `text-label-md` / medium, `gap-sm` between icon and label):

| Key | Look |
|---|---|
| `PrimaryButton` (implicit `Button`) | Primary fill, OnPrimary text, `shadow-sm` |
| `SecondaryButton` | White fill, OnSurface text, OutlineVariant 1px border |
| `GhostButton` | Transparent, OnSurfaceVariant text |
| `DangerButton` | Error fill, OnError text, `shadow-sm` |
| `StepButton` / `StepButtonActive` | consultation stepper |

Inputs (web `inputStyles`): implicit `Entry`, `Editor`, `Picker`, `DatePicker` — 44px, white fill, OutlineVariant border, 16px radius, 14px Inter body. Field captions use `FieldLabel` (never a faint grey). CheckBox colour is Primary; login checkbox is 20×20 in a 44px row.

Cards:

| Key | Look |
|---|---|
| `Card` | White, OutlineVariant 1px, radius **24**, padding **24** (match `.bento-card`) |
| `AuthCard` | Same, padding **32** |
| `ListCard` | Radius **16**, padding 16×12, bottom margin 8 (inner queue rows use this) |

Pills: `Pill` + `PillSuccess` / `Warning` / `Danger` / `Info` / `Neutral`, and matching `PillLabel*` (uppercase). Lane badges: `LaneBadgeS` + `LaneLabelS` (S-), `LaneBadgeR` + `LaneLabelR` (R-). `ClinicTheme.MakeLaneBadge("S-001")`.

## Screen recipes

**Login** (`AuthShell`): page background `SurfaceContainerLow`. Max width **416**. Brand link “Dr. Grace Gavino Medical Clinic” (`BrandLink`). Card `AuthCard`. Title “Log in to your account” (`HeadlineLgMobile`). Description “Patients, doctors, staff and administrators all sign in here.” (`MutedBody`). Fields stacked with 16px gaps; labels `FieldLabel`. Password row has a 44px show/hide control (`eye` / `eye_slash`) and a “Forgot password?” primary text link. Desktop node: “Remember my email on this computer” plus “For safety the password is never saved. You stay signed in on this computer for up to 7 days of use.” Primary button full width, label **“Log in”** (not “Sign in”). Do not add Create-an-account (no patient portal on the node).

**Doctor Today**: `DoctorPageHeader` — title “Your clinic today” (`HeadlineLg`), description “{name} · {dd MMM yyyy in en-PH, e.g. Nov 21, 2026}” (`MutedBody`). Actions: secondary “Your performance” (`monitoring` icon) and primary “Open appointments” (`arrow_forward`). Then a `Card` titled “Your patient queue” (`LabelLg`) with waiting / in progress / completed counts. Queue rows: radius 16, border OutlineVariant, padding 16; name `BodyLg` semibold; action “Start consultation” or “Resume consultation” (`SecondaryButton`). Empty: “No one's checked in yet today.” or “Queue's clear — N seen today.”

**Consultation**: header uses the patient name as `HeadlineMd`; print actions are `SecondaryButton` with the `print` icon: “Print Prescription”, “Print Lab Orders”, plus certificate / queue slip on the node. Call `IPrinter` + `IPrintDocumentFactory` only — do not edit `MauiPrinter`.

## Navigation and DI

`AppShell` owns flyouts and routes. After login, `FlyoutBehavior.Locked` and `ClinicSidebar` (260px) replace the default flyout. Do not add a second nav chrome.

Relay new routes or DI registrations through the parent. `MauiProgram` registers pages as transients and `IPrinter` as a singleton. Core services live in `AddClinicNodeCore`. Printing implementation is owned by the print/packaging bot.

## What not to do

- Do not set `UserAppTheme = Dark` or follow the system theme.
- Do not hard-code leftover hex (`#00685F`, `#F6F8F8`, …); use the resource keys.
- Do not restyle implicit Button/Entry for a one-off; add a keyed style in `Styles.xaml` if needed.
- Do not put product UI in a WebView.
- Do not use a different icon font or emoji in place of `ClinicIcon`.
<!-- JJQR - END -->
