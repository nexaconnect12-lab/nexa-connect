# Table QR ordering — implementation roadmap

Recorded 23 October 2026. This is the selected next development track, following the
user's instruction to retain the remaining release work until specifications are ready.
Scope confirmation/repository assessment is complete. [QR1 secure entry](../Deployment/QR1-Entry.md)
now implements entry/context APIs, storage, staff QR display and the mobile landing.
Full menu/cart/order/payment flows and actual phone acceptance remain pending.

## Confirmed flow

- One QR code per table, bound to its organization, restaurant, branch and table context.
- Guests use the phone camera and an iOS/Android browser, without a staff account or a
  required permanent customer profile. Native mobile apps are outside this first scope.
- Guests browse the menu, build a cart and submit a request for staff review.
- Staff accepts before the owning workflow reserves stock and sends work to Kitchen.
  Scanning, cart creation and guest submission must not create Kitchen tickets.
- Payment is collected at the cashier. Neither guest submission nor staff acceptance is
  proof of payment. Existing cash/manual PromptPay authority and POS admission remain
  mandatory; no guest endpoint can confirm a payment or mark an order Paid.
- The guest sees only its own request/order status, even when another guest scans the
  same table QR. The table QR is an entry capability, not access to every table order.

## Retained work — pending specifications and later execution

Do not reopen purchasing/deployment questions until the user supplies specifications.
Preserve the first-draft records and the Phase 8 review evidence. No dates or passes are
invented for these remaining gates:

| Remaining work | Existing owner/phase | Prerequisites/evidence still needed |
| --- | --- | --- |
| Dedicated-server pilot | Phase 6 | OS/version, CPU, RAM, storage, location, workloads, upload speed, domain/TLS, off-server backup destination, operational targets |
| Hosted verification | Phases 6/8 | Actual login/denial, remote POS/Kitchen, supervision/reboot/outage, full-graph off-server restore, alert delivery, measured capacity |
| Windows delivery | Phase 7 | Trusted publisher signing; actual ordinary-account installation, update, callback, reboot and interrupted-install recovery |
| Physical hardware | Phases 4/5/7/9 | Intended Windows PC, two display sizes/resolutions/touch/DPI, disconnect/reconnect, gallery behavior and PDF inspection; purchase specifications remain unconfirmed |
| Exact release freeze | Phase 8 | Authorized coherent commit/push, exact clean-source successful dispatched CI, matching rebuilt artifacts/configuration/schema and all target observations |
| Consolidated second test | Phase 9 | Frozen candidate, actual user observations, real Singapore midnight, financial/recovery retests and user release decision |

The existing local automated gates passed as recorded in
[Phase 8](../Deployment/Next-Iteration-Phase-8.md). They remain historical evidence for
that candidate. QR executable/configuration/schema changes require a new candidate and
matching regression evidence; do not attach changed QR code to the old artifact binding.
Local QR implementation/isolated automated tests can proceed before hardware purchase
or server readiness. A publicly reachable pilot and actual-phone acceptance cannot be
claimed from those local tests. Kiosk, cloud migration and Store publication remain
separate follow-ons.

## Repository assessment and ownership constraints

