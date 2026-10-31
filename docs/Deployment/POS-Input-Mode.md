# Optional POS touch-first mode

The [terminal-sections follow-up](POS-UX10-Windows11-Cashier.md#terminal-sections-follow-up) moves
this setting to **Terminal & sync → Input & devices → Input settings** after matching installation.
Earlier releases show it directly on the Terminal & sync page; preferences/defaults do not change.

Touch-first operation is optional and **off by default**, including existing terminals
that have no saved preference. Keyboard, mouse and supported scanner input remain usable
in either mode. The UX2 workflow dock, original financial actions and readable controls
remain available in both modes; the switch does not select a different business workflow
or a compact layout. The independent [UX3 display-density setting](POS-UX3-Display-Density.md)
now provides Standard (default) and Touch layouts without changing this keyboard preference.

## Change the setting

Sign in, open **Terminal & sync → Input settings**, and select **Enable touch-first POS**.
The choice applies immediately and is saved for the current Windows user and exact
organization/restaurant/branch/store/terminal scope. Any signed-in operator can change this
non-security preference when POS is not busy or display-safety-blocked.

- **Off (default):** field focus does not open the application keyboard, tapping a
  PasswordBox does not launch the Windows keyboard, and Sign in / Resume does not launch
  it automatically. Use the physical keyboard, mouse or supported scanner.
- **On:** editable text fields open the UX1 text/numeric keyboard. PasswordBox entry and
  external-browser Sign in / Resume can open the Windows keyboard as before.
- **Windows keyboard** remains an explicit manual action in both modes, including the
  lock panel. POS does not suppress keyboard behavior that Windows itself manages.

Turning the setting off hides the application keyboard and clears its field target.
It does not close an already opened Windows OS keyboard; Windows owns that window.
If saving fails, the checkbox returns to the previous saved mode and the settings section
shows retry/access guidance. Missing, linked, unreadable, oversized, malformed or unsupported-version
preferences default to off without blocking sign-in or financial recovery.

## Storage and boundaries

One non-secret `input.json` is stored beneath
`%LOCALAPPDATA%\NexaConnect\Preferences\<scope-hash>`. The key is lowercase SHA256 over
UTF-8 `organizationN|restaurantN|branchN|storeN|terminalN` UUIDs. File version 1 contains
`Version`, `TouchFirst` and optional `Density` (`Standard` or `Touch`); accepted payloads are capped at 1024 bytes, reads use a
fixed bounded buffer and writes use an atomic temporary-file replacement. Linked/junction
paths default off on load and are rejected on save. The preference survives
normal release installation because it is outside immutable release files.

This is presentation data, separate from POS SQLite, outbox, DPAPI tokens and financial
recovery. It changes no authorization, session timeout, customer display, API, role,
financial command, schema or payment calculation. No preference or input text is logged.
Settings cannot be changed while locked, signed out, busy or display-safety-blocked.

## Verification and rollout

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PosUiPreferences|FullyQualifiedName~TouchInput'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests/NexaConnect.PosTouchTests.csproj
```

Unit tests cover default-off/no startup write, save/reload/disable, invalid or oversized
data, exact-scope/Windows-profile separation, Windows file-lock save failure and an actual
linked-directory rejection without changing its target. The
isolated Windows fixture injects a temporary preference directory and checks default-off
keyboard suppression, opt-in persistence, immediate disabling, failed-save rollback and
busy rejection alongside the existing keyboard/workflow/lock layouts. No live settings,
cashier tokens or financial records are used by those fixtures.

Source-bound candidate `d8fc2a1a-e9c3-488f-89c3-02533b3a6be3` passed all 19 QR6
checks, including 1,127 unit tests, six architecture checks, 31 HTTP cases, 47 hosting
checks, the candidate/acceptance/Media-profile guards, frontend checks/builds/audits and
88 frontend tests. Browser suites passed 57 QR, eight day-close, eight cutoff, 22 seal and
seven settlement cases. Unit/browser runs have no failures or skips; browser runs have no
flaky cases. Seven owning cases passed with verified cleanup and broker recovery.
Its protected `verification.json` records `productionVerified=false`; the extended Windows
fixture also passed separately.

### Local activation — 28 October 2026

After the user confirmed POS was closed, package `dd3ec7cde30f415284ae9e37d5b02c10`
was installed into the existing current-user DraftTerminal. The retained stack
`c30b628ab23141f389119daa565f894f` was rebuilt without reset and reached Ready with
all 12 services. Strict candidate revalidation passed after publication. Fresh pilot
`4aa86fb443524ff49d928a2f4fcc7146` configured/started with the unchanged
`https://192.168.0.191:8443` origin and valid certificate identity; live HTTPS diagnostics
passed. Its phone acceptance observations start pending rather than inheriting prior results.

All five recorded recovery/token file fingerprints matched before launch; terminal
scope, references and service URLs were unchanged. A read-only effective-preference probe
using the installed public configuration returned `effectiveTouchFirst=false` without
writing preferences or accessing cashier credentials. A read-only gallery check returned
HTTP 200 with two staff slides, two customer slides and three checksum-valid PNGs.

The exact installed executable reopened in its retained locked state, confirmed by native
inspection. No interactive POS sign-in, sale, payment or gallery change was submitted. Live signed-in
setting changes, both input modes, restart persistence and real hardware/OIDC acceptance
remain pending operator checks.

For subsequent updates, install a fresh matching package through the existing local candidate and
[same-scope pilot refresh](QR7-Pilot.md#refreshing-source-for-the-same-qr-pilot-scope).
Source changes do not update an already running installation. Actual operator tests of
both modes, restart persistence, external keyboard launch, physical touch/scanner and
customer-display privacy remain pending until observed.
