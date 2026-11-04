# Product expansion roadmap

## Cashier/customer refinement — 3 November 2026

The [current native slice](../Deployment/POS-Cashier-Ready-And-Customer-Panels.md) implements a ready identity/status card, guarded full-auth cashier switch and 50/50 approved customer advertising/welcome/order display. The [persistent cashier PIN follow-up](../Identity/POS-Session-PIN.md) adds a POS-owned registry and private online renewal/verification for the original cashier, with matching POS26/Authorization17 activated in the retained pilot and fresh PIN/operator acceptance pending. Enrollment survives future full logins until changed/reset/revoked; actual provider-session expiry/restart/security still requires full login. This does not complete A1/A2 hardware credential enrollment: fingerprint readers, RFID/card adapters and offline identity authority remain unimplemented.


## Shared photo-library direction — 3 November 2026

Use iOS Photos as the interaction reference: a thumbnail grid by default, a switchable details list and a contextual picker popup reused throughout the application. The [first library slice](../Deployment/Media-Library.md) implements product-photo grid/details, server search/24-photo paging, private PNG previews, existing download/confirmed delete and shared menu/screensaver pickers. Candidate selection remains separate from explicit approval/save. This does not complete all M1/M2: batches/drag-drop, metadata names, replacement, bulk actions, uploader metadata, sorting, persistent preferences, other safe formats and additional app consumers remain planned below. No new storage system or framework is introduced. The retained local pilot is activated; fresh operator acceptance remains pending.


Recorded: 2 November 2026. Status: requirements backlog and initial codebase assessment.

This records the requested eight modules and their execution requirements before UX24.
UX24 remains the existing-target operator acceptance and confirmed-defect milestone; this
backlog does not cancel it or renumber earlier UX/QR phases. The phases below describe planned
implementation slices and are not a calendar commitment; delivered first-slice scope is
explicitly recorded above. The initial
Phase 1 assessment is recorded here; detailed designs and application changes follow when
the corresponding implementation slice is started.

Work in the existing application. Preserve behavior and unrelated working-tree changes;
do not rebuild the application, replace technology or introduce another UI framework
without a documented technical justification. No application, migration, API, authentication,
deployment or live-pilot change is made by recording this roadmap. No commit or push is
authorized by this record.

## Phase 1 foundation: initial assessment

The repository already contains the following reusable foundations. These are source-level
capabilities; they do not imply full device, provider, hosted or operator acceptance.

| Area | Existing implementation and relevant location | Expansion gap |
| --- | --- | --- |
| Windows cashier | .NET10 WPF, protected current-user SQLite/DPAPI state, original checkout recovery and durable outbox in `src/Clients/NexaConnect.POS`; optional touch mode defaults off | Reliable full offline commerce and biometric/card cashier authentication remain extensions |
| Portals and mobile QR | React18, TypeScript, Vite and Ant Design; existing workspace packages under `src/Frontend`; ASP.NET Core Customer BFF keeps staff credentials server-side | Extend existing screens, guest contracts and design system; no second frontend framework |
| Business services | .NET10/ASP.NET Core under `src/Services`; API/Application/Domain/Infrastructure responsibilities, scoped authorization and service-owned repositories | New rules must respect existing bounded contexts and move touched legacy behavior toward those layers |
| Data and integration | Service-owned PostgreSQL, Npgsql infrastructure repositories, Redis sessions, RabbitMQ durable inbox/outbox and versioned integration contracts | Add only needed owner-specific migrations/contracts; no shared mutable domain entities or cross-service SQL |
| Identity | Keycloak OIDC/PKCE, Platform Directory membership, Authorization decisions, original-cashier lock/resume | Enrollment, hardware evidence, credential lifecycle, rate limits and fallback require explicit design |
| Media | Media metadata, signed image upload/download, quotas, signature checks, malware scanning, variants, durable processing/deletion; MinIO/S3-compatible storage | General file purposes, documents, batch UX, metadata editing, replacement, bulk management and richer browsing require extensions |
| Menu and commercial workflow | Catalog details/categories/availability/photos/barcodes; Restaurant branch pricing; Order server quote and immutable accepted pricing/receipt snapshots | Promotions, voucher redemption, modifiers and their tax/service/refund interactions are not supplied by current pricing |
| Restaurant and QR | Restaurant-owned table QR/visitor entry, individual guest histories, Order quotes/requests, staff acceptance before Kitchen, original-order cashier collection | Generic QR allocation, occupancy/reservation concurrency, sharing and reassignment policies need design |
| Stock and fulfillment | Inventory reservations, Order recovery/compensation, Kitchen versioned ticket transitions, Payment/POS settlement and review | Recipe/cost/wastage coverage and branch-local execution must be assessed before forecasting or offline claims |
| Customers | Customer-owned initial profile creation/read with tenant authorization, audit/outbox and conflict-safe creation | Loyalty groups, consented delivery details and richer profile lifecycle must not be assumed implemented |
| Reporting | Read projections and financial completeness/reconciliation boundaries in `src/Services/NexaConnect.Services.Reporting` | Forecasts, ingredient predictions, anomaly baselines and an insights dashboard remain planned |
| Notifications | `src/Services/NexaConnect.Services.Notification` already owns queued delivery, inbox/outbox, leased attempts, provider reconciliation and bounded retries | Operational alert rules, acknowledgement/resolution, deduplication/cooldown/escalation and notification-center UX remain extensions |
| Operations and tests | Shared JSON/correlation/optional OTLP foundation; migrations tool; xUnit unit/integration/architecture tests, Windows fixtures, Vitest and Playwright; guarded pilot/QR tools | Each slice needs its own scoped verification and deployment prerequisites; previous tests are not new-feature acceptance |

