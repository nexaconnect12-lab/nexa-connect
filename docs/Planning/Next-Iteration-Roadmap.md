# Next iteration: phases, checklist and implementation prompts

Prepared 21 October 2026 from the first-draft feedback and recorded agreements in
[the November plan](November-2026-First-Draft.md#next-iteration-roadmap).
This document defines planned work, not delivered product behavior. The phase-0
[baseline and execution assessment](Next-Iteration-Phase-0-Baseline.md) records the
completed planning assessment and deferred server/hardware prerequisites.
[Phase 1 source implementation and verification](../Deployment/Next-Iteration-Phase-1.md)
are recorded separately; [Phase 2 UI foundation](../Deployment/Next-Iteration-Phase-2.md)
and [Phase 3 direct Kitchen entry](../Deployment/Next-Iteration-Phase-3.md), plus
[Phase 4 staff/customer displays](../Deployment/Next-Iteration-Phase-4.md) and
[Phase 5 approved gallery/screensavers](../Deployment/Next-Iteration-Phase-5.md)
are implemented and verified in source. Interactive acceptance remains pending.
[Phase 6 hosting artifacts and guards](../Deployment/Next-Iteration-Phase-6.md) are implemented;
actual deployment/readiness remains gated by E01–E04.
[Phase 7 private EXE packaging](../Deployment/Next-Iteration-Phase-7.md) is implemented;
signed distribution and actual install/device acceptance remain pending.
[Phase 8 integration/freeze tooling](../Deployment/Next-Iteration-Phase-8.md) is implemented;
actual freeze remains conditional on its measured gates. Phase 9 remains pending;
no user acceptance is asserted here.

23 October 2026 priority update: the user deferred the remaining hosted release,
signing/device and consolidated-test work until server/device specifications are ready.
Keep those gates pending; do not mark the release frozen or accepted. The selected next
development track is [table QR ordering for iOS/Android browsers](QR-Ordering-Roadmap.md),
with anonymous guests, payment at the cashier and staff acceptance before Kitchen.
Its scope confirmation, [QR1 entry foundation](../Deployment/QR1-Entry.md) and
[QR2 menu/cart previews](../Deployment/QR2-Menu.md) are recorded;
[QR3 submissions/private status/staff rejection](../Deployment/QR3-Requests.md) are implemented;
[QR4 acceptance/price consent/original Order recovery](../Deployment/QR4-Acceptance.md) is implemented;
cashier collection remains pending.

The user considers the first draft sufficient to proceed. This does not fill its
pending detailed UAT results. Preserve the original candidate, records and deferred
cashier-session observation; create new release/UAT identities for changed inputs.

## Scope and execution order

Complete phases 0–8 before the user's next consolidated test in phase 9. Run automated
and developer verification in every implementation phase rather than delaying defect
detection until UAT. Do not expose an unfinished deployment to public traffic.
There is no committed completion date: phase 0 establishes estimates after server,
display and design discovery. Split any phase further if its verified change is too large.

| Phase | Deliverable | Depends on | Completion evidence |
| --- | --- | --- | --- |
| 0 | Baseline, scope and deployment/device assessment | None | Traceable checklist, constraints, prioritized execution plan |
| 1 | Cashier session reliability and authenticated idle resume | 0 | Active/idle/restart/reauthentication tests; preserved original work |
| 2 | Professional UI foundation for both portals and Windows POS | 0, 1 | Consistent layouts, accessible states and functional regression checks |
| 3 | Dedicated Kitchen entry and focused display | 2 | Direct URL/deep-link tests, ticket transitions, authorization checks |
| 4 | Staff and customer Windows POS displays | 1, 2 | Single transaction state, safe screen selection/hot-plug tests |
| 5 | Approved media gallery and distinct screensavers | 1, 4 | Scoped gallery configuration, cache/fallback and locked-display tests |
| 6 | Dedicated-server pilot deployment and operations | 0–5 | HTTPS, identity, service supervision, monitoring and restore evidence |
| 7 | Store packaging evaluation and install/update candidate | 1, 4–6 | Packaging decision, clean-PC authentication/update/recovery proof |
| 8 | Integrated hardening and release freeze | 0–7 | Clean source CI, joined regression, outage/restore and exact artifact inventory |
| 9 | User acceptance, fixes and release decision | 8 | Actual two-display, Kitchen, PDF and next-day observations; user decision |

Phases 3 and 4 may be implemented independently after their prerequisites. Phase 6
preparation can start after phase 0, but its deployed candidate must include phases 1–5.
Phase 7 completes the agreed evaluation and testable installer; Store certification
and public listing are external follow-ups, not prerequisites for the user's private UAT.

## Agreement checklist

Leave unchecked until the linked phase supplies verified evidence.

- [x] R01 — Direct `/kitchen` link within the existing Customer Portal, focused navigation,
  authorized branch/station selection, full-screen option; manager navigation retained.
  [Phase 3 source verification](../Deployment/Next-Iteration-Phase-3.md); actual device/user acceptance remains Phase 9.
- [x] R02 — One Windows PC, two displays: authorized staff/manager controls and a separate
  customer view following the same order and authoritative payment state.
  [Phase 4 source verification](../Deployment/Next-Iteration-Phase-4.md); physical two-monitor/hot-plug acceptance remains Phase 9.
- [ ] R03 — Idle locks instead of signing out; authenticate on the staff screen to resume
  the original cashier's shift, drawer and pending work. Explicit sign-out, revocation
  and absolute session expiry remain distinct. Phase 1, integrated in phases 4–5.
- [x] R04 — Both screens follow one lock state but show different content. No customer
  unlock controls or locked transaction details. [Phases 4–5 source verification](../Deployment/Next-Iteration-Phase-5.md); physical acceptance remains Phase 9.
- [x] R05 — Predefined approved product/menu gallery, staff-managed image order, optional
  product/promotional text, local image cache and neutral branded fallback.
  [Phase 5 source verification](../Deployment/Next-Iteration-Phase-5.md); real provider/device acceptance remains pending.
- [ ] R06 — Professional, consistent Product Owner, Customer Portal and Windows POS UI,
  including focused Kitchen and customer display; usability/accessibility checks. Phases 2–5, 9.
- [ ] R07 — Use the existing dedicated server/static IP first, with separate HTTPS portal
  origins, private infrastructure, off-server backups and operational supervision. Phase 6.
  [Artifacts and runbook](../Deployment/Next-Iteration-Phase-6.md) are available; measured
  hosted operation/reboot, off-server backup and restored-application proof remain pending.
- [ ] R08 — Evaluate Microsoft Store/MSIX versus signed EXE/MSI; verify installation,
  callbacks, dependencies and recovery-preserving updates. Phase 7.
  [Private EXE candidate and source verification](../Deployment/Next-Iteration-Phase-7.md)
  are prepared; actual VM/device and trusted-signing evidence remain open.
- [ ] R09 — Keep configuration/artifacts portable; define readiness and migration gates
  before later cloud migration. Phases 0, 6, 8; actual migration is follow-on C.
- [ ] R10 — Run a second consolidated user test after the next candidate is complete.
  Preserve unexecuted results as pending; fix and retest blockers. Phases 8–9.

QR ordering was selected on 23 October as the next separate development track; use
[the QR roadmap](QR-Ordering-Roadmap.md) and its confirmed guest/payment/approval rules.
It does not become part of the previously bound Phase 8 review candidate. Kiosk ordering
remains an unselected follow-on. Likewise menu-management expansion, onboarding UI, table transfers/split
bills, manual refunds, new providers, fiscal localization, physical peripherals and full
offline synchronization are proposed backlog, not silently included in this release.
If selected, give each its own contracts, invariants and acceptance plan; do not disguise
a new financial workflow as a UI task. Cached screensaver images are not offline checkout.

## Common instructions for every implementation prompt

Copy this block with the chosen phase prompt. Each phase authorizes only its defined
scope; do not run all phases from a request to implement one.

```text
Work in D:\DevOps\nexa-connect. Follow AGENTS.md and the implementation role guide.
Read README.md, AI/architecture/project_overview.md, the canonical architecture,
and the affected component/API/database/identity/deployment docs before changing behavior.
Inspect git status and preserve other edits and existing terminal recovery.

Use docs/Planning/Next-Iteration-Roadmap.md as the plan, not proof of delivered behavior.
Trace the current implementation first, reuse correct existing behavior, then make the
smallest coherent change. Preserve API/Application/Domain/Infrastructure boundaries,
service-owned persistence and versioned integration contracts. For state/financial work,
evaluate authorization, tenancy, invariants, idempotency, concurrency, transactions,
auditability, recovery and observability. Do not mutate another service's database.
New or materially changed HTTP/BFF/adapters use shared correlation/structured telemetry;
workers need equivalent safe JSON/OTLP. Never log credentials, bodies or payment data.

Add risk-appropriate tests and execute relevant checks. Update affected docs/component
READMEs and review both architecture summaries; update them only where affected. Run
documentation_maintainer as the final audit when available and resolve its findings.
Record phase evidence without checking off user/device observations that did not happen.
Report implemented behavior, tests, docs, architecture review, limitations and next work.
Do not publish to an external Store, buy hosting, migrate live data or expose public
services merely because this planning prompt mentions them; prepare a reviewable result
and use the authorization provided in the implementation session for external actions.
```

## Phase 0 prompt — Baseline and scope

```text
Prepare phase 0 of Next-Iteration-Roadmap.md; do not implement later phases.
1. Inventory the current source, verified first-draft artifacts and pending UAT records.
   Retain the cashier-session observation and distinguish user approval to proceed
   from unexecuted detailed acceptance cases.
2. Trace authentication, lock/resume, screen composition, Kitchen navigation, Media,
   installer and deployment behavior. Identify reuse, gaps and existing regression gates.
3. Assess server OS/CPU/RAM/storage, existing workloads, network, domains/TLS and backup
   destination; assess two-display resolution, touch needs and runtime prerequisites.
   Ask for unavailable environment facts without changing the retained test scope.
4. Establish a bounded UI direction from existing components/branding and define R01–R10
   acceptance/evidence. State open decisions, including resume authentication and gallery UX.
5. Create estimates/dependency checkpoints and a replacement-candidate/UAT strategy.
Exit: reviewable baseline and executable plan; unknown hardware/credentials remain
explicit prerequisites rather than invented values. Do not reset the first-draft data.
```

## Phase 1 prompt — Idle lock and session reliability

```text
Implement phase 1, R03, using the common instructions.
1. Reproduce or classify the deferred cashier timeout: idle lock, absolute expiry,
   active-use token renewal, dependency outage or other cause. Record unknowns honestly.
2. Idle must show a screensaver-style lock without closing the shift/drawer or deleting
   work. Use supported identity authentication for resume; do not invent a shared PIN
   or accept silent SSO as proof of a fresh operator authentication when that is required.
3. Only the original authorized cashier resumes retained work. Handle wrong identity,
   cancelled/failed authentication, revocation and absolute expiry distinctly and safely.
4. Keep pending operation IDs and protected recovery across lock/restart; reconcile
   ambiguous checkout through its original command. Review active-use renewal behavior.
5. Test idle/activity boundaries, renewal, restart while locked, failed/wrong-user resume,
   expired/revoked sessions and duplicate-effect prevention. Supply a manual test checklist.
Exit: measured session behavior and usable authenticated resume, without weakened
authorization or any claim that automated tests establish Windows user acceptance.
```

## Phase 2 prompt — Professional UI foundation

```text
Implement phase 2, R06, using the common instructions.
1. Apply the phase-0 UI direction to Product Owner, Customer Portal and WPF POS using
   their established frameworks; share design principles without forcing a framework rewrite.
2. Align navigation, typography, spacing, validation, focus, loading/empty/error states,
   touch targets and readable totals. Clearly show session and uncertain-operation states.
3. Keep platform-owner and tenant/customer authority separate. Preserve existing functional
   screens, authorization boundaries and financial semantics; cosmetic changes add no grants.
4. Prepare reusable layouts for Kitchen, customer display and screensaver without pretending
   their later features are complete. Cover target resolutions and keyboard/accessibility use.
5. Build all affected clients; run functional regression and verify actual rendered layouts.
Exit: consistent, usable core UI with a clear inventory of any remaining design decisions.
Do not silently add table management, refunds, onboarding workflows or new financial rules.
```

## Phase 3 prompt — Dedicated Kitchen entry

```text
Implement phase 3, R01, using the common instructions.
1. Add /kitchen as a direct route/deep link in the existing Customer Portal and deployment.
   Preserve the destination through login and refresh without creating a second ticket store.
2. Provide focused navigation, authorized branch/station selection and full-screen controls;
   keep entry through manager navigation and an understandable exit path.
3. Reuse owning Kitchen lifecycle commands, version checks and live scope authorization.
   Clear stale tickets on scope/sign-out changes; handle connection loss without false success.
4. Test direct unauthenticated/authenticated loads, refresh, denied roles/tenants,
   station filtering, legal transitions, duplicate/conflicting requests and full-screen fallback.
Exit: the same authorized queue works from a direct link and portal navigation. Browser
tests do not claim physical touchscreen acceptance; retain that phase-9 checklist.
```

## Phase 4 prompt — Two Windows POS displays

```text
Implement phase 4, R02 and the display portion of R04, using the common instructions.
1. Add a dedicated customer window in Windows extended desktop with explicit staff/customer
   monitor selection. The staff window retains existing role-checked operational controls.
2. Drive both windows from the same current transaction. Show customer-safe items,
   quantities, authoritative prices/charges/total, tender and confirmed/pending outcome.
   Do not infer payment success or create a second checkout pipeline.
3. Hide internal controls, staff identity/secrets and unrelated orders from the customer view.
   On lock, completion, cancellation and sign-out, apply deliberate content clearing rules.
4. Handle monitor disconnect/reconnect, restart and single-screen fallback without moving
   staff content onto a customer monitor. Customer input cannot unlock staff controls.
5. Test presentation/state mapping and financial noninterference; supply the actual
   two-monitor/hot-plug checklist for phase 9.
Exit: synchronized, isolated display roles with original operation/recovery identities intact.
```

## Phase 5 prompt — Gallery and screensavers

```text
Implement phase 5, R04–R05, using the common instructions.
1. Reuse the existing Media bounded context and supported validation/scanning lifecycle.
   Provide an authorized configuration flow selecting approved product/menu images,
   order and optional names/promotional text, scoped to the intended organization/branch.
2. Document ownership and versioning of the selection separately from Media asset ownership.
   Deliver only allowed assets/configuration to the terminal; do not embed privileged tokens.
3. Show different slideshows on two screens under one lock state. Staff retains visible
   Resume/Authenticate; customer sees only approved content and no transaction/unlock data.
4. Cache validated assets locally with bounded size and explicit refresh/removal semantics;
   address expired access, revoked/deleted selections and interrupted downloads. Use a
   neutral branded fallback if assets are unusable. Never block locking/resume on media.
5. Test scoping, ordering, malformed media, offline cache/fallback, content removal,
   authentication non-bypass and original-work recovery.
Exit: configurable screensavers with safe cache behavior; no claim of offline sales support.
```

## Phase 6 prompt — Own-server hosted pilot

```text
Implement phase 6, R07 and migration portability for R09, using the common instructions.
1. Use the phase-0 server facts to prepare reproducible deployment artifacts for the
   dedicated server; do not assume its OS or reuse the local Development fixture as production.
2. Configure separate HTTPS portal origins, identity issuer/clients/callbacks, public POS
   endpoints, secure cookies, persistent BFF keys/sessions and least-privilege secrets.
3. Keep databases/broker/cache/storage private, restrict administration, preserve database
   ownership and support proper service supervision, reboot startup and dependency ordering.
4. Provide safe migrations, readiness, correlation/metrics, actionable alerts, resource
   limits, off-server backups and an isolated restored-application drill. Include identity,
   configuration and required infrastructure recovery; do not certify it from SQL dumps alone.
5. Verify the candidate in isolated staging, including remote POS and Kitchen access,
   restart, TLS/login, denial boundaries and outage behavior. Prepare cutover/support
   instructions; deploy externally only within the authorization of this phase's session.
Exit: measured hosted-pilot readiness and a reproducible operating guide, with remaining
environment blockers explicit. Preserve the first-draft records and protected terminal state.
```

## Phase 7 prompt — Store evaluation and installation

```text
Implement phase 7, R08, using the common instructions.
1. Recheck current Microsoft Store eligibility, packaging policies and costs using official
   sources. Evaluate MSIX against signed EXE/MSI and record the compatibility decision.
2. Build a private testable package for the selected approach with explicit runtime
   dependencies and safe per-terminal onboarding; no local draft secrets or fixed IP binding.
3. Verify browser callback registration/activation and current-user data paths. Define
   migration from the current unpackaged client without deleting/transplanting DPAPI state
   or exposing unrelated scope. Treat unsupported migration as a blocker, not a reset shortcut.
4. Test install/reinstall, restart, supported upgrade, interrupted update, schema
   compatibility and uninstall data semantics. Preserve pending cashier work and receipts.
5. Document update timing/control for active shifts and prepare publisher/listing/privacy
   requirements. Do not create accounts or submit/publish without session authorization.
Exit: evidence-backed packaging decision and a private installer candidate for phase 9;
external Store certification/listing remains separately tracked and need not delay UAT.
```

## Phase 8 prompt — Integration and release freeze

```text
Implement phase 8, R09–R10 release preparation, using the common instructions.
1. Review R01–R09 and phase evidence against actual code/configuration. Close scope gaps
   and split proposed extra features out rather than silently marking them delivered.
2. Run appropriate builds, architecture/unit/frontend/persistence/authentication and joined
   financial gates. Verify checkout→Kitchen→cash close→day settlement→ledger/CSV invariants,
   retries, concurrency, denied scopes, broker/dependency outages and recovery.
3. Exercise hosted supervision/reboot and isolated full-graph restoration with separate
   terminal-recovery checks. Measure resource/latency targets agreed in phase 0.
4. Fix blockers, commit a coherent candidate when authorized, obtain exact-source clean CI
   and bind artifacts/configuration/schema identities to a new release and UAT session.
5. Deliver the installer, operator runbook and R01–R10 test script with pending manual
   cases explicit. Keep public Store/cloud migration external milestones separate.
Exit: reproducible candidate ready for the user's second test; no invented manual passes.
```

## Phase 9 prompt — Consolidated second user test

```text
Run phase 9, R10, using the common instructions; support the user rather than fabricate
their observations. Use the phase-8 frozen candidate on the dedicated-server pilot.
1. Supply and record a concise script for Product Owner/Customer role isolation, direct
   Kitchen access, cashier lock/resume/renewal, both displays, gallery cache/fallback,
   install/update, cash/fictional PromptPay, Kitchen, PDF, cash variance and denied actions.
2. The user operates the actual two-monitor PC, inspects saved PDFs and checks screen
   disconnect/reconnect. Observe recovery through original commands without duplicate money.
3. Keep the same environment through actual Singapore midnight, then test day close,
   settlement receipt/restart, ledger and bounded CSV. Never substitute clock changes or
   historical fixtures for the real next-day observation.
4. Record each result and safe evidence, distinguish measured automation from user reports,
   and classify defects. Code/configuration fixes require matching candidate evidence,
   replacement binding and affected retests; retain traceability to earlier observations.
5. Produce a final report with blockers, limitations and proposed follow-up priorities.
   Leave acceptance to the user; unresolved financial/security/data-loss/recovery blockers
   prevent a readiness claim. Do not enable unimplemented refunds or real provider transfers.
Exit: documented second-test outcome and user decision, or explicit blockers and retest plan.
```

## Follow-on prompts outside the core second-test scope

### A — QR ordering, selected 23 October 2026

The [QR ordering roadmap](QR-Ordering-Roadmap.md) specializes this original follow-on
prompt with the user's confirmed rules and implementation slices. It records outstanding
Phase 6–9 readiness separately; selecting QR does not waive those release gates.

```text
Scope QR ordering as a separate follow-on under the common instructions.
First define guest authority, table/branch binding, ordering/payment timing and abuse limits.
Then implement a mobile menu/cart and secure table QR entry with server-authoritative
prices/availability, safe guest access to only its own order and idempotent submissions.
Start with payment at the cashier: implement an explicitly authorized handoff to the
owning order/payment workflow rather than assuming staff-only APIs support guest orders.
Reuse Kitchen and Inventory workflows; never treat scanning as payment confirmation.
Test QR tampering, cross-tenant/order access, stale prices/stock, retries and response loss.
Deliver a separate candidate, deployment guidance and actual-phone acceptance checklist.
Provider online payment and offline guest ordering are separate scope decisions.
```

### B — Kiosk ordering, if selected after A's reusable ordering foundations

```text
Scope kiosk ordering as a separate follow-on under the common instructions.
Agree target hardware/OS, device enrollment, accessibility, payment-at-cashier versus
provider/device integration, printer needs and outage policy before implementation.
Reuse menu/cart components and owning contracts while adding a kiosk-scoped device identity,
touch layout and secure reset between customers. Kiosk inactivity clears the guest session;
do not reuse the staff screensaver rule to expose a previous guest's order to the next guest.
Preserve any submitted/uncertain order for authorized reconciliation without duplicate charge
or disclosure. Prevent access to staff controls, other guests and device administration.
Test guest transitions, retries, unattended restart, device revocation and chosen peripherals.
Deliver a separately bound release and actual shared-device user acceptance; no assumed
provider/card or physical-device certification from browser tests.
```

### C — Cloud migration after readiness

```text
Plan cloud migration only after the user accepts the UI/service readiness checkpoint.
Use the common instructions. Measure existing workload, agree availability/recovery
objectives and compare current provider costs before selecting a cloud topology.
Rehearse migration of service-owned data, identity, secrets/configuration, media and
required broker/session state in isolation. Verify restored application behavior and
financial invariants, POS endpoints/callbacks and gallery/cache behavior.
Define DNS cutover, quiescence/draining, source-of-truth fencing, reconciliation and a
failure plan that cannot run competing writers or roll back immutable financial history.
Do not copy protected Windows terminal recovery between devices/accounts.
Prepare artifacts and a reviewable cutover plan; execute purchases/live migration only
with explicit session authorization. Run separate post-migration user acceptance.
```

## Documentation and maintenance

This plan changes no runtime, contract, database or deployed topology. Project overview,
canonical project architecture and restaurant architecture were reviewed; implementation
claims remain unchanged. Update those documents in the phase that changes their subject.
Maintain this plan's evidence links and checklist during execution without erasing the
historical first-draft roadmap or marking planned capabilities as delivered.
