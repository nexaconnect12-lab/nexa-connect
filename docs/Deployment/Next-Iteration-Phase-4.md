# Next-iteration Phase 4 — staff and customer Windows POS displays

Implemented in source on 21 October 2026 from `c496f8e`. The existing WPF staff window retains all
role-checked operational actions. A second window displays an allow-listed projection of
the same active transaction; it owns no checkout, service API, recovery store or permission.
See [ADR-039](../Architecture/Decisions/ADR-039-pos-staff-customer-displays.md) and
[the roadmap](../Planning/Next-Iteration-Roadmap.md).

## Setup and recovery

1. Start the replacement candidate on the private staff monitor. In Windows choose **Extend**,
   verify monitor identities physically, and keep staff controls away from customer use.
2. Authenticate through the existing original-operator flow. Open **Terminal & sync**.
3. Choose **Refresh displays**, explicitly select **Staff display** and **Customer display**,
   then **Apply display roles** and confirm. Device name, pixel resolution and primary flag
   help identify each display. At least two distinct, non-overlapping monitor rectangles
   are required. Duplicate/invalid/unknown assignments are refused.
4. The main staff window is placed inside the selected staff work area. The borderless,
   non-activating customer window occupies its selected display. Placement uses physical
   pixels, including negative coordinates, with PerMonitorV2 awareness. The staff minimum
   remains 1000×680 WPF DIPs; incompatible geometry/scaling fails closed rather than spilling
   controls to another display. Final hardware sizes are still unconfirmed.
5. **Use staff screen only** clears/closes customer output and keeps normal single-screen
   operation. Both role changes and explicit disable require an authenticated, non-busy operator.
6. A topology/DPI change, unsafe move/resize across the staff boundary, assignment failure
   after placement starts, or unexpected customer-window closure invokes the safety path.
   Customer output closes, staff content/authentication controls are masked, and the original
   session locks without discarding financial recovery. Only neutral restart guidance and
   **Close POS safely** remain visible. A rejected move is fenced before relocation.
7. Stop customer use, reconnect the private staff screen, close/restart POS there and select
   roles again. Reconnect does not automatically reopen output. Each process starts with no
   customer assignment/window; no monitor preference or customer content is stored on disk.

The safety view cannot authenticate or resume. Ordinary idle lock keeps the customer window
open with neutral **Please wait** content and leaves staff authentication on the staff screen.
Customer touch/mouse activity does not count as staff activity or unlock work. This is not
Windows kiosk mode: physically verify extended desktop and prevent OS mirroring/customer
access to staff hardware. Native driver/event timing needs actual hardware acceptance.

## Customer content and financial invariants

| Source/event | Customer presentation |
| --- | --- |
| Cart editing | Copied item names/quantities/menu unit prices, labeled **Cart estimate** |
| Server quote | Quote lines plus exact server subtotal/service charge/tax/total, labeled **Quoted total** |
| Accepted/current nonterminal Order | Stored server total/breakdown and allow-listed tender label; payment remains pending |
| Uncertain payment/checkout | Verification guidance; never a success indication or new collection instruction |
| Pricing conflict | Clears stale pricing and requests cashier review |
| Cancellation pending/review | Verification guidance; never cancelled until confirmed |
| Validated Paid result | Generic **Payment confirmed**; items, amounts, currency and tender clear immediately |
| Confirmed cancellation/rejection/payment failure | Generic outcome; transaction details clear |
| Lock/expiry | Neutral customer view with no transaction details, even if an in-flight command finishes |
| Original-operator resume | Latest in-memory projection, subject to the original lock/authentication guard |
| Successful sign-out | Welcome content; prior customer projection cleared |
| Restart | No customer window; pending settlement can show retained total only as unconfirmed after authentication/reassignment, without reconstructed lines |

The customer contract excludes tokens, staff/actor identity, branch/terminal/Order UUIDs,
bank references, raw errors, financial review data, refund details and unrelated receipt
lookups. The customer window has no payment, navigation, unlock or authentication actions.
Totals remain visible independently of the item viewport; very long orders need device
readability testing. No automatic scrolling, customer input or image gallery is added.