Assessment references: [overview](../../AI/architecture/project_overview.md),
[system architecture](../Architecture/Project-Architecture.md),
[restaurant/offline architecture](../Architecture/Restaurant-POS-Architecture.md),
[DDD approach](../Architecture/Decisions/ADR-005-domain-driven-design.md),
[Media](../../src/Services/NexaConnect.Services.Media/README.md),
[Customer](../../src/Services/NexaConnect.Services.Customer/README.md),
[Notification](../../src/Services/NexaConnect.Services.Notification/README.md),
[migration tooling](../../src/Tools/NexaConnect.DataMigration/README.md),
[frontend](../../src/Frontend/README.md), [POS](../../src/Clients/NexaConnect.POS/README.md),
[current pricing](../API/Order-Pricing.md) and [local recovery storage](../Database/POS-Local-SQLite.md).

## Ownership, database and API rules

Before changing a module, inspect its project/dependencies, actual schema and migrations,
controllers/BFF routes, authentication, workflow, validations, error mapping and tests.
Write a small implementation plan naming affected files, owners, dependencies, migrations,
API changes, risks and tests; then implement that invoked slice end to end unless genuinely
blocked. Do not stop at a screen or a plan when implementation is requested.

Reuse existing Media assets, Customer profiles, Restaurant table/QR records, Order snapshots,
POS local outbox and Notification delivery. Names such as MediaFile/MediaMetadata,
Promotion/PromotionRule/Voucher/VoucherRedemption, StaffCredential/AuthenticationAudit,
QrCode/TableQrToken, SyncOperation/SyncConflict, BusinessInsight/ForecastResult,
Alert/AlertDelivery and ExternalOrder/IntegrationMessage/ProviderConfiguration are conceptual
needs, not instructions to duplicate tables or create services immediately. Ownership of
new promotion, alert and insight aggregates is a Phase 1 design decision.

Keep business invariants in Domain/Application. Controllers handle transport, validation
and response mapping; persistence, storage, HTTP, SDKs and messaging adapters stay in
Infrastructure behind narrow ports. Repositories follow aggregate needs. Parameterize SQL,
use least-privilege runtime roles and explicit owner-local transactions. Coordinate contexts
through versioned contracts, inbox/outbox and appropriate reservations/compensation, not
cross-database transactions or shared business entities.

Use existing versioned routes, protected tenant context, live authorization, CSRF where
applicable, bounded requests and paginated collections. Retryable commands need stable
identities, concurrency fencing, explicit outcomes and auditability. Secrets stay outside
source control. Append owner-specific migration directories under
`src/Tools/NexaConnect.DataMigration/Scripts`; never rewrite applied migrations. Publish
compatibility, forward recovery and populated-data rollback limits before rollout. Every
new/changed service/BFF/adapter adopts shared correlation and safe structured telemetry;
workers provide JSON and optional OTLP telemetry without logging secrets or business payloads.

