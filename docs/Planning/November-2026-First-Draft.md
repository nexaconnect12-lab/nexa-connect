# November 2026 first draft: implementation slices and prompts

Slice 8 is release preparation and acceptance execution using the existing Slice 7 tools.
Its [evidence and outstanding blockers](../Architecture/Evidence/First-Draft-Slice-8.md)
record candidate CI, retained environment/package preparation, corrective changes and
the pending installation and real user drill. This adds no business-feature scope.

Slice 7 implements [acceptance recording and the solo tester handover](../Deployment/First-Draft-UAT.md), with [executed tooling evidence and explicit pending gates](../Architecture/Evidence/First-Draft-Slice-7.md). Actual Windows/PDF, real next-day testing and the user's final acceptance remain pending.

Slice 6 implements the [local candidate gate and isolated restore drill](../Deployment/First-Draft-Release.md). Its [executed evidence](../Architecture/Evidence/First-Draft-Slice-6.md) distinguishes local input/build/schema identity and recovery verification from clean-checkout CI, restored application-graph disaster recovery and pending Windows/PDF/real next-day user acceptance.

Slice 5 adds [manager financial acceptance](../Deployment/First-Draft-Manager.md), combining normal-clock cash/PromptPay reconciliation with guarded historical Singapore settlement/CSV checks. Executed status is in [slice 5 evidence](../Architecture/Evidence/First-Draft-Slice-5.md); the real next-day solo drill and Windows/PDF acceptance are still distinct gates.

Slice 4 implements [Windows packaging, recovery preflight and terminal admission](../Deployment/First-Draft-Windows-POS.md). Executed automated checks and pending interactive WPF/PDF gates are tracked in [slice 4 evidence](../Architecture/Evidence/First-Draft-Slice-4.md); installation or backend passes alone do not establish device acceptance.

Slice 2 implements [scoped draft provisioning and operational selectors](../Deployment/First-Draft-Provisioning.md). Local evidence is tracked in [slice 2 verification](../Architecture/Evidence/First-Draft-Slice-2.md). Slice 3 implements [joined checkout and recovery](../Deployment/First-Draft-Checkout.md), with executed status tracked separately in [slice 3 evidence](../Architecture/Evidence/First-Draft-Slice-3.md). Slice 4 device checks and the Slice 5 real next-day drill remain open. Slice 6 supplies release hardening with execution status recorded separately; Slice 7 remains UAT and handover.

Prepared on 14 October 2026 from the current repository. The user selected one branch with Windows POS, online cash/manual PromptPay, the kitchen queue and manager portal. This is a controlled test draft, not production certification.

Slice 1 implements the [owned local stack lifecycle](../Deployment/First-Draft-Test-Stack.md), manual-tender defaults, explicit local Redis sessions, financial-host liveness and user-scoped RabbitMQ health probes. Its local verification and remaining gates are tracked in [slice 1 evidence](../Architecture/Evidence/First-Draft-Slice-1.md). This does not provision slice 2's branch/menu/role records or establish remote/production acceptance.

Target milestones: baseline and scope by 16 October; first staff testing from 11 November; feature freeze on 20 November; full user acceptance testing from 23 November; accepted draft or explicit remaining blockers by 30 November. Dates are planning targets, conditional on access to a Windows terminal, test environment, test identities, printer and staff. Budget roughly one focused implementation owner for 27 working days before the freeze (two baseline days plus 25 implementation days), plus staff participation and the final acceptance week. Re-estimate after the baseline gate; do not compensate for delays by weakening financial or authorization checks.

Slice 0 verification was executed on 14 October against commit `7e91480429838f092093b4b142a2b0a64cce4c9f`. See the [baseline report, prioritized backlog and acceptance matrix](November-2026-Slice-0-Baseline.md). Clean solution/WPF build, both independent clean BFF publishes, unit/architecture and frontend checks passed. Current-commit CI has nine successful workflows and one end-of-day infrastructure failure with a RabbitMQ `filesystem_permission` diagnostic category. Live joined and physical acceptance remain subsequent gates. Dates remain provisional pending host/device/staff prerequisites.

## Review findings

