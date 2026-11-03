# Cashier ready panel, session PIN and customer advertising

This native WPF slice adds a staff-ready card and equal-width customer advertising/order panels. It preserves existing checkout, original-payment recovery, service authorization, display safety and approved-gallery ownership. The verified matching package is activated in the retained local pilot as recorded below; actual operator/PIN/hardware acceptance remains separate.

## Cashier workflow

On **Shift & cash** or **Checkout** only, when not busy or processing a cart/pending transaction, the ready card shows the authenticated cashier label, shortened register identifier, shift/cash-session status and **Sign in / Open shift / Start sale** as appropriate. The label uses validated UserInfo `preferred_username`, with a shortened subject fallback. It stays on the staff screen. Start sale navigates to the existing sign-in, shift or checkout controls; it creates no sale automatically. Busy, locked, unsafe-display and working states retain their normal protected operational flow. QR/history/review tabs omit this card to keep their actions reachable on smaller staff screens.

The staff full-sign-in action always requests provider `prompt=login`, including initial sign-in and after local sign-out; it does not silently reuse browser SSO as a cashier switch. For quick resume, complete full sign-in, then choose **Set session unlock PIN** within two minutes. Enter and confirm six digits using the masked touch keypad or keyboard. Manual/idle lock hides work; wake the prompt and choose **Unlock with PIN** when offered. In the installed lock-choice refinement, **Use full sign-in instead** is a smaller underlined, keyboard-accessible fallback rather than a competing primary action. If PIN is unavailable, **Sign in to resume** becomes the primary action with an explanation; pending browser authentication exposes **Cancel sign-in**, with PIN hidden. During PIN verification both unlock choices are disabled. There is no preset/default PIN. Enrollment lasts only for the running authenticated session. Five consecutive failures, expired access/absolute session, restart or security lock require full sign-in. New full sign-in clears the PIN; enroll again explicitly. See [security and failure model](../Identity/POS-Session-PIN.md).

**Switch cashier** is available only after all retained work is resolved. An open shift or cash session, cart, pending checkout/order/refund/review or unresolved outbox blocks the switch. Lock is the pause mechanism for retained work; it does not release custody or replay a transaction. Fingerprint and staff-card readers are explicitly unconfigured; this slice supplies no tested hardware adapter or biometric/card enrollment.

### Lock-choice presentation follow-up — installed

The simplified primary PIN/fallback sign-in choice is installed in follow-up release
`38a23739c3824ef49989785208c86fb1`. The earlier display/PIN release
`2b6baf12d93e4193b61d030d34bae33f` below is historical and retained its peer
Resume/Authenticate control. The one-second session render refreshes availability as
credentials expire. Enrollment, attempt limits, original-cashier binding and full-authentication
policy are unchanged.

Follow-up candidate `d47757ce-b860-49e0-845a-899c36650838` passes the full gate: 1,463 unit,
seven architecture, 147 HTTP, 99 frontend unit and 161 browser cases, 14 owning persistence
and four drawer-authority cases with cleanup, and native print/session/keypad/display plus
updated unlock-choice fixtures. Both npm audits report zero vulnerabilities. Matching
23-file package `38a23739c3824ef49989785208c86fb1` has independently verified native and
Git-filtered source bindings.

After user-confirmed POS closure, the follow-up is activated on the same retained run with
QR pilot `f3c6e831b14e4dc3b1f19f33b7501b04` and supervised host
`4a2916dc7d624505b7ec55ae43060c44` (current-user scheduler running; automatic sign-in
startup off). The installed executable path is verified and POS independently reopens
responsively. Nine protected local files and 159 business tables remain unchanged, as do
credentials, configuration, certificate bytes, reference scope and URLs. Real cashier PKCE
menu/QR history reads and anonymous denials pass; real-manager grid/details/picker/cancel
and navbar checks pass. Trusted portal/guest ordering return 200; guest Kitchen returns 404.
These checks issue no financial or media-approval mutations.

Fresh acceptance `268c3f53e2534fd6be188f7585f78966` has a valid installed binding and fresh
QR candidate, with all seventeen operator cases pending. No historical passes transfer.
Actual PIN enrollment/resume, physical displays/readers and operator/production verification
remain unclaimed.

## Customer display

The assigned customer monitor uses two equal star-width columns separated by a narrow divider. The left displays only the approved **customer** gallery playlist while its existing lease is valid, during both idle and transaction modes. Images fill the panel with aspect-preserving cropping and optional approved public captions. Missing, expired, denied or cleared gallery data falls back to a neutral panel and public shop name. Staff-gallery media is never substituted.