## Phase sequence and slice gates

| Phase | Manageable slices | Exit gate |
| --- | --- | --- |
| 1 — Foundation | F1 assessment (initial record above); F2 business/ownership decisions; F3 versioned data/API/security designs; F4 reuse/extension of authorization, audit, configuration and existing UI primitives | Agreed rules and prerequisites for the next feature slice; no speculative bulk schema creation |
| 2 — Core business | M1 media browse/batch UX; M2 metadata and permissioned lifecycle; P1 promotion eligibility/quote model; P2 redemption/checkout/reporting; A1 authentication ports/simulator; A2 approved reader enrollment/login | Connected UI, owner APIs/persistence, failure/recovery tests and explicit hardware/provider verification status |
| 3 — Ordering/reliability | Q1 table policy/concurrent allocation; Q2 generic QR and staff corrections; O1 cached menu/durable drafts; O2 approved offline cash/local Kitchen; O3 safe sync/conflict review; W1 pickup/delivery website; W2 selected provider adapters | Original-operation recovery and tenant/privacy boundaries survive retries, partitions and restart |
| 4 — Operations/insights | N1 rule-driven alert state and in-app center; N2 configured delivery/escalation; I1 data quality/descriptive trends; I2 demand/ingredient forecasts; I3 anomaly investigation and shortage-alert integration | Explainable, permissioned, freshness-aware outputs; no automatic financial or purchasing decisions |
| 5 — Verification | V1 combined regression/concurrency/security; V2 migrations/configuration/deployment rehearsal; V3 actual hardware/provider/operator acceptance and documentation audit | Measured results and documented unresolved dependencies; no simulated result promoted to real acceptance |

Dependencies may refine the order: promotion policy precedes promotional QR/online/offline
pricing; offline authority precedes offline cash; recipes/cost/data completeness precede
ingredient/margin predictions; forecast-based alerts follow usable forecasts. Existing
rule-based alerts and descriptive reports can precede forecasting. No dates are promised
until business rules, hardware, providers and hosting prerequisites are confirmed.

## 2A. Media Management

Extend the current Media context and portal rather than introducing another asset store.

- Multiple files per operation and drag/drop where supported; per-file progress, successful/
  failed status, bounded concurrency and explicit failed-file retry.
- Editable file/display names and approved metadata with safe normalization, duplicate-name
  handling, concurrency protection and audited renames. Preserve original upload provenance;
  a display name never becomes a physical storage path.
- Preview supported safe formats; permissioned original/variant download, replacement and
  deletion. Confirm destructive actions and show reference/usage consequences before commit.
- Search, filters, sorting and server pagination. Grid view shows thumbnails/previews and
  useful metadata; Details view shows name/type/size/upload date/uploader/actions. Preserve
  useful view/filter choices per appropriate user/workspace scope and support practical
  multi-selection/bulk actions with per-file results.
- Keep metadata in Media-owned PostgreSQL, separate from the storage port. Reuse unique
  storage identifiers and private MinIO/S3-compatible storage. Evaluate an explicit local
  storage adapter only if required; do not replace the existing object-storage default.
- Reuse quotas, extension/MIME/signature/size checks, scanning, tenant permissions, signed
  leases, quarantine, processing and durable deletion. Prevent path traversal, unauthorized
  reads and unsafe active content. General documents/other formats need an approved allowlist,
  scanner and preview policy; current image support is not general document support.
- Adapt existing asset fields for identity, original/display name, storage key, content type,
  size, uploader and created/updated timestamps. Reuse versions rather than mutable object keys.
- Define replacements as safe version/new-asset operations. Revalidate Catalog/Restaurant
  references and explicit public approval through owner contracts; never silently substitute
  menu, approved screensaver or merchant QR content through a generic upload permission.

Likely components: Media API/Application/Domain/Infrastructure, Customer BFF, existing
portal media/menu screens and shared design-system controls; Media migrations only as needed.
Risks/tests: upload interruption, quota races, scan/storage outage, wrong tenant/purpose,
replacement/delete/reference races, pagination and bulk partial failure. Retention and allowed
file formats remain prerequisite decisions.