The original planning review inspected architecture and component documentation, the file/project inventory, selected Order pricing/workflow and Kitchen Application code, POS XAML and hardware abstractions, frontend scripts, migration and development launchers, and historical CI evidence. That initial review did not execute the application or test suites, or retrieve current remote CI, and was not a complete security or financial code audit. Slice 0 subsequently executed clean-source checks and retrieved exact-commit CI results; the [baseline report](November-2026-Slice-0-Baseline.md) records current verification and remaining live/physical gates.

| Area | Existing foundation | Draft work |
| --- | --- | --- |
| Identity and tenancy | Separate BFF sessions, POS PKCE, product authorization and branch ownership | Provision restricted test roles; prove cross-tenant denial and revocation in the joined draft |
| POS and checkout | WPF menu/cart, authoritative THB pricing, cash/manual PromptPay settlement, protected recovery, shifts, cash review and immutable ordinary receipts | Package a repeatable terminal setup; verify printer, lock/restart and uncertain-command behavior |
| Kitchen | Online ticket queue and versioned transitions through Customer BFF | Joined real-identity POS-to-Kitchen acceptance; usable branch/station selection |
| Inventory | Durable stock reservation/release and duplicate protection | Verify checkout/compensation effects in the joined flow; do not imply recipe costing or wastage management |
| Reporting and day close | Sales/tender projections, preparation, seals, approval, online durable settlement, cash corrections, settlement ledger and bounded CSV export | Deploy compatible consumers before producers; verify complete selected evidence and operator usability |
| Operations | Numerous disposable acceptance runners, safe evidence and telemetry | One stable test-stack lifecycle, current deployment instructions, recovery drill and current CI evidence |
| Offline and hardware | SQLite recovery and cash-movement replay; generic Windows receipt printing | Full offline ordering/KDS and device-specific peripheral adapters remain outside this draft |

Concrete risks found:

1. `scripts/run-checkout-development.ps1` and `scripts/run-phase8-development.ps1` cover different application graphs. The Kitchen contract explicitly warns that the Phase 8 launcher does not start Kitchen and requires matching BFF dependency addresses. A coordinated test host is necessary.
2. Slice 0 found older versions presented as current in deployment summaries. Slice 1 corrects current guidance to compatibility `0.38.0`, Order 19, Payment 18, POS 23, Reporting 22 and Authorization 15 while preserving historical introduction versions. Resolve future targets from the selected commit's actual catalog rather than copying introduction boundaries.
3. Kitchen browser tests use synthetic BFF responses. The Kitchen runbook leaves joined live POS/Kitchen acceptance open. Ordinary payment-failure compensation and explicit pre-payment cancellation have different paths; evaluate both before making a safety claim. In particular, completed kitchen work cannot simply be undone or treated as an unpaid sale cancellation.
4. `OrderPricingService` restricts quotes to THB. Customer branch creation defaults to SGD and Asia/Singapore; slice 2 must explicitly choose THB and the confirmed Singapore timezone. Slice 0's checked-in POS test-card/workstation-path mismatch is resolved in slice 1: default cash, disabled test card and empty QR path, with explicit generated manual-tender settings.
5. POS receipt refunds currently cover captured provider payments and explicitly exclude manual cash/PromptPay settlements. This selected payment scope has no implemented manual-tender refund workflow. Staff must not use a stock release, negative cash movement or late correction as a substitute for a refund.
6. The clean-checkout CI document records remaining setup/broker investigation and remote rerun needs. It is historical evidence, not a live Actions status check. Obtain fresh evidence during slices 0 and 6.

Primary sources: [project overview](../../AI/architecture/project_overview.md), [system architecture](../Architecture/Project-Architecture.md), [restaurant architecture](../Architecture/Restaurant-POS-Architecture.md), [integration matrix](../Architecture/Phase-10-Product-Integration.md), [Kitchen contract](../API/Kitchen-Queue.md), [POS README](../../src/Clients/NexaConnect.POS/README.md), [clean-checkout evidence](../Deployment/CI-Clean-Checkout-Verification.md), and [deployment guide](../Deployment/Deployment-Guide.md).

## Draft scope and boundaries

