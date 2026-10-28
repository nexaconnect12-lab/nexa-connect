# POS UX3 — display density and responsive layouts

Display density is independent of the [optional automatic keyboard setting](POS-Input-Mode.md).
The defaults are **Standard density** and **touch-first off**. Changing either setting
preserves the other, so all four combinations are supported.

## Settings and behavior

Sign in and open **Terminal & sync → Input settings → Display density**:

| Density | Staff controls and rows | Body text | Menu tiles |
| --- | --- | --- | --- |
| Standard (default) | 36 DIP minimum | 14 DIP | 160 DIP wide, 108 DIP minimum high |
| Touch | 52 DIP minimum | 16 DIP | 184 DIP wide, 136 DIP minimum high |

Density also adjusts field/button/card padding and tab controls. Explicitly sized controls,
such as multiline reasons, may be taller. The on-screen keyboard keeps its own minimum
48-DIP keys in either density. Density does not open a keyboard or change the saved
automatic-keyboard preference. The manual Windows keyboard action remains available.

Settings apply immediately and persist per Windows user and exact terminal scope. Changes
are allowed only while signed in, not busy, not locked and not display-safety-blocked.
If saving fails, the selection returns to the previous density and visible retry/access
guidance appears; both previous preferences remain active.

The supported minimum staff window is now 960×640 logical units. Below 1100 logical units
wide, Shift & cash stacks the shift/setup card above reconciliation instead of squeezing
both into columns. Checkout keeps menu and cart columns with wrapping density-sized menu
tiles; Payment keeps its scrollable card and original pinned confirmation button. Orders
uses density-sized rows and retains horizontal scrolling for columns that do not fit.

Payment, QR collection, Shift & cash, cash-review details and Terminal page scroll viewers
support vertical touch panning. Orders supports panning in both directions for its table.
These scrolling gestures move the view without issuing business commands.

Below 1100 logical units wide, the dock's amount column narrows from 230 to 180 logical
units. Below 720 logical units high, surrounding margins are reduced and secondary
workflow description/session guidance is hidden to prioritize
the current status, totals and original actions. Status remains scrollable; the workspace
scrolls to keep the current field visible above the keyboard. On small windows, other
workspace content may require scrolling or keyboard dismissal. No order/payment action is
submitted by reflow, resizing, density selection or keyboard dismissal.

Window dimensions and thresholds are WPF device-independent units, not physical pixels.
Windows scaling changes the available logical area. These bounds are developer checks,
not certification of unconfirmed purchased display resolutions/DPI or accessibility modes.

## Ownership and compatibility

`PosDensityMetrics` and `PosResponsiveLayout` are presentation policies.
`MainWindow.Density.cs` applies window-local dynamic resources and rearranges existing
staff controls without replacing their commands. Customer display content, native financial
confirmation dialogs, original-cashier authentication, tax/pricing, settlement/custody,
idempotency, outbox and financial recovery remain unchanged. No service/API/role or
financial database/schema change is introduced.

The existing non-secret version-1 `input.json` gains optional `Density` with the strings
`Standard` or `Touch`. Legacy files without this field retain their `TouchFirst` choice
and use Standard. An unknown density string uses Standard while retaining a valid
keyboard preference. Unreadable/malformed/unsupported-version files still use both defaults.
The UI saves both current preferences together through bounded reads, linked-path rejection
and atomic replacement outside immutable releases and financial storage. No startup migration
write is needed, and installation must preserve existing preferences.

## Verification and rollout

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PosDensity|FullyQualifiedName~PosUiPreferences'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests/NexaConnect.PosTouchTests.csproj
```

Unit tests cover both independent choices, legacy compatibility, invalid values, persistence,
defaults and layout thresholds. The Windows fixture uses isolated temporary preferences and
financial storage, synthetic identity and mocked HTTP reads; it issues no live API requests
or financial commands. It checks all four density/keyboard combinations at 960×640,
1024×768, 1280×800 and 1536×864, including current field visibility, pinned actions, stacked
cash cards, Orders row density, exact large server totals and failed-save rollback. Existing
input, workflow, busy and lock guards remain covered. `-- --preview` creates synthetic
Standard/Touch layouts under `.runstate` for visual review.

The initial source run passed all 1,139 Windows-enabled unit tests without skips; the
extended Windows fixture passed and both small-window synthetic previews were inspected.
Source-bound candidate `712da93c-ad7c-4eee-830e-dd251e728c9c` subsequently passed
all 19 QR6 checks: 1,139 unit tests, six architecture checks, 31 HTTP cases, 47 hosting
checks, candidate/acceptance/Media-profile guards, frontend checks/builds/audits and
88 frontend tests. Browser suites passed 57 QR, eight day-close, eight cutoff, 22 seal
and seven settlement cases. Unit/browser runs have no failures or skips; browser runs
have no flaky cases. Seven owning cases passed with verified cleanup and broker recovery.
The protected `verification.json` records `productionVerified=false`. Earlier cancelled
gate attempts are not claimed as successful candidates.

Matching package `3347750f987a42f6a462ec6bc6c37658` is installed, with its separate
retained-pilot activation recorded below. Actual operator/device acceptance remains pending.

Use the existing [same-scope pilot refresh](QR7-Pilot.md#refreshing-source-for-the-same-qr-pilot-scope)
and recovery-preserving Windows install process for local activation. Do not reuse old
phone observations as new acceptance or reset the retained branch data.

### Local activation — 28 October 2026

After the user confirmed POS was closed, package `3347750f987a42f6a462ec6bc6c37658`
was installed into the existing current-user DraftTerminal. Retained stack
`c30b628ab23141f389119daa565f894f` rebuilt without reset and reached Ready with all
12 services. Strict source-candidate revalidation passed after publication. Fresh pilot
`11399e2f623d484190b30575c68cc80a` configured and started at the unchanged
`https://192.168.0.191:8443` origin with unchanged valid certificate files; live HTTPS
diagnostics passed. New phone acceptance observations remain pending.

All six recorded recovery/preference file fingerprints matched before launch; terminal
scope, references and service URLs were unchanged. A read-only preference probe using
the installed public configuration returned **Standard** density and automatic keyboard
**off**, preserving the existing choice without writing preferences. A read-only gallery
check returned HTTP 200 with two staff slides, two customer slides and three validated PNGs.

Native inspection confirmed the exact installed executable reopened in its retained locked
state, hiding operational content. No interactive POS sign-in, sale, payment or gallery
change was submitted. Live signed-in density switching and physical acceptance remain
pending below; installation/read-only probes do not establish those observations.

Pending physical/operator checks include actual resolutions and Windows scaling for both
purchased displays, keyboard/mouse operation in Standard, touch operation in Touch, all
four saved combinations after restart, popup/dropdown/focus behavior, scanner entry, original
payment confirmations, lock/resume, customer privacy and display disconnect safety. Density
does not implement card-reader authentication. Continuous locked refresh is a separate
subsequent [UX4 implementation](POS-UX4-Locked-Gallery.md), whose activation/acceptance
is not established by the density receipts.