## 2B. Promotions and Discount Management

- Cover codes/vouchers, percentage and fixed discounts, set meals/combos, buy-one-get-one,
  happy-hour pricing, item/category offers and minimum-spend rules. Customer/loyalty targeting
  is conditional on actual Customer/group support, not assumed from profile creation alone.
- Configure name/description/type, active/inactive/expired status, effective dates/times,
  outlet/channel/day-of-week scope, items/categories, spend/quantity thresholds, value/cap,
  voucher and usage limits, eligible supported groups and whether offers can stack.
- Establish deterministic conflict/priority/stacking rules, branch timezone, rounding and
  rule-version semantics. Evaluate eligibility on the server and recalculate fresh quotes
  when items/quantities or relevant context change. Display original price, each discount
  and final amount; validate codes before accepting a commercial snapshot.
- Prevent duplicate/concurrent over-redemption with owner-local constraints/transactions,
  stable original-order identities and explicit reservation/consume/release recovery.
  Define cancellation, expiry, failed payment and refund effects on voucher eligibility.
- Retain immutable applied-rule versions, allocations and totals on accepted orders/receipts
  and reporting events. Editing a promotion must not recalculate completed transactions.
- Require permission, limits, reason and immutable audit for overrides; no client-authoritative
  discount amount. Cover zero/negative totals, caps, currency/quantity precision, combo stock
  effects and replay without double inventory deductions.
- **Decision gate:** restaurant-approved interaction with tax-inclusive/exclusive prices,
  service charges, mixed/other discounts, exemptions and refunds/voids. Preserve existing
  pricing until a versioned extension is agreed. Singapore testing and THB currency do not
  establish a universal tax order or jurisdictional certification.

Likely components: Order pricing Domain/Application and immutable receipts, Restaurant
configuration, Catalog/menu references, Authorization, Payment/POS refunds and Reporting;
promotion-rule ownership and any new context are to be decided. Tests include decimal
calculations, time boundaries, stale quotes, eligibility, parallel last-voucher redemption,
rollback/outbox consistency and original refund/receipt allocation.

## 2C. Cashier Biometric and Card Login

- Device-independent ports such as `IBiometricReader`, `ICardReader` and
  `ICashierAuthenticationService`, adapted to existing layering and OIDC/session ownership.
- Authorized enrollment, replacement, revocation and account deactivation; credentials
  resolve to an active staff account with live appropriate access. Administrator enrollment
  authority is separate from ordinary cashier use.
- Enable/disable individual methods. Preserve an approved fallback: existing login and,
  only after its security design, PIN or another suitable method. Handle failed/unknown/
  repeated scans, unavailable devices and reader/SDK errors with clear feedback.
- Verify cashier identity before granting/resuming work; preserve original-cashier custody,
  idle-lock and pending-payment ownership. A shared Windows account's fingerprint unlock
  cannot silently identify a particular cashier.
- Evaluate Windows Biometric Framework/device-verified mechanisms versus a documented
  vendor SDK only after OS, exact reader, SDK support and licensing are known. Identify
  card transport: PC/SC, HID, keyboard-wedge or vendor API. Barcode availability does not
  prove staff-card or fingerprint support.
- Prefer device-verified biometric evidence. No raw fingerprint images/templates are
  stored by default; any exceptional design needs explicit justified security, privacy and
  legal review. Card identifiers
  may be cloneable/public and may require a second factor or challenge.
- Rate-limit attempts and audit outcomes/enrollment/revocation without raw biometric,
  card-secret, PIN or token logging. Provide development mocks/simulators and label their
  results separately from genuine reader verification. Offline authentication depends on
  the separately approved revocation/freshness/grace policy.

Likely components: POS authentication/device Infrastructure, Keycloak identity integration,
Authorization and a designated credential-owning Application/persistence boundary. Hardware
model/connection/SDK/licensing and proof-to-OIDC binding are prerequisites, not selected here.

## 3A. QR Ordering with Automatic Table Assignment