Use one organization, restaurant and branch, THB, a deliberately configured business timezone, one Windows cashier terminal and a browser kitchen screen. Provision cashier, kitchen operator, manager and read-only accountant identities, plus a separate tenant for denial tests. Use a small representative menu and existing station codes. Staff can authenticate, open a shift/drawer, select items, confirm the server quote, place and settle an order, progress kitchen tickets, retrieve/reprint receipts, reconcile/close cash, review variance and use the manager day-close/reporting flow.

The user confirmed a Windows PC, a PDF printer and testing in Singapore, with the user as the sole first-draft tester. Configure the test branch timezone as `Asia/Singapore`. Use the PDF printer through the existing Windows print path; inspect generated receipt PDFs and verify print/reprint and cancelled/failed print recovery. Physical receipt-printer, drawer and scanner acceptance is deferred beyond this draft. Test currency remains THB because the current quote contract requires it; testing in Singapore does not enable SGD pricing or imply local fiscal compliance. Slice 1 provides the isolated local stack on the Windows PC; remote hosting remains outside this local verification.

The user exercises cashier, Kitchen operator, manager and accountant roles through separate least-privilege test identities. Use isolated browser profiles/sessions and automated coordinated requests for competing-operator/manager cases; do not replace those tests with one unrestricted account. Independent staff feedback is deferred. The November dates remain targets; the user's specific test sessions are not yet scheduled.

Manual PromptPay remains explicit human receipt verification with a reference; it is not automated bank reconciliation. Use test references and test funds only where separately authorized. Ordinary receipts are not fiscal invoices. Day close accepts completed branch-local dates, so exercise a prior completed date through existing guarded acceptance infrastructure and a real next-day staff drill; do not add a production clock override.

Exclude new QR/kiosk/mobile clients, full branch-edge offline ordering, provider card onboarding, automatic PromptPay confirmation, manual-tender refunds, split bills, promotions, modifiers, table transfer/merge, recipe inventory, fiscal compliance and device-specific drawer/scanner support. The current order command contains product/quantity lines and no table/modifier contract; the draft demonstrates a basic order flow rather than the complete dine-in domain. If any excluded feature is required for testing, re-estimate explicitly before implementation. Manual-tender refunds are a particularly material scope change.

## Schedule and exit gates

| Slice | Target dates | Deliverable | Exit gate |
| --- | --- | --- | --- |
| 0 — Baseline and scope | 15–16 October | Verified backlog, test matrix and release baseline | Selected commit builds; current failures classified; scope and environment prerequisites recorded |
| 1 — Joined test environment | 19–23 October | Repeatable cashier/Kitchen/manager stack | Fresh setup, readiness, migrations, browser login, restart and owned cleanup succeed |
| 2 — Branch setup and navigation | 26–28 October | Correct test data, role provisioning and practical selectors | Staff reach the correct branch/station without manually copying UUIDs; tenant denial verified |
| 3 — Cashier-to-Kitchen slice | 29 October–4 November | Joined real-command lifecycle and compensation verification | Cash/PromptPay orders and kitchen transitions work; duplicate/restart/conflict cases pass |
| 4 — Windows POS and recovery | 5–10 November | Installable draft terminal and physical verification | Authentication, receipt printing, response loss, restart and session locking pass |
| 5 — Manager and financial evidence | 11–13 November | Usable reconciliation/day-close/reporting flow | Prior-day settlement, authorization, contention, delivery gaps and CSV provenance verified |
| 6 — Release hardening | 16–20 November | Frozen release candidate with operational evidence | Required tests execute without unexplained skips; restore/recovery drill and privacy checks pass |
| 7 — Staff UAT and handover | 23–30 November | Accepted first draft and bounded defect backlog | No unresolved blocking financial/security/data-loss defects; repeatable staff script and support guide |

Dependencies follow slice order. Existing earlier slices are reused, not rebuilt. Staff cashier/Kitchen testing starts on 11 November while slice 5 completes; the full manager flow enters UAT on 23 November. Reserve 26–30 November for fixes and focused retesting. If time slips, reduce navigation polish or menu size first. Never remove idempotency, tenant checks, persisted recovery or trustworthy financial evidence to meet a date.

## Common instruction for every implementation prompt

Paste this block before the relevant slice prompt. Execute one slice at a time and use its reviewed result as the next slice's baseline.

