# POS UX8B — prepared native drawer software integration

## Status and equipment gate

The user selected software preparation because the exact PAISHUO printer model, 4141P drawer
connection and vendor opening-test details are unavailable. Native coordination, the UX8A
HTTP adapter, disabled manual controls and the guarded post-cash-payment hook are implemented
in source. **Physical UX8B is not complete.** Production constructs only DisabledDrawerDriver;
there is no enable switch, RAW command, pulse/pin/voltage selection or receipt-queue fallback.
This preparation was initially source-only. Subsequent [UX8D](POS-UX8D-Pilot-Acceptance.md)
installed matching software/migrations with history-only reads; physical opening remains disabled.

The staff Terminal & sync → Cash drawer section shows software prepared/hardware disabled,
three predefined manual reasons and an unavailable opening action. Printer selection cannot
enable it. No customer-display controls, financial commands, new backend schema, permission,
credential or package-configuration field are introduced. UX8A's POS24 / Authorization16 and
explicit backend DrawerIntents enablement remain prerequisites for eventual activation.

UX8C subsequently adds [authorized read-only history](POS-UX8C-Drawer-History.md), including
inspection of an existing journal while the driver is disabled. The no-I/O statements below
describe default startup and opening workflows, not explicit authorized history reads.
Protected lookup context is additive for future reservations.

## Native workflow

1. Validate the full original scope/actor and require a configured driver plus current session.
   The production disabled driver returns before journal access or HTTP.
2. Reserve the separate local intent before contacting POS. This consumes the original sale
   across retry operation IDs, or the explicit manual operation, even if the response is lost.
3. Request the UX8A server grant with the current original subject/issuer, unexpired unlocked
   token and exact configured scope. HTTP denies redirects/cookies, caps successful JSON at
   4096 bytes and bounds the entire headers/body operation to ten seconds. The original
   operation GUID is propagated as correlation; no response bodies or credentials are logged.
4. Only a first, matching reserved server grant can allow one future driver submission. Check
   the current cashier/generation/shift/cash session, cancellation, driver availability and
   the unexpired maximum60-second lease immediately before the call. Replay/read cannot grant.
5. Driver success means Submitted, not physically opened. Driver error/cancellation after
   entering submission means Unknown. Report the fixed outcome with an independent five-second
   deadline and the still-current original identity, then finish the local intent.
6. Report/identity/transport failure retains consumed Unknown (or Reserved if local storage
   cannot complete). AuditUnavailableAfterAttempt is distinct from failure before submission.
   No automatic device retry, local reset, journal deletion or grant renewal is provided.

Denied authority also consumes the local attempt without opening. A fresh deliberate manual
action has its own operation and explicit confirmation; it does not reuse or repair the original
intent's evidence. Distinct authorized manual operations are not globally blocked by earlier
uncertain attempts. Investigation/reconciliation controls remain a physical-activation
prerequisite, not an implemented global lockout. Original status lookup is read-only through the adapter and never submits a
device command; a restart reconciliation UI is not supplied in this preparation.

## Payment and manual-action boundaries

The payment hook runs after server-confirmed payment and protected financial recovery cleanup.
It captures original subject, UI generation, shift, cash session, Order and settlement key.
Only a fresh, non-replayed original THB cash confirmation with matching original amount/Order
is eligible. Existing uncertain-payment verification is excluded even if its response is not
marked replayed. PromptPay, card, receipt reprint, refund and generic recovery never trigger
automatic opening. Missing/delayed POS cash projection denies the grant; there is no polling
or historical replay. Drawer failures do not collect, reverse or recreate payment recovery.

Manual controls additionally require a configured driver, signed-in private staff display,
open local custody, no in-flight/uncertain payment or refund, a predefined reason and deliberate
confirmation. Pending checkout/Order, refund and Cash Review attempts also block the manual
action. This slice adds no separate unresolved cash-movement outbox guard. UX8A still enforces
live permission and authoritative original cashier custody; manager role or a local flag
cannot act as another cashier. Lock, cashier change and stale
responses cannot restore actionable UI or start another submission. These software paths
are exercised with fake drivers only until the hardware gate is satisfied.

## Local storage, verification and follow-up

The lazy separate `drawer-intents.db` path is inside the existing current-user configured POS
storage directory, including isolated-pilot scope where selected. It is independent of the
financial `pos-state.db` schema and release folders. Default startup/opening workflows create
or open no file; UX8C history only opens an existing file read-only after authorization.
The original intent table stores hashes, fixed cause/reason/outcome and UTC times, without raw subjects,
payment/card data, device commands or tokens. Preserve it with recovery backups and updates;
never remove it to repeat an action. See [journal lifecycle](../Database/POS-Drawer-Intents.md).

Run the complete `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` Windows gate. Focused
DrawerWorkflowTests/DrawerIntegrationTests cover HTTP contract mapping, token/scope/lock checks,
lost responses, lease/size validation, consumed replay, outcome-report failures and payment
eligibility. The Windows fixture checks default-disabled controls/no file, then a fake configured
driver through the actual native hook, including failure without payment-recovery mutation.
No real hardware, merchant payment, authentication session or existing pilot database is used.

Final candidate `1b751ebb-9ae8-4dd2-bc9d-65c57ca6316c` completed the full gate on
30 October 2026 at 00:00 Singapore time: 1,312 unit tests, six architecture checks,
84 HTTP cases, 90 frontend tests, 66 QR browser cases and the required Windows, hosting,
security and profile checks passed. Nine QR owning cases verified cleanup/broker recovery;
three separately hashed drawer owning cases verified cleanup
(`c3e1e48412d64c959cb4eedbb3fa65ba`). Earlier diagnostic evidence affected by source changes
remains historical and was not relabelled as passed. This verifies software preparation only:
the retained pilot was not installed, migrated or activated for UX8B, and the production
driver remains disabled with no default journal I/O or physical acceptance.

Service `nexaconnect-pos-client`, logger NexaConnect.POS.Drawer, emits safe fixed Drawer client
boundary/transport/response events with validated original correlation IDs. Join them to UX8A
`nexaconnect-pos` Drawer intent events. Do not log bodies, subjects, headers, tokens or commands.

To complete physical UX8B: supply exact printer model/driver, drawer connector/electrical
compatibility, supported command and successful vendor opening test; implement one verified
adapter/profile, then test authorized/denied manual opening, cash-only automatic opening,
lock/restart, duplicate suppression, disconnect/spooler failure and audit uncertainty. Activate
only matching tested backend/native packages while preserving custody/journals. No physical
acceptance or public release is implied by software tests.

See [UX8A](POS-UX8A-Drawer-Authority.md), [API](../API/Drawer-Intents.md) and
[ADR-056](../Architecture/Decisions/ADR-056-native-drawer-coordination.md).