- Preserve existing signed/opaque table entry, individual guest sessions, digital menu/cart,
  server quotes, duplicate-safe submission, staff acceptance before Kitchen and original-order
  cashier collection. A table QR already identifies its table; do not recreate that feature.
- Extend descriptions, supported modifiers, availability and approved promotions through the
  existing quote/confirmation/status flow, with no guest account requirement by default.
- A generic restaurant QR needs an explicit assignment policy: party size/capacity, available
  service/zone, occupied/held tables, existing open orders, shared-table permission and expiry.
  Assign only an eligible table through a version-fenced/transactional reservation or lease;
  prevent simultaneous guests from claiming incompatible occupancy.
- Show the assigned table before confirmation. If the correct physical table cannot be
  determined safely, request table selection or staff help; do not randomly assign an occupied
  table. Define stale-lease recovery, shared seating and existing-order behavior.
- Authorized staff correction/reassignment requires reason/audit and conflict checks against
  original order/fulfillment state. Do not automatically merge bills, guest histories or payments.
- Keep prices, availability, totals and promotion validation server-owned. Never expose
  sequential IDs as authorization tokens; retain replay/revocation/tenant/guest privacy guards.

Likely components: Restaurant table/visit/occupancy Domain/Application, Order QR use cases,
Customer BFF/guest UI, POS and Kitchen adapters. Tests include concurrent allocation,
expired leases, corrected assignments, duplicate submit, cross-branch tokens and isolated
guest history. Seating/shared-table/reassignment rules remain prerequisites.

## 3B. Offline POS and Safe Synchronization

The existing SQLite recovery/outbox does **not** establish complete offline selling.
Distinguish cloud/WAN loss with a working branch LAN from an isolated terminal or failed
branch server. The [proposed branch-edge model](../Architecture/Restaurant-POS-Architecture.md)
and business authority must be confirmed before enabling offline financial operations.

- Stage cached menu/pricing and durable drafts first, with version/freshness, connectivity
  indicators and pending-synchronization counts visible to authorized staff. Later enable
  approved order edits, local table state, local Kitchen delivery,
  cash recording and eligible queued operations only under the agreed authority model.
- Windows uses its existing appropriate durable SQLite foundations. A future browser offline
  path must evaluate IndexedDB plus local service/branch architecture; browser cache is not
  sufficient authority for reliable transactions.
- Record each durable command with stable global operation/idempotency IDs, contract version,
  tenant/branch/terminal/user/shift scope, dependency ordering and original device/business
  timestamps. Track pending/processing/synchronized/failed outcomes, leases and server receipts.
- Atomically save local operation/state before showing success. Use explicit acknowledgements,
  checkpoints, retention and idempotent server processing; recover interrupted sync/restart
  with bounded exponential backoff and visible failed/conflict queues and authorized original retry.
- Define explicit conflict handling for stale menu/pricing/promotions, concurrent table/order
  changes and stock availability. Preserve accepted commercial snapshots; request review/consent
  for proposals that require repricing. No silent newer-state overwrite or financial last-write-wins.
- Prevent duplicate stock deduction, cash posting and provider capture. Distinguish local cash
  acceptance, sync status and authoritative server posting; rejected sync must never discard
  collected money or fabricate a successful reconciliation.
- Respect shift/admission, offline identity grace/revocation, tax/rule freshness, clock skew,
  business-day closure, settlement barriers and late-work custody. Define multi-terminal ownership
  and stock limits before deployment; document consistency, recovery and manual review.
- Provider-dependent card/online confirmations and external delivery operations follow actual
  documented provider offline capabilities. Never mark an unconfirmed external payment Paid.

Likely components: POS local Domain/Application/storage, branch-edge topology if approved,
Order/Inventory/Kitchen/Payment/POS owners and versioned sync adapters. Tests cover partitions,
lost acknowledgements, exact replay, restart during sync, concurrent edits, day-close conflicts,
clock skew, denial/revocation and exhausted retry/reconciliation. Branch hardware/topology,
cash authority and offline limits remain explicit design prerequisites.

## 3C. Website Ordering and Delivery Integration

- Extend the existing guest menu/cart/server quote foundation for configured pickup and
  delivery, current modifiers/promotions, availability, fees/taxes, confirmation and status.
  Collect only necessary contact/address data with purpose, consent/access and retention rules;
  do not automatically create a customer/loyalty profile from a guest order.