```text
Work in D:\DevOps\nexa-connect. Implement only the requested slice of
docs/Planning/November-2026-First-Draft.md. Follow AGENTS.md as Lead Developer /
Architect. Inspect README.md, AI/architecture/project_overview.md, canonical
system and restaurant architectures, relevant ADRs, contracts, component READMEs
and current code before editing. Preserve existing work and reuse implemented
vertical slices. Record baseline commit and dirty-file status.

Keep API transport, Application orchestration, Domain invariants and Infrastructure
persistence separate. Preserve service-owned databases and versioned integration
contracts. For financial/inventory/state changes explicitly assess authorization,
tenant isolation, idempotency, concurrency, transaction boundaries, auditability,
failure recovery and observability. Preserve shared correlation and safe structured
telemetry; never log secrets, tokens, headers, payment bodies or personal data.

Make the smallest coherent changes, with risk-appropriate tests. Use disposable
infrastructure for destructive acceptance and never alter unrelated environments.
Separate synthetic/component tests, joined real-identity tests and physical staff
attestation. Do not count skipped opt-in tests as acceptance. Do not deploy to
production, publish externally or make real financial transfers under this prompt.

Update every affected API, database, identity, deployment and component document.
Review both architecture summaries; change only affected sections. Run the
documentation_maintainer final audit when available, wait for it and resolve drift.
Run relevant checks and inspect the final diff. Handoff: changes, executed tests
and evidence, docs updated, architecture-summary review/change status, limitations
and the next slice's prerequisites. Stop dependent work when its gate cannot be
proven and identify the concrete blocker without weakening protections.
```

## Slice 0 prompt — Baseline and scope

```text
Establish the November one-branch online draft baseline. This slice is investigation
and verification; do not implement new business capabilities.
1. Reconcile the current code, service defaults, migration catalog, integration
   matrix and acceptance evidence. Confirm THB/manual payment scope and list
   missing table/modifier, offline, manual-refund and peripheral capabilities.
2. Build from a clean source copy; verify both BFF frontend bundles and WPF build.
   Run Unit and Architecture suites plus frontend check/test/build. Classify
   integration tests requiring configured disposable infrastructure.
3. Inspect current CI if accessible. Separate authored workflows, historical local
   passes and current remote results; record unavailable access accurately.
4. Create a prioritized draft backlog and traceable acceptance matrix covering
   cashier, Kitchen, manager, accountant, second tenant and failure scenarios.
5. Record exact prerequisites: Windows device/printer, test-stack host, identities,
   secret injection, branch timezone and staff availability. Update this plan with
   actual evidence and estimates. Report build blockers for slice 1.
Exit: a reproducible baseline and explicit scoped backlog, not a readiness claim.
```

## Slice 1 prompt — Joined test environment

```text
Provide one repeatable isolated test-stack lifecycle for the selected draft.
1. Inspect checkout and Phase 8 launchers, disposable acceptance helpers, service
   configuration and BFF endpoints. Resolve overlapping ports and missing hosts.
2. Reuse existing infrastructure to coordinate Platform Directory, Authorization,
   Restaurant, Catalog, Inventory, Kitchen, Order, POS, Reporting, Customer BFF /
   portal, Keycloak, PostgreSQL, RabbitMQ and Redis as required by the chosen mode.
   Include Payment only where actual report/day-close dependencies require it;
   keep provider checkout disabled. Add observability dependencies as needed.
3. Derive schema targets and compatibility from the current catalog. Correct stale
   current-version/setup guidance; distinguish historical release descriptions.
   Preview then apply only to the owned test databases with separate identities.
4. Validate secrets, workload audiences, service-owned grants, dependency addresses,
   persistent keys/session storage, queue bindings and consumer readiness. Apply
   TLS rules for the selected deployment; document local-only HTTP exceptions.
5. Supply start/status/stop and data-reset procedures that operate only on owned
   test resources. Verify fresh setup, BFF login, service restart and cleanup.
Exit: staff can start the same complete stack without undocumented manual steps.
```

## Slice 2 prompt — Branch setup and navigation