Existing original checkout/settlement/cancellation keys, DPAPI/SQLite scope, write-before-send
ordering, source-service authorization, optimistic concurrency and transaction/outbox boundaries
remain. Display publication failures cannot escape a financial callback or change its send/clear
ordering. Monitor events do not submit, cancel, refund, settle or replay business commands.

The touched manual-settlement client now checks nonempty settlement ID, matching Order ID,
`Status=Paid`, method, exact amount and currency before recovery can clear. A mismatched 2xx
response is unconfirmed and preserves the same attempt. Legitimate committed replay remains
accepted. The endpoint and service-owned settlement semantics are unchanged.

The adapter uses existing shared JSON logging and correlation through
`nexaconnect-pos-client`; `order.manual-settlement` is the bounded operation name. Correlation
is the original Order UUID. Query the configured POS-client logs for `Checkout boundary rejected`,
`Checkout transport failed` or `Checkout response rejected`, then the correlation ID. No amount,
body, bank reference, credential, cookie or arbitrary header is logged. Like existing WPF
telemetry, console visibility requires an attached host; file/OTLP deployment is not added.

## Executed verification

- WPF build passes with zero warnings/errors.
- Full Unit suite with Windows DPAPI opt-in: **888 passed, zero failures/skips**.
- Architecture suite: **6 passed, zero failures/skips**.
- Focused display/settlement/checkout subset: **50 passed**, included in the full count.
  Thirty new unit cases cover projection privacy/clearing, exact pricing, intermediate
  outcomes, lock/in-flight updates, restart, explicit roles, negative coordinates, monitor
  removal/resolution/work-area/primary changes, reconnection fencing, and settlement mismatch/replay.
- Off-screen rendering uses actual customer XAML and stripped staff XAML with synthetic
  data. Five customer views at 1024×768/800×600 and four staff setup/safety views at
  1280×850/1000×680 are developer layout evidence. Selected total/tender/message and staff
  setup/safety bounds checks found no clipping of selected controls. All nine images were
  visually reviewed; this is separate from native placement acceptance.

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE = '1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore
dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj --no-restore
```

Private developer renderer/output lives under ignored `.runstate/phase4-display-preview`.
It renders without showing/assigning windows, starting POS/App, registering callbacks,
authenticating or opening recovery. It does not establish interactive behavior, actual native
placement, DPI handling or physical monitor acceptance. Existing first-draft installer,
release activation, recovery files and UAT evidence are untouched. No install/deployment,
commit/push, service migration, identity grant or public hosting is part of this implementation.

## Phase 9 device checklist

Use a new frozen candidate/UAT identity, preserving the first draft. Record monitor models,
ports, pixel resolutions, orientation, Windows scaling and which supports touch.

- Verify staff/customer assignment in both left/right layouts, including negative coordinates,
  primary-role changes and mixed DPI. Verify the staff UI never appears on the customer screen.
- Add/change/remove cart lines, review actual server charges/total, then cash/manual PromptPay.
  Exercise confirmed, lost-response and exact-replay outcomes without duplicate financial effects.
- Confirm receipt lookup, cash review and refunds do not project unrelated customer information.
- Lock during checkout/payment, try customer touch/key activity, cancel/wrong-user/failed resume,
  then authenticate the original operator. Check no details leak while locked.
- Confirm Paid/cancelled/rejected outcomes clear details, then start the next order and sign out.
- Unplug each monitor during idle, cart, in-flight payment and lock. Verify neutral safety mask,
  closed customer output and retained original recovery. Reconnect must not auto-restore.
- Close/restart on the private staff display, verify fresh role assignment and single-screen mode.
- Exercise Windows resolution/scaling changes, long names/orders/large totals, actual touch,
  sleep/wake and device driver behavior. Do not treat unit/bitmap checks as those observations.

Phase 5 adds approved media/gallery and distinct screensavers; this phase uses neutral text.
