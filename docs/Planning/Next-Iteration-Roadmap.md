# Next iteration: phases, checklist and implementation prompts

29 October 2026: [POS UX8 drawer preparation](../Deployment/POS-UX8-Cash-Drawer.md) is
implemented in source with a disabled staff panel, isolated workflow and local intent-store
tests. **UX8 remains incomplete**: no physical driver, production payment hook or server
opening authorization/audit is enabled. The user reports equipment available and printer
information PAISHUO / USB / 80 mm / 200 DPI; the exact model and drawer connection/command
are unconfirmed (not connected or unknown). Complete the documented hardware and security
gates before activation. This supersedes the earlier not-yet-delivered equipment status;
actual resolution, touch and peripheral acceptance remain unverified.

Additional drawer listing supplied 29 October: **PAISHUO 4141P**. The drawer model is now
recorded; exact printer model, drawer connector/electrical protocol and supported opening
command remain pending. See [UX8 equipment facts](../Deployment/POS-UX8-Cash-Drawer.md#equipment-facts-and-prerequisites).

28 October 2026: the user confirmed purchase of an **S200D dual-screen Windows POS**:
Windows 11, Intel Core i5 (5th generation), 8 GB RAM, 128 GB SSD, 15.6-inch main
staff screen and 11.6-inch extended customer screen. The unit is described as a
touch dual-screen machine with Wi-Fi and Bluetooth, including a cash drawer,
80 mm roll-paper POS printer and barcode scanner. See the updated
[equipment prerequisites E05–E06](Next-Iteration-Phase-0-Baseline.md#environment-prerequisites-and-decision-register).
Resolution update, 28 October 2026: the seller's website lists **`1366x76.8 / 1920x1080`**.
The user clarified that the purchased POS has not arrived and its actual specifications
cannot yet be checked. They believe the maximum may be `1920×1080`; this is unverified.
`1366×768` is only a possible correction of the seller's first value. Neither actual/native
resolution nor assignment to the 15.6-inch staff versus 11.6-inch customer screen is
confirmed. Do not treat seller-listed options or a possible maximum as measured Windows
settings, the delivered configuration or hardware acceptance.
Windows scaling/DPI, which screens support touch, OS edition/architecture and
peripheral models/interfaces/drivers still require confirmation. Purchase does not
establish physical display or peripheral integration acceptance.

26 October 2026: the user confirmed purchase of **`usenexaconnect.com` at Cloudflare
Registrar**. This is the domain for upcoming dedicated-server/QR10 planning and later
cloud migration. DNS/subdomain assignments and trusted HTTPS are not yet configured or
verified; remaining server prerequisites are unchanged. See
[hosting prerequisites](../Deployment/Next-Iteration-Phase-6.md).

[QR7 local pilot preparation](../Deployment/QR7-Pilot.md) follows QR6. Its initially
unconfirmed LAN/certificate facts are supplied and checked before explicit activation;
phone/POS/PDF results require actual operator observations. Dedicated-server,
signing/hardware/clean-CI/freeze phases remain deferred and unchanged.

The selected [native POS Orders list](../Deployment/POS-Order-List.md) shows all configured-
branch Orders with a QR filter and eligible original-Order selection. This source update
removes manual UUID entry without changing acceptance, custody, payment or release gates.

[QR6 integration/security/candidate preparation](../Deployment/QR6-Integration.md) is implemented in source. Patched Zod and fresh local evidence replace the QR5 security blocker once verified; actual hosted/iOS/Android/WPF/PDF/clean-CI/signing/user acceptance remains pending. The existing Phase6–9 specification-dependent work is still deferred.

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
[QR5 original cashier collection/payment](../Deployment/QR5-Collection.md) is implemented; QR6 integration/hosted/phone acceptance remains pending.

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

## Long-term POS product design principles

[POS UX7 — terminal setup and peripheral diagnostics](../Deployment/POS-UX7-Terminal-Setup.md)
adds one staff setup overview, explicit read-only reachability/device discovery, navigation
to existing tests and a sanitized support snapshot. It preserves input/printing/scanning
defaults and separates configuration/detection from actual hardware/UAT observations.
Physical scanner/printer/displays, saved PDF, phone and long-lock acceptance remain pending.

[POS UX6](../Deployment/POS-UX6-Barcode-Scanning.md) adds Catalog-owned versioned branch
barcodes, organization-wide product uniqueness, staff management and scan entry that defaults
off on startup. Its running-window opt-in may remain checked across lock/sign-out, while
input/pending lookup clears. Live lookup reuses original cart/pricing guards; supported scanner text/Enter
cannot approve POS-owned confirmations. Model/interface/layout/suffix and actual operator/
physical acceptance remain pending. Drawer, weighted/GS1, vendor protocols, offline lookup
and card authentication remain separate future slices.

[POS UX5 — printer setup and 80 mm receipts](../Deployment/POS-UX5-Receipt-Printing.md)
adds saved Windows queues, optional roll layout and labelled synthetic test output.
Read-only sales/refund reprints retain live authorization and payment custody. Actual PDF
inspection and purchased-printer model/driver/paper acceptance remain explicit pending
checks; drawer/scanner and card-reader integration remain separate future work.

[POS UX4 — continuous locked screensaver playback](../Deployment/POS-UX4-Locked-Gallery.md)
adds a separate expiring gallery-only reader with live delegated authorization. It preserves
five-minute image withdrawal and cashier lock/resume protection. Implementation and automated
verification are distinct from local activation, ten-minute locked playback and physical
dual-screen acceptance, which must be recorded when observed.

The [POS UX3 density slice](../Deployment/POS-UX3-Display-Density.md) adds Standard (default)
and Touch layouts independently of the optional keyboard setting, with responsive staff
screens. Physical display/scaling and full operator acceptance remain pending.

The next bounded implementation, [POS UX2 — cashier workflows/UI refinement](../Deployment/POS-UX2-Cashier-Workflows.md),
adds pinned original actions and amount provenance, guarded next-step navigation, clearer
menu states and touch styling. It preserves financial authority and leaves physical and
end-to-end operator acceptance pending; it does not deliver card-reader authentication.

The first bounded implementation is [POS UX1 — touch-first input foundation](../Deployment/POS-UX1-Touch-Input.md):
docked text/numeric keyboards, reason shortcuts and Windows keyboard launch for browser
sign-in. This supplies source behavior; physical touch/scanner/OIDC acceptance and the
broader card-first/design principles below remain pending.

Recorded 28 October 2026 at the user's request. These principles guide future POS
UI/UX decisions and feature development. They do not mark capabilities as implemented,
expand the current release scope or authorize immediate implementation of every item.
Apply them when selecting and designing future slices, alongside the existing security,
ownership and release gates. Retain existing roadmap items and their acceptance status.

### Keyboard-less, touch-first operation

28 October 2026 clarification: touch-first operation is **optional**, not mandatory.
Default POS operation uses keyboard and mouse. **Terminal & sync → Input settings**
offers a saved touch-first opt-in; see [input modes](../Deployment/POS-Input-Mode.md).
Keyboard-less requirements below apply to the selected touch mode. Preserve effective
keyboard/mouse/scanner operation in both modes and do not require every customer to use touch.

Design the optional touch mode to work without a physical keyboard or mouse. Minimize
manual typing: prefer touch-friendly buttons, dropdowns, searchable lists and predefined
options over free-text fields. Use compatible barcode scanners, card readers and other
supported peripherals where appropriate to reduce entry effort.

In touch mode, provide an on-screen keyboard whenever text entry is required, including necessary
sign-in and recovery flows. Show it when needed, keep the focused field and important
controls visible, and avoid obstructing totals, validation feedback or confirmation
actions. Optimize touch targets, focus, scrolling and dismissal for touchscreen use.
Preserve keyboard accessibility and supported scanner input without making a physical
keyboard necessary for normal cashier workflows.

### Windows 11-inspired visual direction

Use Windows 11 as a design reference for a clean, modern and consistent POS interface:
clear typography, appropriate spacing, rounded corners and intuitive visual hierarchy.
Keep colors, icons, buttons, dialogs and navigation patterns consistent throughout the
application. Retain a distinct POS-focused interface optimized for retail operations,
readability, usability and fast cashier workflows.

Layouts must remain responsive across touchscreen sizes, resolutions and DPI settings.
Use the purchased 15.6-inch staff and 11.6-inch customer displays as physical acceptance
targets once their resolutions and touch capabilities are confirmed. This direction
refines the provisional Phase 2 styling; framework, component-library and visual-token
choices require a scoped design review before implementation.

### Card-based cashier sign-in

Plan compatible identification-card authentication so cashiers can sign in by presenting
a card instead of relying primarily on username/password entry. Identify the cashier
through a validated authentication flow and provide clear success/failure feedback.
Handle invalid or unregistered cards, repeated scans and reader failures gracefully,
with an appropriate fallback authentication method when a card cannot be used.

Apply the security, access-control and retained-work ownership requirements in
[optional add-on D](#d--optional-add-on-cashier-card-reader-access). Card presentation
must not bypass authorization, original-cashier resume restrictions or separate manager
approvals. Card-based staff access is distinct from payment-card processing. The add-on's
commercial scope, reader selection and delivery timing remain undecided.

### Principles for every future POS feature

1. **Minimal typing:** reduce manual text entry wherever possible.
2. **Optional touch-first operation:** default to keyboard/mouse operation and offer
   configured touchscreen workflows without dependence on a physical keyboard or mouse.
3. **Fast cashier workflows:** minimize steps in common tasks while retaining necessary
   financial confirmations and authorization.
4. **Consistent UI:** follow the Windows 11-inspired direction across POS screens.
5. **Secure authentication:** make sign-in quick while preserving appropriate security.
6. **Reliability:** handle peripheral failures and exceptional cases with clear recovery
   and fallback paths that preserve orders, shifts and payments; unavailable authentication
   or peripherals must never silently bypass required controls.

### Decisions and acceptance planning before implementation

| Area | Open question or technical decision |
| --- | --- |
| On-screen keyboard | Choose Windows touch-keyboard integration or an application keyboard; verify numeric/text entry, required languages/layouts, invocation/dismissal and behavior in external identity-provider windows. Keep authentication entry on the staff display and avoid recording sensitive input. |
| Display and accessibility | Confirm resolution, DPI and touch support per purchased screen; agree touch-target sizing, contrast, scaling and keyboard-occlusion acceptance cases. |
| Visual system | Review a representative cashier workflow and agree typography, spacing, colors, icons, dialogs and reusable components before applying the direction broadly. |
| Workflow efficiency | Identify frequent tasks and baseline their steps and typing; include cashier usability checks for improvements without removing necessary safeguards. |
| Card and reader | Select card technology, reader/interface and Windows driver support; assess clone/replay resistance and whether a PIN or stronger authenticator is required. A card identifier alone is not proof of authority. |
| Identity lifecycle | Decide enrolment, cashier-to-card binding, replacement/revocation, failed-attempt limits, repeated-scan handling, safe audit events and identity-provider integration. |
| Fallback and outages | Select an authorized fallback usable without a physical keyboard; define lost-card, reader disconnect/reconnect, identity outage and offline behavior without bypassing authentication or changing retained ownership. |
| Peripheral acceptance | Confirm printer, drawer and scanner models/interfaces; test scanner input versus text entry, repeated scans and device failures on the actual Windows POS. Card reader purchase/support remains unconfirmed. |

Future selected implementation slices should include relevant touch-only end-to-end
checks, on-screen-keyboard visibility/occlusion checks, actual-device usability and
peripheral failure/recovery cases. Card sign-in additionally requires authorization,
invalid/unregistered/revoked-card, repeated-scan and fallback tests. Keep unexecuted
hardware and user observations pending rather than inferring acceptance from source tests.

## Common instructions for every implementation prompt

Copy this block with the chosen phase prompt. Each phase authorizes only its defined
scope; do not run all phases from a request to implement one.

```text
Work in D:\DevOps\nexa-connect. Follow AGENTS.md and the implementation role guide.
Read README.md, AI/architecture/project_overview.md, the canonical architecture,
and the affected component/API/database/identity/deployment docs before changing behavior.
Inspect git status and preserve other edits and existing terminal recovery.

Use docs/Planning/Next-Iteration-Roadmap.md as the plan, not proof of delivered behavior.
Apply its long-term POS product design principles to relevant future POS work; implement
only the selected slice, with explicit scope and acceptance for any new capability.
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

### D — Optional add-on: cashier card-reader access

Recorded 26 October 2026 at the user's request as an **optional add-on service**, outside
the current QR9/QR10 scope. No implementation or hardware purchase is authorized by this record.

- Staff-card sign-in and authenticated resume of the Windows POS lock screen.
- Evaluate NFC/RFID badge plus personal PIN or a compatible FIDO2 card/authenticator;
  reader/card selection and the authentication design remain unconfirmed.
- Preserve the original cashier's shift, drawer and pending-payment ownership, existing
  branch permissions and separate manager approvals. A card number alone grants no authority.
- Include card enrolment, lost-card revocation, failed-attempt limits, audit records and
  actual Windows reader/card acceptance testing when this add-on is selected.
- Pricing, delivery timing and offline behavior remain undecided.
- The 28 October [long-term POS principles](#long-term-pos-product-design-principles)
  establish the desired card-first cashier experience, clear success/failure feedback,
  invalid/unregistered-card and repeated-scan handling, reader-failure recovery and an
  appropriate keyboard-less fallback. They guide this add-on without marking it delivered.

## Documentation and maintenance

This plan changes no runtime, contract, database or deployed topology. Project overview,
canonical project architecture and restaurant architecture were reviewed; implementation
claims remain unchanged. Update those documents in the phase that changes their subject.
Maintain this plan's evidence links and checklist during execution without erasing the
historical first-draft roadmap or marking planned capabilities as delivered.