```text
Make the test branch usable without copying internal identifiers between screens.
1. Inspect current hierarchy, Catalog, Inventory, authorization provisioning and
   sample-data tooling. Provision one consistent THB branch, explicit business
   timezone, terminal/store, representative menu/station codes and test stock.
   Use owning APIs or approved service-owned tooling, never cross-service SQL.
2. Provision least-privilege cashier, Kitchen operator, manager and accountant
   access; configure existing manager limits where the selected actions need them.
3. Reuse existing authorized branch discovery in operational screens. Replace
   mandatory raw branch UUID entry where supported. Provide configured/discovered
   existing station codes without inventing a canonical station-management model.
4. Ensure selector changes clear stale state and preserve permission boundaries.
   Keep configuration/provisioning minimal; document unsupported menu editing.
5. Verify real user access, cross-tenant/branch denial and safe re-provisioning.
Exit: each test role reaches its intended workflow with coherent branch data.
```

## Slice 3 prompt — Cashier-to-Kitchen slice

```text
Complete joined acceptance for existing online manual-tender checkout and Kitchen.
1. Trace POS quote/confirmation -> durable Order -> Inventory reservation -> Kitchen
   station tickets -> cash or verified manual PromptPay settlement -> receipt.
2. Add a guarded joined runner using actual customer identity, PostgreSQL and
   broker boundaries. Fixtures may supply reference data, not pretend that seeded
   financial transitions demonstrate real checkout acceptance.
3. Verify changed-price reconfirmation, insufficient stock, exact checkout retries,
   one ticket per station after replay, Start/Ready/Complete, competing kitchen
   screens, read-only roles and branch/tenant denial.
4. Trace explicit pre-payment cancellation and ordinary workflow compensation
   separately. Exercise cancellation during preparation and after completion.
   Prove stock is not released twice and completed food is never silently undone.
   Fix confirmed in-scope defects coherently; document unresolved review states
   rather than falsely reporting cancellation or payment success.
5. Test Order/Kitchen process interruption, response loss and broker outage/recovery;
   verify durable publication and no duplicate stock, ticket or settlement effects.
Exit: actual joined flow and failure boundaries pass with safe evidence and cleanup.
```

## Slice 4 prompt — Windows POS and recovery

```text
Prepare the existing WPF client for staff use on one Windows test terminal.
1. Inspect publish/configuration, PKCE protocol registration, DPAPI/SQLite recovery,
   session policy, current receipt rendering and generic Windows print path.
2. Package a reproducible draft install/update and terminal configuration. Preserve
   local recovery on update; detect wrong organization/branch/terminal scope.
3. Improve only usability that blocks testing: pending-order visibility, original
   order verification, cashier totals/change, manual PromptPay confirmation,
   receipt lookup/reprint and clear failure messages. Keep stable operation IDs.
4. Verify on the actual device: sign-in, token renewal, idle lock, fresh login,
   checkout response loss, restart before/after send, protected state integrity,
   unauthorized actions and receipt print/reprint. Printer failure must not offer
   another collection or change Paid status.
5. Use the confirmed PDF printer through the Windows print path. Inspect saved
   receipt PDFs and exercise print/reprint and cancelled/failed print recovery.
   Defer physical receipt-printer, drawer/scanner and protocol adapters. Identify
   which interactive checks are user attestations; do not claim physical printing.
Exit: an installable terminal with proven payment/recovery and receipt behavior.
```

## Slice 5 prompt — Manager and financial evidence

```text
Make existing cash reconciliation, reporting and online day close testable by staff.
1. Trace actual cash/PromptPay sales through POS movements and Reporting receipts;
   prove separate sales, tender, refund and cash-variance meanings. Do not require
   projections to be synchronous or treat observed completeness as certification.
2. Exercise cash close and variance review, completed branch-local date evidence,
   prepare/cutoff/seal/approval, source fencing and durable finalization using the
   existing contracts. Simplify navigation/instructions only where staff get stuck.
3. Verify manager contention, stale reviewed versions, accountant read-only access,
   live revocation, dependency outages, delayed/missing delivery and restart/resume.
   Use guarded historical-time acceptance fixtures and a real next-day staff drill;
   introduce no production time override or bypass of readiness blockers.
4. Verify immutable settlement receipt, ledger comparison and bounded CSV export
   preserve original totals, separate corrections, delivery gaps and provenance.
5. Document a safe manual-tender refund limitation and operator escalation path.
   No negative movement or late correction may masquerade as a refund. Do not add
   new refund, fiscal, global-watermark or offline-finalization scope in this slice.
Exit: joined manager flow passes with honest financial evidence and role boundaries.
```