- Preserve staff acceptance and valid POS/Kitchen workflow unless an explicitly approved
  channel policy changes it. Map a customer lifecycle such as Pending → Accepted → Preparing
  → Ready → Completed onto actual internal states; payment and fulfillment remain separate,
  with constrained rejection/cancellation/refund/failure and manual-review transitions.
- Provider-independent ports and provider-specific adapters only for documented, available APIs.
  Confirm intended partners, region/access contracts, sandbox, credentials, licensing, status
  and menu/modifier mappings before choosing or enabling an adapter. Supply realistic mocks meanwhile.
- Signed authenticated webhooks or supported polling; resolve tenant from trusted provider
  configuration, validate schemas/timestamps/replay protection and deduplicate external order/
  message IDs. Provider-reported payment status is not an unverified internal payment capture.
- Normalize menu items, quantities, modifiers, prices and states through an anti-corruption
  adapter; reconcile fees, unavailable items, acceptance/preparation updates and cancellation/
  refund boundaries without inventing invalid internal transitions or duplicate orders.
- Handle rate limits, timeouts, retries, outages, delivery logs, dead-letter/failed messages
  and authorized original retry. Keep credentials out of source, browser state and logs.

Likely components: guest frontend/BFF, Order channel/fulfillment integration, Restaurant,
Catalog, Customer, Payment and provider adapter Infrastructure. Public domain/TLS, hosting,
delivery areas/fees, customer privacy and provider API access remain prerequisites; the
local QR test listener is not a public online-order deployment.

## 4A. Automated Alerts and Notifications

Use existing Notification delivery/inbox/outbox; design operational alert state/rule ownership
without replacing business-source ownership or duplicating the delivery service.

- Rules cover low stock, predicted ingredient shortages, unusual wastage/adjustments; refunds,
  voids, discounts, repeated payment/authentication failures; late/stuck Kitchen orders and
  printer/display failures; delivery/provider failures, failed workers, sync errors and other
  important operational faults. Predicted rules depend on sufficiently reliable insights data.
- Permissioned in-application center with informational/warning/critical severity; configurable
  thresholds, recipients, an open/acknowledged/resolved lifecycle, resolution details and creation/acknowledgement/
  resolution times. Acknowledgement does not repair or change the underlying business record.
- Durable rule/event identities, deduplication, grouping and cooldowns; bounded escalation
  for unresolved critical events. Prevent recursive notification-failure storms.
- Essential detection/escalation runs in approved server/worker/event processing, not an open
  cashier browser. Reuse source facts, correlation, immutable audit and Notification leased
  delivery/attempt/reconciliation semantics; outages retain retryable state.
- Email/push/other channels stay disabled until actually supported, configured and authorized,
  with recipient/privacy/consent and delivery-failure handling. No provider or message sending
  is authorized by this roadmap record. Customer displays never expose staff alerts or histories.

Likely components: source-event owners, Notification Application/Infrastructure, Reporting
read models, Authorization and portal/POS staff views. Tests cover scope denial, duplicate
events, cooldown, escalation, source/delivery outage, lease recovery and original delivery retry.

## 4B. AI-Powered Business Insights

- Assess usable historical sales, stock, recipes, costs and wastage before choosing a model.
  Build explainable statistical/rule baselines first; show insufficient/incomplete data rather
  than fabricate forecasts, costs or confidence. Use calendar/seasonal data only when available.
- Demand by item/day/time period, seasonality and known calendar factors; forecast quantities,
  assumptions, uncertainty/confidence where supported, and explainable contributing factors.
- Best-selling/declining dishes over configurable comparisons: quantities, revenue and margin
  only when cost data is present. Distinguish correlation from causation and paid, refunded,
  cancelled and creation-time measures; preserve branch calendar and financial completeness meaning.
- Recipe-driven ingredient needs, units/yields, existing/reserved stock and confirmed incoming
  deliveries; show likely shortages and suggested reorder quantities with explicit assumptions.
- Baseline/threshold-driven anomalies in refund, void, discount, wastage and sales patterns;
  expose authorized supporting facts/transactions. An anomaly is an investigation lead, not
  a confirmed allegation. Avoid sensitive staff/customer details in broad dashboard summaries.