The right displays the public shop name and **Welcome** when no active order projection exists, with the applicable neutral/payment-completion message. During work it shows only the existing copied item names/quantities/prices, explicitly labeled estimate or server total/breakdown and allow-listed tender. Lock masks transaction details while permitted customer advertising may continue. Paid/cancelled results clear items/amounts/tender as before. Sign-out clears prior projection/gallery; restart requires existing display assignment and original-cashier recovery. No customer input, staff identity, proof/reference, card token, credentials, diagnostic history or payment command crosses this boundary.

The right panel scrolls for long orders; actual readability at the purchased 15.6/11.6-inch displays, resolution/scaling and real topology must be checked separately. The half-screen split does not change physical monitor assignment, fullscreen staff lock behavior or fail-closed display-safety handling.

## Configuration and rollout

| Native setting | Default and validation |
| --- | --- |
| `Pos:ShopName` | `NexaConnect`; nonempty public text, at most 80 characters, no control characters |
| `Session:EnablePinUnlock` | `true`; permits explicit session enrollment, does not supply a PIN |
| `Session:IdleTimeoutMinutes` | Existing default 5, range 1–60 |
| `Session:AbsoluteTimeoutHours` | Existing default 10, range 1–24; refresh/PIN never extend it |
| `Session:TokenRefreshLeadSeconds` | Existing default 60, range 15–240 |

The checked-in native `appsettings.json` includes the PIN-enable and shop-name defaults. These settings live in the native terminal configuration; this slice does not add a manager settings editor. Existing configurations remain compatible through defaults. There is no backend route, migration, permission, new dependency or supported-reader SDK. Retain current key rings, token/financial recovery, input/density/printer settings and approved galleries. Close active POS work before a matching package/service refresh; do not install an unverified or mismatched candidate.

Verify PIN validation, attempt exhaustion, enrollment timing, same-operator restoration, expiry/restart/security/full-sign-in fallback and persisted-marker privacy using the focused unit/native fixtures. Verify equal display split, approved-gallery lease/fallback and private projection separately. Full source verification and actual installed operator/physical-reader/display tests are distinct. Test results and activation identifiers must be recorded from executed runs, not inferred from this guide.

## Executed source verification

The final POS build passes with zero warnings and zero errors. The focused unit selection
passes 69 cases; the complete unit suite passes 1,463 with zero skipped after enabling the
temporary current-user Windows DPAPI fixture. All seven architecture tests pass. The full
isolated native PosTouchTests run passes existing payment/drawer/history/scanner/density,
session-expiry and lifecycle regressions plus the new equal customer split at 800×600,
1366×768 and 1920×1080 logical sizes, approved-ad persistence, welcome/working/busy states,
ready-card navigation and masked touch PIN keypad checks. These are executed source/synthetic
fixture results, not physical display or reader verification.

The subsequent complete source-bound candidate
`0fe76278-72af-4b9e-b6ae-d1d8d323f024` passes 1,463 unit, seven architecture, 147 HTTP,
99 frontend unit and 161 browser cases, 14 owning persistence and four drawer-authority
cases with verified cleanup, plus native print/scanning/session/display fixtures. Both
npm audits report zero vulnerabilities. Ten standalone UI-foundation cases also pass the
Customer Portal navbar-only title refinement. Candidate and native package source inventories
are independently verified against current inputs.

## Initial display/PIN activation — 3 November 2026 (historical)

After user-confirmed POS closure, matching 23-file release
`2b6baf12d93e4193b61d030d34bae33f` was installed on retained run
`c30b628ab23141f389119daa565f894f`, with fresh QR pilot
`84b747e1d8644f0783a1a200b17fff64` and supervised host
`2dfc865086a54b45ae499a96a961c549`. The current-user scheduler runs independently;
automatic sign-in startup remains off. The new installed POS path is confirmed and reopened
responsively through the desktop shell.

Preservation checks confirm nine protected local files and 159 business tables unchanged,
with the same credentials, configuration, certificate bytes, reference scope and URLs.
Real cashier PKCE reads pass the three-item menu and scoped open/paid QR-history checks;
anonymous access is denied. Real-manager portal checks pass grid/details thumbnails,
menu picker/cancellation and navbar-only inner-page titles. Trusted portal/guest ordering
return 200; the guest Kitchen path returns 404. These checks create no order/payment,
media assignment or approval and do not enroll a real PIN or verify physical displays/readers.

The then-fresh acceptance pack `d6bbacf1b5f140f8844d0a1b0870f07e` had a valid installed binding and
fresh QR candidate, with all seventeen operator cases pending. No historical passes are
transferred. Actual original-cashier/PIN/display testing remains required; this activation
does not establish production readiness or fingerprint/card support.