## Slice 6 prompt — Release hardening

```text
Freeze and verify the one-branch draft release candidate.
1. Record commit/build/schema/configuration identities. Run Unit/Architecture,
   relevant PostgreSQL/broker suites, frontend check/test/build/audits and joined
   cashier/Kitchen/day-close/settlement checks against that candidate. Require
   meaningful opt-in cases to execute and verify disposable cleanup.
2. Obtain fresh clean-checkout CI evidence where accessible. Diagnose setup or
   broker failures from safe bounded artifacts; do not hide them with retries,
   skipped tests or unrelated infrastructure changes.
3. Exercise restart recovery, retained outbox delivery and one isolated backup /
   restore drill for service databases and owned identity/configuration state.
   Assess POS DPAPI/local-state recovery separately; do not imply the protected
   terminal database is portable to another Windows account or device.
4. Verify readiness, structured telemetry/redaction, bounded failure alerts and
   operator investigation queries. Document the test environment's actual alert
   receiver; local Alertmanager currently sends no external notifications.
5. Fix blocking data-loss, duplicate-effect, financial, authorization and setup
   defects. Revalidate affected gates and document forward-recovery constraints
   where immutable financial history prevents downgrade.
Exit: frozen candidate, reproducible evidence and usable operations/recovery guide.
```

## Slice 7 prompt — Staff UAT and handover

```text
Run final staff acceptance for the November draft in its designated test environment.
1. Supply a short staff script and expected results for cashier sign-in/shift,
   cash and manual PromptPay checkout, Kitchen preparation, cancellation conflicts,
   receipt/reprint, cash variance review, next-day close, settlement ledger/CSV and
   denied accountant/cross-tenant mutations.
2. The user runs scenarios on the Windows PC with the PDF printer and synthetic
   data, using separate cashier/Kitchen/manager/accountant identities. Use isolated
   browser sessions and automated coordination for contention. Collect sanitized
   defects/evidence; distinguish measured checks from the user's attestation.
   Do not perform real payments or message external people under this prompt.
3. Classify blockers by financial correctness, security, data loss, unrecoverable
   workflow and practical usability. Fix blockers within scope, add appropriate
   regression coverage and rerun affected acceptance on the final candidate.
4. Record passed/failed/blocked cases and support/recovery procedures. Carry minor
   UI requests and excluded capabilities into a separately prioritized backlog.
5. Deliver the exact installable build, setup/runbook, evidence index, role matrix,
   known limitations and next iteration plan. Recommend acceptance only when no
   blocking defect remains; leave the final staff acceptance decision to the user.
Exit: reviewable first draft by 30 November, or explicit blockers and revised dates.
```

## Next-iteration roadmap

Execution plan: [next-iteration phases, agreement checklist and copyable prompts](Next-Iteration-Roadmap.md).
The detailed plan separates the core second-test milestone from proposed QR/kiosk
follow-ons and the later cloud migration. Entries below retain the original agreements.
Phase 0 is recorded in the [baseline assessment](Next-Iteration-Phase-0-Baseline.md);
the user will confirm server specifications and consult on POS hardware before purchase.

### Dedicated Kitchen display entry — planned

Recorded at the user's request on 21 October 2026; implementation has not started.

- Add a direct `/kitchen` route within the existing Customer Portal application and deployment, with a focused ticket queue and minimal navigation for Kitchen staff.
- Keep Kitchen accessible from the manager's Customer Portal navigation; both entry points use the same tickets and backend workflows.
- Preserve the existing authentication, live authorization and branch/station permission boundaries. The direct link must not bypass sign-in or grant additional access.
- Provide authorized branch/station selection and a full-screen display option. Validate the layout and ticket controls on the chosen screen and touch device before claiming hardware support.
- Acceptance: opening or refreshing the direct URL reaches the Kitchen view after authentication; authorized ticket transitions work through either entry point; unauthorized access remains denied; exiting full-screen restores normal navigation.

This is a roadmap item, not a delivered route or a separate Kitchen website. Scheduling and implementation verification remain pending.