- Existing-UI dashboard with date filters, summary metrics, trends, recommendations, freshness/
  completeness indicators and authorized drill-down. Record model/rule/input versions and
  as-of times for reproducibility; forecasts never become actual financial facts.
- External AI requires explicit justification/approval and data minimization. No unnecessary
  customer personal data, payment credentials or sensitive payloads leave the system. Predictions
  never automatically reprice menus, purchase supplies, refund payments or adjust stock.

Likely components: Reporting projections/APIs, authorized owning-service fact adapters,
Catalog recipes, Inventory and read-only portal insights. Forecast persistence/model execution
ownership is a Phase 1 decision. Tests use representative history, missing/stale data,
seasonality, temporal backtesting, uncertainty, unit conversions, privacy and permission checks.

## UI, testing and completion contract for every invoked slice

Reuse existing layout, navigation, authorization UI, design-system, validation, API client,
error handling and telemetry packages. Keep optional touch-first mode optional/default off,
support keyboard/mouse users and the Windows11-inspired POS design direction. Provide
loading/empty/success/error states, actionable messages, responsive tablet/mobile layouts
where supported, practical pagination and explicit destructive confirmations. No business
logic belongs in view components or client-authoritative totals.

Use existing xUnit unit/integration/architecture projects, Windows native fixtures, Vitest,
Playwright and owning PostgreSQL/broker acceptance patterns. Relevant commands include:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests
dotnet test tests/Architecture/NexaConnect.ArchitectureTests
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter <affected-boundaries>
dotnet build src/Clients/NexaConnect.POS
# From src/Frontend:
npm run check
npm test
npm run build
npm run audit:production
npm run audit:all
```

Select actual owning integration fixtures/environment from component READMEs; a generic
test invocation without its required database/provider is not proof. Run the relevant
browser/native tests and the matching source-bound regression gate. Include calculation,
authorization/tenant denial, transactions/concurrency, duplicate operations, token validation,
table conflicts, sync crash/recovery, alert dedup/retry, external adapter failures and
forecast-data tests. Clearly distinguish mocks/simulators from actual devices/providers.
Fix introduced regressions; never assert a test passed without an executed successful result.

Each completed slice must have working UI → Application/API → owner persistence where
required; live authorization/validation, failure/recovery behavior and relevant tests. Report
implemented features, changed files/modules, actual migrations/API contracts, executed
commands/results, configuration/environment requirements, unresolved dependencies and next
steps. Update relevant component/API/database/identity/deployment docs plus affected overview/
architecture sections, obtain the documentation-maintainer audit and inspect the final diff.
Keep increments reviewable/buildable; create commits or push only when explicitly authorized.

## Outstanding decision register

| Decision | Required before |
| --- | --- |
| Approved general file formats/purposes, retention, replacement/reference policy, need for local storage | General Media lifecycle rollout |
| Promotion priority/stacking, rounding, tax/service/refund allocation, voucher release and channel/offline eligibility | Promotion design and commercial schema/API changes |
| Exact fingerprint/card reader, interface, OS driver/SDK/licence and trusted staff-identity binding/fallback | Hardware SDK/adapters or real enrollment/login enablement |
| Table occupancy/party-size/shared seating/reassignment rules | Generic QR auto-assignment |
| Branch edge vs terminal-only authority, LAN Kitchen, offline cash/admission/stock limits and conflict rules | Offline transaction enablement |
| Pickup/delivery areas, fees/tax/privacy, actual providers/contracts/sandbox/webhook requirements | Public ordering and provider adapters |
| Alert owners, thresholds, escalation, authorized recipients/channels and consent | Operational alert/channel enablement |
| Historical completeness, recipes/units/cost/wastage and calendar coverage; any external AI approval | Forecast/margin/ingredient claims or external AI processing |

Unknowns are documented prerequisites. Implement ports, configurable policy and realistic
development simulators in the appropriate slice; keep unverified capabilities disabled.
Ask for clarification when an ambiguity would risk incorrect business behavior, security,
data loss or significant rework. This roadmap record itself adds no runtime defaults.
