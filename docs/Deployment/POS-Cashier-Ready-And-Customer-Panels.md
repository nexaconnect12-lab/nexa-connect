# Cashier ready panel, session PIN and customer advertising

This native WPF slice adds a staff-ready card and equal-width customer advertising/order panels. It preserves existing checkout, original-payment recovery, service authorization, display safety and approved-gallery ownership. It is a source implementation until a matching verified package is activated; actual operator/hardware acceptance remains separate.

## Cashier workflow

On **Shift & cash** or **Checkout** only, when not busy or processing a cart/pending transaction, the ready card shows the authenticated cashier label, shortened register identifier, shift/cash-session status and **Sign in / Open shift / Start sale** as appropriate. The label uses validated UserInfo `preferred_username`, with a shortened subject fallback. It stays on the staff screen. Start sale navigates to the existing sign-in, shift or checkout controls; it creates no sale automatically. Busy, locked, unsafe-display and working states retain their normal protected operational flow. QR/history/review tabs omit this card to keep their actions reachable on smaller staff screens.

The staff full-sign-in action always requests provider `prompt=login`, including initial sign-in and after local sign-out; it does not silently reuse browser SSO as a cashier switch. For quick resume, complete full sign-in, then choose **Set session unlock PIN** within two minutes. Enter and confirm six digits using the masked touch keypad or keyboard. Manual/idle lock hides work; wake the prompt and choose PIN unlock when offered, or **Resume / Authenticate** for full sign-in. There is no preset/default PIN. Enrollment lasts only for the running authenticated session. Five consecutive failures, expired access/absolute session, restart or security lock require full sign-in. New full sign-in clears the PIN; enroll again explicitly. See [security and failure model](../Identity/POS-Session-PIN.md).

**Switch cashier** is available only after all retained work is resolved. An open shift or cash session, cart, pending checkout/order/refund/review or unresolved outbox blocks the switch. Lock is the pause mechanism for retained work; it does not release custody or replay a transaction. Fingerprint and staff-card readers are explicitly unconfigured; this slice supplies no tested hardware adapter or biometric/card enrollment.

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

No full QR release candidate or local-pilot activation is produced for this slice. The
installed pilot remains unchanged. Matching release verification/package activation and
actual original-cashier/display acceptance remain follow-up work.