### Windows POS with two displays — planned

Recorded at the user's request on 21 October 2026; implementation has not started.

- Support two displays connected to one Windows POS PC: a staff/manager-facing operational view and a separate customer-facing view, using Windows extended desktop.
- Keep order entry and authorized operational controls on the staff display. Manager-only actions continue to require manager authorization; the display assignment grants no permissions.
- Proposed customer content: current order items, quantities, prices, discounts/charges where applicable, total, selected tender and customer-safe payment status/change. Final content and interaction requirements remain to be agreed.
- Derive the customer view from the same active order and authoritative quote/payment results. Never present a pending or uncertain payment as confirmed.
- Exclude staff controls, credentials, internal diagnostics, other customers' orders and manager financial reports from the customer display. Clear transaction details appropriately on completion, cancellation, lock and sign-out.
- Provide display selection and safe behavior on disconnect, reconnect and restart; do not move internal staff content onto the customer screen automatically.
- Acceptance: both screens stay consistent through order edits and payment outcomes; customer content remains appropriately limited; screen loss does not duplicate or interrupt financial commands; role restrictions and recovery remain intact. Verify on the actual two-display Windows hardware.

This is a roadmap requirement, not current dual-display support. Scheduling and implementation verification remain pending.

### Cashier idle screen lock and authenticated resume — agreed requirement

Recorded on 21 October 2026 following the deferred cashier-session timeout observation.

- Idle timeout locks the POS workspace with a screensaver-style lock screen; it does not sign out the cashier, close the shift/drawer or discard work.
- Require authentication before resuming protected controls. Mouse movement, a keypress or dismissing the screensaver alone must not unlock the workspace. The authentication mechanism remains to be designed; no new PIN or shared unlock credential is approved by this requirement.
- Resume the original cashier's authorized context, shift, drawer, order and pending operation identities. A different user must not inherit the original cashier's work or authority through unlock.
- Preserve recovery across lock and restart; pending or uncertain checkout must be reconciled through its original operation without collecting again.
- Hide sensitive content while locked, including transaction details on the planned customer-facing display. Recheck session validity and authorization before protected actions resume; explicit sign-out, revocation and absolute session expiry remain separate from idle locking.
- The current draft already documents an idle lock with fresh authentication. Review the reported behavior against this requirement before deciding what code needs to change; this entry does not certify successful resume or imply the timeout defect has been reproduced.
- Acceptance: idle triggers lock without logout, authenticated resume restores retained work, failed/cancelled authentication leaves controls locked, active use follows the intended renewal policy, and lock/restart/resume creates no duplicate shift or financial effects.

Linked observation: `cashier-session`, evidence `7127393b0c64422c92060481e421f0f1` in the private UAT notes. Investigation, authentication UX design and verification remain pending.

### Media-gallery screensavers for both POS displays — agreed requirement

Agreed and recorded on 21 October 2026; implementation has not started. Extends the two-display POS and authenticated idle-lock roadmap items above.

- Use a predefined gallery of approved product/menu images for a screensaver slideshow. Authorized staff select the images and their display order through the gallery configuration.
- Customer display: show approved product/menu images, with optional product names and promotional text. Hide order and payment details while locked; provide no authentication controls on this display.
- Staff/manager display: show gallery images with a visible Resume / Authenticate control. Authentication takes place only on the staff display.
- Both displays follow one POS lock state while presenting different content. Touching either slideshow or dismissing its visuals must not unlock protected controls; successful authentication restores the appropriate operational and customer views without losing retained work.
- Cache approved images locally for network interruptions. If no usable approved images are available, show a neutral branded fallback; media loading failures must not prevent locking or authenticated resume.
- Respect the existing Media ownership, organization scope and access rules when selecting, retrieving and caching gallery assets. Detailed cache refresh/removal behavior remains to be designed.
- Acceptance: configured images appear in the selected order on the intended displays; customer content exposes no internal controls or transaction details while locked; slideshow interaction cannot bypass authentication; cached images work during a network interruption; missing/unusable media falls back safely; resume preserves the original shift, drawer and pending operation identities.

Scheduling, gallery configuration UX, implementation and verification remain pending. This entry records the agreed behavior and does not claim delivered screensaver or offline checkout support.