The canonical [restaurant architecture](../Architecture/Restaurant-POS-Architecture.md#10-qr-ordering)
already describes opaque revocable table entry, guest isolation and the shared Ordering
workflow. Current executable checkout is staff/workload-authorized: Order's quote/place
controller and `OrderPricingService` resolve Catalog prices and Restaurant pricing policy;
`PlaceOrderWorkflow` coordinates Inventory and Kitchen and leaves manual tenders unpaid.
QR1 adds anonymous table entry/context only; it does not expose an anonymous order flow.

The first QR implementation must establish explicit guest authority. It must not forward
a staff session, issue staff roles to guests or weaken existing staff API authorization.
Keep table/branch ownership in Restaurant, order/request lifecycle in Ordering, financial
authority in Order/POS/Payment, and execution in Inventory/Kitchen. Define versioned
contracts/adapters before crossing these boundaries; no cross-service SQL or shared
domain entities. A table label must not silently create floor-management/split-bill rules.

The existing Windows POS checkout/recovery flow also needs an explicit accepted-order
lookup/import path. Recreating the guest cart as a fresh cashier order would duplicate
Inventory/Kitchen effects. Cashier handoff is a required implementation slice, not an
assumed capability of the current portal.

The current draft's authoritative checkout currency is THB; Singapore is the business
timezone/test location. QR must use the branch's supported authoritative currency and
existing pricing policy. This plan does not add SGD, modifiers, online provider payment,
table tabs/transfers/split bills, native apps or offline guest execution.

## Slices and completion evidence

Use the common instructions in the [next-iteration roadmap](Next-Iteration-Roadmap.md#common-instructions-for-every-implementation-prompt)
with each prompt below. Implement and verify one coherent slice at a time. QR0 is this
scope/repository assessment; QR1 source is implemented and QR2–QR6 remain pending. Intermediate slices must be visibly
incomplete and disabled from public ordering until the dependent flow is verified.

| Slice | Deliverable | Depends on | Exit evidence |
| --- | --- | --- | --- |
| QR0 | Confirm scope and retain prior release gates | None | This recorded agreement and repository assessment |
| QR1 | Secure table entry and per-visitor guest sessions — implemented in source | QR0 | [Contract, verification and remaining deployment/phone gates](../Deployment/QR1-Entry.md) |
| QR2 | Mobile menu, authoritative quote and cart | QR1 | Responsive browser UI, scoped menu, bounded quantities, stale-price/availability handling |
| QR3 | Durable guest submission and staff review | QR2 | Idempotent pending requests, private status, authorized bounded queue, rejection/expiry, no Kitchen/financial side effects before acceptance |
| QR4 | Staff acceptance into existing Order/Inventory/Kitchen | QR3 | Stable original order identity, live permission/version/pricing checks, concurrent/retry/outage recovery, one set of downstream effects |
| QR5 | Windows cashier retrieval and settlement | QR4 | Retrieve original accepted order, retained recovery identity, authorized single payment, original receipt and correct cash/reporting effects |
| QR6 | Joined hardening and phone-test candidate | QR1–QR5 | Security/financial regression, fresh candidate, deployment guidance and separately pending physical iOS/Android acceptance |

### QR1 prompt — secure table entry

```text
Implement QR1 using AGENTS.md, the common roadmap instructions and the confirmed QR flow.
Define the guest authority/threat model and record the boundary decision in an ADR before
adding routes. Implement staff-authorized, organization/restaurant/branch-bound table QR
creation, rotation and revocation through owning persistence and independently owned
migrations. Validate the live Restaurant hierarchy and appropriate branch authority.
Use cryptographically unpredictable entry capabilities and separate short-lived per-visitor
sessions. Store secrets safely; keep them out of logs, analytics, referrers and error bodies.
A shared table QR must not expose another visitor's order. Define CSRF/origin policy,
HTTPS/secure cookie behavior, expiration/revocation and bounded anti-abuse controls.
Provide only the entry/context boundary in this slice; no fabricated public order or
payment authority. Adopt shared safe observability/correlation for every new adapter/route.
Test tampering, expired/revoked entry, cross-tenant/table/visitor access and rate limits.
Update API/identity/database/deployment/component docs and architecture summaries; audit.
```

### QR2 prompt — mobile menu and cart

```text
Implement QR2 on the verified QR1 guest context for iOS Safari and Android Chrome browser
use. Add a responsive mobile menu, quantities/cart and an authoritative server quote using
the owning Catalog/Restaurant/Order policies. Scope every request to the validated entry;
do not accept guest-supplied organization/table/currency/prices as authority. Bound inputs
and responses; do not expose internal stock quantities, staff APIs or unpublished assets.
Handle loading, empty menu, unavailable products, changed quotes and connection loss.
Show the table, currency, item totals and taxes/service charges clearly before submission.
Keep guest navigation separate from staff sign-in/session and do not add permanent profiles.
Test menu isolation, manipulated totals, stale prices/availability, keyboard/touch layouts
and narrow/landscape viewports. Phone emulation is not physical-device acceptance.
Update affected docs and obtain the documentation audit. Submission remains QR3.
```

### QR3 prompt — pending guest requests

```text
Implement QR3 durable guest submissions, each bound to the visitor, table, exact reviewed
quote and a retained idempotency identity. Retries/response loss return the original result;
changed payloads cannot reuse that identity. Refresh must recover the guest's own request
without exposing another guest at the same table. Add an authorized bounded staff queue
with pending/rejected/expired states and version checks. Define deadlines and bounded
per-session/table/branch request limits, with cleanup/retention and safe audit metadata.
No guest submission may reserve stock, send Kitchen work, collect money or claim Paid.
Test persistence/restart, concurrent retries, visitor guessing, revocation and denial.
Document the contract and incomplete acceptance/payment flow; run the documentation audit.
```

### QR4 prompt — staff acceptance and owning workflow

```text
Implement QR4 staff acceptance of an original guest request with live scoped placement
authority and optimistic concurrency. Revalidate entry/table policy, the exact reviewed
price/availability and current branch hierarchy. A changed price requires guest review
and consent; do not silently charge a new amount. Record actor/decision/version and the
stable order/command identity durably before coordinating the existing placement workflow.
Represent QR/dine-in/table provenance explicitly without rewriting existing POS history.
Use owning Inventory/Kitchen operations and transactional outbox/audit. Rejection must
have no downstream work; uncertain acceptance must resume the original command, not create
a replacement order. Kitchen dispatch occurs only after the authorized acceptance.
Keep the order unpaid for cashier collection. Test competing staff, stock/price changes,
dependency/broker outages, crash windows and exactly one set of downstream effects.
Update contracts/schema/compatibility/deployment and all relevant docs; audit.
```

### QR5 prompt — cashier handoff and payment

```text
Implement QR5 a scoped Windows POS lookup of the original accepted QR order using a
bounded collection reference. Validate organization/restaurant/branch, terminal, original
order identity, current authenticated cashier/active shift and unpaid order state; the
reference itself grants no settlement authority. Once collection starts, bind recovery
to that original collecting cashier/shift under the existing recovery rules.
Load the authoritative original order/pricing into the existing retained checkout/payment
recovery path. Never place a second order or repeat Inventory/Kitchen placement to collect
payment. Preserve original operation IDs through timeout, lock/resume, restart and replay.
Use existing cash/manual PromptPay admission and payment confirmation rules, with no guest
payment endpoint or provider transfer. Display collection/payment status and original receipt
to staff, guest and customer display under their separate disclosure rules.
Test paid/rejected/wrong-branch orders, concurrent collection, response loss/restart,
revocation, one payment/cash effect and reporting/receipt consistency. Document and audit.
```

### QR6 prompt — integration and actual-phone checklist

```text
Implement QR6 integrated verification across guest entry/menu/quote/submission, staff
acceptance, Inventory/Kitchen, cashier payment/recovery, original receipt and reporting.
Exercise cross-tenant/table/visitor access, tampering/revocation, abuse/CSRF, stale prices,
stock exhaustion, duplicate submissions/acceptance/payment and dependency interruptions.
Keep secrets/financial bodies out of telemetry and apply the shared observability contract.
Run existing financial/architecture/frontend/security gates plus the new owning persistence
and browser checks, sequentially where frontend publication shares dependencies.
Prepare a new matching candidate, deployment/HTTPS/network guidance and a concise actual
iOS Safari/Android Chrome camera-scan test. Include app switching, refresh, network loss,
multiple guests at one table, staff approval and cashier/PDF verification.
Leave actual device/hosted/clean CI/signing and user acceptance pending without evidence.
Preserve prior Phase6–9 deferred work, and refresh affected release/UAT scope rather than
reusing the old Phase8 binding or inventing a passed phone test. Document and audit.
```
