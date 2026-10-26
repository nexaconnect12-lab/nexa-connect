# Table QR ordering — implementation roadmap

QR9 — end-to-end pilot acceptance and fixes: diagnostic/checklist/recording tooling is implemented; execute actual iOS/Android, cashier/Kitchen/payment/PDF and interruption cases, resolve observed defects and retain fresh candidate evidence. Source implementation does not complete actual acceptance. See [QR9](../Deployment/QR9-Acceptance.md).

QR8 — workflow usability and status feedback is implemented in source: friendly labels, active-screen refresh, collection blocker guidance and bounded guest submission recovery. Integrated verification and local rollout evidence are tracked in [QR8](../Deployment/QR8-Usability.md); actual devices and release acceptance remain separately pending.

QR7 was selected 25 October 2026 as local pilot activation/actual acceptance. At selection,
LAN hostname/trusted certificates were unconfirmed, so [tooling/prerequisites](../Deployment/QR7-Pilot.md)
were prepared before explicit activation. Device results still require actual observations.
QR1–QR6 source remains implemented; QR7 adds the isolated profile/guest edge and pending
operator records without reopening deferred server/hardware purchasing decisions.

The user subsequently selected [all branch Orders in Windows POS with a QR filter](../Deployment/POS-Order-List.md).
The native list and scoped staff read API remove manual UUID entry for eligible QR
collection while preserving original acceptance, custody, payment and recovery. The
operational list does not merge table bills or add guest payment authority; actual
cashier/phone/PDF acceptance remains separately observed.

Recorded 23 October 2026. This is the selected next development track, following the
user's instruction to retain the remaining release work until specifications are ready.
Scope confirmation/repository assessment is complete. [QR1 secure entry](../Deployment/QR1-Entry.md)
now implements entry/context APIs, storage, staff QR display and the mobile landing.
[QR2 menu/cart](../Deployment/QR2-Menu.md) now adds responsive menu browsing, quantities
and authoritative THB totals. [QR3 requests](../Deployment/QR3-Requests.md) now adds retained quote submissions, private
status recovery and staff rejection. [QR4 acceptance](../Deployment/QR4-Acceptance.md) now implements exact guest price review
and atomic original Order handoff to Inventory/Kitchen recovery.
[QR5 collection](../Deployment/QR5-Collection.md) implements Windows cashier payment and
original private guest receipts. [QR6](../Deployment/QR6-Integration.md) implements joined
hardening and local candidate preparation; actual phone/hosted/release acceptance remains pending.

## Confirmed domain — 26 October 2026

The user purchased `usenexaconnect.com` through **Cloudflare Registrar** for NexaConnect.
Use this owned domain when planning QR10's dedicated-server pilot and later cloud migration.
Subdomain assignments, DNS records, public server address and trusted HTTPS activation
remain to be configured and verified; registration does not mean services are deployed.
See the [hosting prerequisites](../Deployment/Next-Iteration-Phase-6.md).

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
| Dedicated-server pilot | Phase 6 | Domain `usenexaconnect.com` purchased at Cloudflare Registrar; OS/version, CPU, RAM, storage, location, workloads, upload speed, DNS/TLS setup, off-server backup destination and operational targets remain pending |
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

QR5's 24 October dependency audit identified GHSA-mrhx-c7xc-m3w5 in Zod3.23.8; that
dated failed audit remains [historical evidence](../Deployment/QR5-Collection.md). QR6
pins patched4.6.7 and adds GUID-compatible financial regressions plus mandatory fresh
production/full dependency audits. See [current verification](../Deployment/QR6-Integration.md);
a passing audit is time-dependent and does not replace the remaining release gates.

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

QR5 adds an explicit original accepted-order collection path to Windows POS
checkout/recovery. Recreating the guest cart as a fresh cashier order would duplicate
Inventory/Kitchen effects. Cashier handoff uses Windows POS; the portal grants no guest
payment authority.

The current draft's authoritative checkout currency is THB; Singapore is the business
timezone/test location. QR must use the branch's supported authoritative currency and
existing pricing policy. This plan does not add SGD, modifiers, online provider payment,
table tabs/transfers/split bills, native apps or offline guest execution.

## Slices and completion evidence

Use the common instructions in the [next-iteration roadmap](Next-Iteration-Roadmap.md#common-instructions-for-every-implementation-prompt)
with each prompt below. Implement and verify one coherent slice at a time. QR0 is this
scope/repository assessment; QR1–QR6 source/tooling is implemented; actual hosted/device/release acceptance remains pending. Intermediate slices must be visibly
incomplete and disabled from public ordering until the dependent flow is verified.

| Slice | Deliverable | Depends on | Exit evidence |
| --- | --- | --- | --- |
| QR0 | Confirm scope and retain prior release gates | None | This recorded agreement and repository assessment |
| QR1 | Secure table entry and per-visitor guest sessions — implemented in source | QR0 | [Contract, verification and remaining deployment/phone gates](../Deployment/QR1-Entry.md) |
| QR2 | Mobile menu, authoritative quote and cart — implemented in source | QR1 | [Scope, bounds, verification and remaining gates](../Deployment/QR2-Menu.md) |
| QR3 | Durable guest submission and staff review — implemented in source | QR2 | [Idempotency, isolation, expiry, quotas and verification](../Deployment/QR3-Requests.md) |
| QR4 | Staff acceptance into existing Order/Inventory/Kitchen — implemented in source | QR3 | [Atomic identity, consent, recovery and verification](../Deployment/QR4-Acceptance.md) |
| QR5 | Windows cashier retrieval and settlement — implemented in source | QR4 | [Original identity, immutable custody, receipt, cash/reporting and verification](../Deployment/QR5-Collection.md) |
| QR6 | Joined hardening and phone-test candidate — implemented in source | QR1–QR5 | [Security/financial/owning verification, fresh local candidate and pending actual-phone gates](../Deployment/QR6-Integration.md) |

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
