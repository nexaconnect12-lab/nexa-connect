# POS UX1 — touch-first input foundation

Current behavior: [touch-first is an optional saved setting](POS-Input-Mode.md), **off by
default**. Automatic keyboard behavior described below applies when enabled. Manual
Windows keyboard access remains available in both modes. This supersedes UX1's original
unconditional automatic keyboard behavior without changing its historical verification.

Implemented in source on 28 October 2026. This slice follows the
[long-term product principles](../Planning/Next-Iteration-Roadmap.md#long-term-pos-product-design-principles).
It does not establish physical S200D acceptance or update an installed release automatically.

## Cashier behavior

Tap an enabled text field while signed in to open the staff-only docked keyboard.
Focusing a text field with a physical keyboard also opens it. Tap the field again after
**Done · Hide keyboard** to reopen it. Letter keys, Shift, Symbols, Space, Backspace and
Clear edit the existing field and its selection. Done only hides the keyboard; it never
submits a financial command. The first text layout is English; use **Windows keyboard**
for other layouts/languages and external credential entry.

Opening cash, counted closing cash, cash movement amount and refund amount use a numeric
layout. Its decimal key inserts the current Windows culture's decimal separator, matching
the existing amount parsers. Drafts permit empty/incomplete values while editing and at
most two fractional digits; existing command validation still rejects invalid amounts.
Field maximum lengths apply; fields without a limit have a 4096-character soft-key cap.
Backspace removes a selected range or the preceding Unicode text element.

The keyboard reserves space below a scrollable workspace rather than overlaying it.
The focused field scrolls into view; other controls and totals remain reachable by
scrolling or hiding the keyboard. At smaller sizes, the header or other sections may
scroll out of view. Keys are at least 48 device-independent pixels high and follow the
existing rounded, Segoe UI/green POS theme. This is an incremental Windows 11-inspired
foundation, not a completed application-wide visual redesign.

Existing generated shift numbers, menu/station selectors, tender choices, refund reasons
and original-Order selection continue to reduce typing. Two cancellation-reason shortcuts
fill **Customer request** or **Item unavailable**; the cashier must still explicitly
cancel the unpaid order through its original confirmation/validation path.

## Security, authentication and peripherals

The application keyboard targets only editable, visible, enabled staff TextBoxes.
It hides and clears its target on deactivation, tab changes, busy transitions, lock and
sign-out. Editing rechecks the active window, session, display-safety and field guards.
It never receives passwords or appears in the customer window. PasswordBox entry uses
the Windows keyboard instead. No draft text is logged, persisted by the keyboard,
copied to the clipboard or published to another service/display.

Sign in / Resume opens the Windows `osk.exe` from the system directory before opening
the existing external-browser PKCE flow. **Windows keyboard** is also available in the
staff header and revealed lock panel. Windows owns its visibility, positioning, language
and dismissal; POS does not read browser fields, send synthetic keystrokes or collect
credentials. If launch fails, POS provides Windows Settings → Accessibility → Keyboard
guidance in the workspace status and, when locked, the authentication panel's status;
authentication remains subject to the original guards. Confirm that the OS
keyboard appears on the private staff monitor before entering credentials on real hardware.

Native physical-keyboard/scanner-style TextBox insertion is unchanged. This adds no
barcode-to-product lookup, scanner driver, payment-card processing or cashier badge
authentication. Actual scanner terminators, reader support and failure/reconnect behavior
need peripheral-specific acceptance. No service/API, identity client/role, database,
financial invariant, transaction boundary, idempotency key or recovery format changes.

## Verification and activation

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~TouchInputEditTests|FullyQualifiedName~PosSession|FullyQualifiedName~LockPrompt|FullyQualifiedName~CashierPresentation'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests/NexaConnect.PosTouchTests.csproj
```

The Windows fixture uses synthetic identity and isolated temporary storage, never live
tokens, orders or POS recovery. It checks keyboard actions, minimum key heights/focus
policy, numeric mode, field visibility/non-overlap at 1000×680, 1280×850 and 1536×864,
native text insertion and busy/disabled/locked guards. These are logical window sizes,
not certification of unknown physical resolutions or DPI. `-- --preview` renders a
synthetic layout to `.runstate/pos-touch-preview.png` for visual review. Portable unit
tests cover selection replacement, length limits, decimal drafts/cultures and Unicode
backspace. Existing cashier/session tests protect retained-work behavior.

The 28 October source verification records 61 focused tests passed and three skipped;
the default complete unit run records 1,082 passed and 15 skipped (1,097 total), with no
failures. Skipped environment-dependent cases do not establish their acceptance. The final
Windows-enabled candidate `029650e1-8a22-45bf-8bfb-0a11a43c1eb9` subsequently passed
all 19 QR6 gate checks: 1,097 unit tests, six architecture checks, 31 HTTP cases, 47 hosting
checks, 22 candidate guards, 39 acceptance guards, the Media-profile guard runner, builds,
frontend checks/audits and 88 frontend tests. Its browser suites passed 57 QR, eight day-close,
eight cutoff, 22 seal and seven settlement cases. The unit stage has zero skips, and the QR
browser stage has no failures, skips or flaky cases. Seven owning cases passed with verified
cleanup and broker recovery. The protected `verification.json` and `owning.json` under that
candidate directory record this source-bound evidence with `productionVerified=false`.
The final
Windows fixture also checks visible lock-panel guidance through a simulated keyboard-launch
failure; it does not launch or certify the real Windows keyboard. The synthetic preview
shows the amount field above the docked keypad with Done visible. Local activation is
recorded below; signed-in input workflows and the actual-device checks remain separate.

### Local activation — 28 October 2026

With the user-confirmed POS closure, release `babb26711a6443929793fd46faeae42c` was
installed into the existing current-user DraftTerminal. The retained stack run
`c30b628ab23141f389119daa565f894f` was rebuilt without reset and reached Ready. Strict
candidate revalidation still passed after rebuild. Fresh pilot
`e62e513e7d89451393dd19de94d3fe3f` configured/started with the unchanged
`https://192.168.0.191:8443` origin and certificate identity; live HTTPS diagnostics passed.
Its phone observation cases start pending rather than inheriting earlier results.

All five recorded recovery files matched their pre-update hashes before launch;
provisioned references, terminal scope and service URLs were unchanged. A read-only
cashier gallery check returned HTTP 200 with two staff slides, two customer slides and
three checksum-valid PNGs. No orders, payments or gallery selections were submitted.

The installed executable reopened in its retained locked state. Native inspection
confirmed the first Return revealed the centered Resume / Authenticate panel and the
new Windows keyboard button, without starting authentication. Neutral branding is
expected until the original cashier signs in and refreshes an expired gallery lease.
The agent did not authenticate or certify signed-in touch entry, real OS-keyboard launch,
OIDC or physical scanner behavior. Those remain operator acceptance checks below.

For subsequent updates, package and install a fresh matching candidate through the existing
local release workflow; an older package without UX1 has no keyboard UI.
Close POS before replacing its selected release and preserve recovery. No backend schema
or configuration change is needed for touch input. A source-bound retained QR pilot still
uses the [same-scope refresh procedure](QR7-Pilot.md#refreshing-source-for-the-same-qr-pilot-scope),
including its stack rebuild/restart and fresh activation binding. Keep the existing phone
certificates only while their unchanged origin, identity and lifetime remain valid.

Pending actual-device checks:

- Confirm both display resolutions/DPI and touch support; use Windows Extend with explicit
  private staff/customer assignments. Test text/numeric layouts and scaling on the S200D.
- Perform menu selection, cash opening/counting, checkout, cancellation and receipt lookup
  using touch only; verify scrolling exposes needed totals/actions and Done submits nothing.
- Test browser sign-in, wrong-cashier resume, cancelled authentication and Windows keyboard
  launch/failure/placement without a physical keyboard. Required Windows components and
  local access policy must allow the OS keyboard.
- Scan into the intended field and test terminators/repeated scans, unplug/reconnect,
  field switching, touch caret/selection, hardware typing and lock while editing.
- Verify customer privacy, display disconnect safety, original recovery and paid-state
  preservation. No actual peripheral/OIDC/device acceptance is claimed by fixture tests.

Card-reader sign-in, additional application keyboard languages and full UI redesign remain
separate follow-ups. [UX4](POS-UX4-Locked-Gallery.md) subsequently implements continuous
locked gallery refresh in source; its deployment and human/device acceptance are separate.