### Microsoft Store distribution for Windows POS — planned evaluation

Recorded at the user's request on 21 October 2026; packaging and publication have not started.

- Evaluate MSIX packaging for the existing WPF POS, including Store-hosted distribution, signing and managed updates. Compare against a signed EXE/MSI submission with separately hosted installer binaries before selecting the distribution approach.
- The current ZIP/PowerShell draft package is not a Store-ready submission. Define clean-PC runtime dependency handling, terminal onboarding and environment configuration without distributing backend secrets or another terminal's protected recovery.
- Verify packaged browser authentication callbacks, per-user storage/protection and compatibility with existing recovery. Installation, restart and update must preserve original shift, drawer, checkout and pending operation identities; assess upgrade/migration and uninstall data behavior explicitly.
- Design update timing and recovery for active cashier work, interrupted installation and schema compatibility. Do not assume Store-managed updates alone establish operational safety.
- Prepare the publisher account, listing/privacy materials and certification evidence. Account creation, external submission and publication remain separate future actions; this roadmap entry does not authorize or perform them.
- Cost baseline checked on 21 October 2026: Microsoft's new onboarding flow offers free Individual and Company registration; MSIX Store distribution includes Microsoft hosting/signing. Recheck current eligibility, fees and policies before execution. EXE/MSI hosting/signing and application backend operations can incur separate costs.
- Acceptance: clean-PC install/sign-in/onboarding, restart and supported upgrades pass; protected recovery and pending financial work survive without duplicate effects; package configuration contains no secrets; distribution decision, operating costs and certification requirements are documented. Public availability requires successful Store certification and an explicit release decision.

References: [developer account onboarding](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account), [MSIX package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements), and [EXE/MSI package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements).

Scheduling, packaging decision, implementation and verification remain pending. Store distribution covers the Windows client; it does not host the NexaConnect backend.

### Hosting progression: existing dedicated server, then cloud — agreed direction

Agreed on 21 October 2026. Use the user's existing dedicated server and static IP for the initial hosted environment. Move to cloud after the product has a professional UI and its services satisfy agreed operational readiness criteria. No deployment or migration has been performed by this roadmap entry.

- First assess server OS, capacity, storage, network reliability and existing workloads. Define the hosted pilot's supported scope and recovery expectations before admitting users.
- Deploy Product Owner and Customer portals with separate HTTPS origins and appropriate identity configuration. Use a reverse proxy; keep service infrastructure private and restrict administrative access. Replace local Development-only settings with reviewed deployment configuration before external exposure.
- Establish off-server backups, restore verification, monitoring, patching and controlled releases on the dedicated server. Preserve bounded-context database ownership, tenant isolation and POS recovery throughout.
- Keep deployment configuration portable through externalized endpoints/secrets and reproducible artifacts. Do not embed the dedicated server's public IP into client binaries or make the local first-draft fixture the deployed production configuration.
- Before cloud migration, agree measurable UI and service readiness criteria, including authorization, financial correctness, recovery, performance and operational verification. Professional UI completion alone does not establish service readiness.
- Plan and test cloud sizing/costs, data and identity migration, DNS/authentication callback changes, POS connectivity, cutover/reconciliation and failure recovery. Maintain immutable financial history and protected terminal state; do not assume DPAPI recovery is portable.

Server assessment, deployment implementation, readiness criteria and cloud provider selection remain pending. Cloud migration is a later milestone; no hosting purchase or external publication is authorized by this planning record.

## Completion criteria and documentation handoff

The draft is ready for acceptance when a fresh environment can reproduce the selected release, one real Windows terminal and browser Kitchen complete the supported workflows, manager evidence survives restarts and contention, unauthorized roles cannot mutate it, and the defined recovery tests produce no duplicate financial/inventory effects. All release-blocking failures must be resolved or explicitly prevent acceptance.

The initial plan and slice 0 baseline did not change application behavior or architecture. Slice 1 adds local lifecycle/configuration, explicit Redis selection and financial-host liveness; root/component/deployment docs and both architecture summaries now describe those changes. The restaurant architecture is reviewed without change because financial ownership and the planned offline model are unaffected. Slice 1 evidence distinguishes verified local behavior from remaining remote, business-flow and physical-client gates.
