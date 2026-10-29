# Next iteration phase 0 — baseline and execution assessment

29 October 2026 equipment update: the user reports the purchased POS equipment is now
available. Printer information supplied: PAISHUO, USB, 80 mm, 200 DPI. Exact model/Windows
driver and drawer connection/protocol are unconfirmed; the drawer is not connected or its
connection is unknown. This updates E05/E06's earlier delivery status without establishing
physical acceptance, display resolution/touch mapping or a supported drawer driver. See
[UX8 prerequisites](../Deployment/POS-UX8-Cash-Drawer.md).

29 October 2026 drawer detail: user-supplied model **PAISHUO 4141P**, advertised for PC/Tablet;
OS field “no”; 0.8 mm SPCC cold-rolled steel sheet/ABS; warranty unavailable; Guangdong, China.
This identifies the drawer only. Its connection, electrical/pulse specifications and the
exact printer model remain unconfirmed. See the UX8 prerequisites above; no hardware pass
or direct USB compatibility is inferred from the listing.

Subsequent [POS UX4](../Deployment/POS-UX4-Locked-Gallery.md) implements expiring gallery-only
readers for locked playback; automated verification, local activation and physical/human
acceptance remain distinct. Purchased screen resolution/scaling/touch mapping remain
unconfirmed and do not block this software slice.

Recorded 21 October 2026 (Asia/Singapore). Phase 0 is a source/evidence assessment,
not a new release, runtime change or server deployment. The
[phase plan and prompts](Next-Iteration-Roadmap.md) define the subsequent implementation.

## Assessment outcome

The core scope is bounded and ready for phase 1 implementation. Server and POS hardware
discovery remain prerequisites for the affected later phases, not assumed facts.
The user explicitly deferred confirming server specifications. At the original assessment,
POS purchase and display specifications were pending; the 28 October 2026 purchase update
below supersedes that equipment status. Do not infer that the POS or development PC is
the dedicated server.

The first draft is sufficient for the user to proceed; its detailed pending UAT results
remain pending. Preserve the frozen first-draft artifacts and recovery rather than
reusing their identity for the next implementation.

## Source and retained evidence inventory

| Item | Baseline |
| --- | --- |
| Git HEAD | `d6e8b2ccba48b2e07d690cbd75c7645f28ad381c` |
| Branch | `codex/cash-close-acceptance-verification` |
| Executable input SHA-256 | `e1b8b806f7d713869621c37ecf69df57cb14f3de0f6a7cd2d0fbdd47eac2ebac` |
| Working tree | Documentation edits/new planning files; dirty, not a new clean candidate |
| Retained draft RunId | `d972fcff109d435691b9eb9afcd4ddf7` |
| Frozen terminal release | `6ae071af0c2e45e1abb226a337f2fa86` |
| Final first-draft local release gate | `f1f29ecf0fa747159801c75b67f95ffa` |
| First-draft UAT | `022201cf3d784f5b8a3c3e5fc7652e35`; all 16 case records still pending |
| Preserved setup defect | `69cbe389a4f8437abc62d385ef70ab55`; recorded open, original-profile scope mismatch |
| Deferred session observation | `7127393b0c64422c92060481e421f0f1`; user-reported timeout/required sign-in, not independently reproduced |

Private evidence remains under `.runstate/first-draft-handover/150594a8b9f24c9199d7ca076ee45221`
and the original UAT directory; do not distribute those directories. The retained local
receipt records a passed clean D6 candidate. Archived exact-SHA CI metadata records ten
successful dispatches; this phase does not rerun CI or claim current remote status.
The Windows ZIP SHA-256 was rechecked as
`2c896b7bec1090ea063bc464e3265e163421052839ccfa590e76091276665d8c`.
The current executable input hash matches that baseline, but dirty documentation does
not constitute a clean release. Do not reinterpret historical evidence as next-iteration proof.

The user supplied installed Status from the separate Windows account showing the final
release/run/revision and recovery preserved. That report does not prove the remaining
interactive cases, and the original setup defect has not been silently resolved.
The later shift incident was traced to unavailable application hosts and the retained
stack was restarted in the earlier troubleshooting session. This phase does not assert
current liveness or perform transactions, reprovisioning, reset or another restart.

## Implementation map: reuse and gaps

| Area | Source evidence and reusable behavior | Gap / phase action |
| --- | --- | --- |
| Session policy | `PosSessionPolicy.cs` and `PosClientConfiguration.cs`: five-minute idle, ten-hour absolute defaults, refresh lead 60 seconds; configurable bounded values | Retain explicit policies; classify observed failure before choosing changes. Phase 1 |
| Authentication | `PosAuthentication.cs`: PKCE/state callback, serialized refresh, persisted lock marker and forced `prompt=login` | Idle replaces tokens with a nonsecret marker, explaining why fresh authentication is expected; this alone does not establish the user's exact cause. Design resume semantics explicitly. Phase 1 |
| Identity ownership | `PosTokenSet.cs` has no subject field; `LocalShiftState` carries shift ID/number/time; sign-in installs a returned token | No explicit original-cashier identity binding was established by this review. Trace validated issuer/subject and retained work before implementing same-user resume; do not trust an unvalidated token payload or assume service checks alone supply the UX guarantee. Phase 1 |
| WPF workspace | `MainWindow.xaml.cs`: timer/interaction locking, clears receipt/card entry, disables protected controls; `App.xaml.cs` starts one main window | No dedicated lock overlay/gallery or customer window/display selection. Review in-flight/busy operations because the session timer skips while busy. Phases 1, 4–5 |
| Recovery and API | `LocalPosStore.cs`, `LocalPosDatabase`, original checkout/settlement IDs and authorized POS/Order workflows | Reuse protected state and authoritative outcomes; add presentation/state boundaries without a second checkout or a recovery purge. Phases 1, 4 |
| Portal UI | React 18, Ant Design, shared design-system/layout/error/authorization packages; Product Owner has separate BFF/cookie/client | Consolidate visual consistency and states while preserving platform/customer authority. WPF keeps its native framework. Phase 2 |
| Kitchen | `KitchenQueuePanel.tsx`: branch/station selection, versioned transitions and bounded requests; Customer `main.tsx` restores section state from the URL hash and writes navigation changes to it | Existing `#kitchen` navigation and BFF SPA fallback are reusable; the proposed `/kitchen` pathname entry, its login/refresh behavior and focused full-screen layout need implementation. Keep the same Kitchen queue. Phase 3 |
| Media | Media README and service own scoped metadata, Catalog validation, scan-gated upload, variants, short-lived signed downloads, durable processing/deletion | Existing portal Media panel is asset management, not an approved ordered slideshow. Define selection owner, device retrieval, cache expiry/removal and lock-safe rendering. Phase 5 |
| Installation | Terminal scripts provide hashed immutable per-user releases, read-only scope preflight and preserved recovery; WPF targets `net10.0-windows` | ZIP/PowerShell is not Store-ready. Assess MSIX protocol/storage/update compatibility, runtime handling and migration before selecting packaging. Phase 7 |
| Deployment | Separate Customer/PlatformAdmin BFFs; production Redis sessions and durable certificate-protected Data Protection requirements documented | Draft launcher forces Development/loopback and excludes the complete Product Owner/Media graph. Compose/test launchers are not a ready dedicated-server deployment. Phase 6 |
| Verification | Session/authentication unit tests; frontend Kitchen contracts; joined cashier/manager/financial gates; terminal/lifecycle/UAT/release guards | Add missing same-user resume, deep-link, two-display, gallery and hosted operations coverage. Existing tests do not certify new hardware or a public deployment. Phases 1–9 |

Primary source locations: [POS README](../../src/Clients/NexaConnect.POS/README.md),
[frontend README](../../src/Frontend/README.md),
[Product Owner README](../../src/Frontend/apps/product-owner-portal/README.md),
[Media README](../../src/Services/NexaConnect.Services.Media/README.md),
[Customer BFF README](../../src/Gateway/NexaConnect.CustomerBff/README.md),
[PlatformAdmin BFF README](../../src/Gateway/NexaConnect.PlatformAdminBff/README.md),
[deployment requirements](../Deployment/Deployment-Guide.md),
[Keycloak production runbook](../Identity/Production-Runbook.md).
These findings identify review targets; they are not a complete security certification.

## Environment prerequisites and decision register

| ID | Known / decision | Pending prerequisite or bounded default | Gates |
| --- | --- | --- | --- |
| E01 | Existing dedicated server and static IP; own-server first | User will confirm OS/version, CPU, RAM, storage/free space and current workloads later; no server access assumed | Phase 6 deployment |
| E02 | Singapore test context | Server location, routing/upload speed, internet reliability and power arrangements unknown | Phase 6 capacity/connectivity |
| E03 | Separate portal origins intended | Domain/DNS ownership, certificate method, identity origin and administrative access method unconfirmed | Phase 6 external access |
| E04 | Off-server recovery required | Backup destination, retention, recovery time/data-loss objectives, alert recipient and maintenance owner unconfirmed | Phase 6 restore and phase 8 readiness |
| E05 | Purchased S200D confirmed 28 October 2026: Windows 11, Intel Core i5 5th generation, 8 GB RAM, 128 GB SSD; 15.6-inch main staff screen and 11.6-inch extended customer screen; advertised touch dual-screen, Wi-Fi and Bluetooth. Seller website lists `1366x76.8 / 1920x1080`; user believes maximum may be `1920×1080`, unverified. POS has not arrived; actual delivered specifications cannot yet be checked | After delivery, confirm native/actual resolutions and main/customer assignment, Windows scaling/DPI, exact CPU, OS edition/architecture, touch support per screen, ports, GPU/display topology and mounting. `1366×768` is only a possible typo correction; seller-listed options/maximum are not confirmed delivered specifications or measured acceptance | Phases 4, 7 hardware verification; phase 9 |
| E06 | Purchased bundle includes cash drawer, 80 mm roll-paper POS printer and barcode scanner; Microsoft Print to PDF remains the verified test target | Confirm peripheral models, connection interfaces, printer driver/protocol, drawer connection and scanner input mode. Physical integration remains separately scoped and unverified | Phase 9 PDF acceptance; separately scoped peripheral acceptance |
| D01 | Idle screen lock, authenticated resume, no work discard | Prefer existing Keycloak interactive flow as the implementation starting point; mechanism/UX reviewed in phase 1. No new PIN, bypass or shared unlock credential approved | Phase 1 |
| D02 | Gallery selection by authorized staff, approved product/menu assets | Select owning context and permission, playlist schema/version, refresh/capacity/withdrawal rules and fallback branding in phase 5 | Phase 5 |
| D03 | Professional consistent UI | No logo/style reference confirmed. Provisional reuse: existing green `#14532d`, semantic colours, neutral surfaces, existing typography/frameworks; no claim of user-approved final visual design | Phase 2 rendered review |
| D04 | Cloud later; Store evaluation now | No provider purchase, migration date or public listing selected; verify current Store rules during phase 7 | Phases 7–8 / later migration |

Unconfirmed screens do not justify inventing a hardware specification. For development
only, use the existing WPF 1280×850 workspace/minimum 1000×680 as regression references,
plus resizable layouts and simulated secondary monitors. These are not buying advice or
hardware certification; add the actual purchased resolutions and DPI to the test matrix.

## UI direction and implementation boundaries

- Retain React/Ant Design for portals and native WPF for POS. Reuse existing design tokens,
  validation/error patterns and visible focus; avoid a framework migration as styling work.
- Prioritize readable order/tender totals, clear active versus locked/uncertain states,
  keyboard access and deliberate touch targets. Keep platform administration separate
  from restaurant operations even where visual styling is shared.
- Staff owns protected controls and authentication. Customer sees a bounded presentation
  of the same transaction; neither a second order owner nor a second settlement path.
- Locked displays hide transaction details. Staff shows Resume/Authenticate over approved
  imagery; customer shows approved imagery without authentication. Media failure must
  not delay locking or resume. Coordinate security state independently of slideshow timers.
- Phase 4 should move touched presentation/state mapping out of the large WPF window
  where useful, without a speculative rewrite. Business invariants stay in owning services.
- Gallery selection must not move Media storage ownership into POS or shared code. Select
  the configuration owner after tracing current contracts; record an ADR if a durable new
  boundary/contract is needed. This baseline does not decide that ownership prematurely.

## R01–R10 acceptance and verification allocation

| Requirement | Automated / developer evidence | Phase-9 user evidence |
| --- | --- | --- |
| R01 Kitchen | Authenticated direct load/refresh/return, scope denial, station/version/retry tests | Actual station selection, full-screen use, ticket preparation |
| R02 Dual displays | Same snapshot/quote/outcome mapping, no duplicate commands, safe monitor fallback | Real screens/DPI, orientation, unplug/reconnect and restart |
| R03 Idle/resume | Idle boundary, active renewal, forced login, wrong-user/revocation/expiry, preserved operation IDs | Leave idle, authenticate/resume original work, restart locked |
| R04 Separate lock content | Single security state, no customer unlock/transaction leakage, failure/cancel auth | Inspect both displays while locked and after resume |
| R05 Gallery | Asset scope/safety, selection order, stale/revoked cache policy, outage/fallback | Approved images/text, readability, offline imagery and resume |
| R06 UI | Build/functional/accessibility/layout checks across affected surfaces | Operate Product Owner, Customer, POS and Kitchen at actual resolutions |
| R07 Own server | TLS/identity/supervision/reboot, denied infrastructure access, isolated restore | Access portals/POS from intended devices and review support instructions |
| R08 Installer | Clean-profile install/callback/upgrade, schema/recovery compatibility | Actual PC install/update and retained-work verification |
| R09 Portability | Externalized configuration, reproducible artifacts, readiness and future cutover checklist | Review supported scope/limitations; no cloud execution required |
| R10 Second test | Exact-candidate CI, gate receipts, immutable artifact/UAT bindings | Cash/fictional PromptPay, PDFs, recovery, next-day close/ledger/CSV and user decision |

For financial/state workflows, preserve live authorization and tenant isolation,
aggregate/transaction boundaries, immutable receipt history, idempotent original commands,
concurrency checks and durable audit/outbox recovery. Presentation, locks, monitors and
slideshows must not create financial commands. New HTTP/BFF/adapters must carry validated
correlation and safe structured failure/authorization events; worker telemetry is equivalent.

## Effort ranges and execution checkpoints

These are preliminary engineering-effort ranges in working days for one implementer,
including focused tests/docs, not calendar commitments or autonomous-agent time guarantees.
They assume reuse of existing workflows, one branch, no new payment provider/refund/offline
workflow and timely environment access. Re-estimate after each phase; external delays,
hardware purchase, certification and substantial discovered defects are additional.

| Phase | Initial range | Checkpoint / dependency |
| --- | --- | --- |
| 0 | Assessment delivered in this change | Environment register retained open; no later phase claimed complete |
| 1 | 2–4 days | Lock/identity/resume behavior proven before dual-display integration |
| 2 | 4–7 days | Consistent core design and rendered states; provisional branding explicit |
| 3 | 2–3 days | Kitchen deep-link, auth return, versions and full-screen verified |
| 4 | 3–5 days | Dual-display state mapping; physical acceptance still gated on hardware |
| 5 | 4–6 days | Gallery ownership/configuration/cache decision and failure tests |
| 6 | 4–7 days after server prerequisites | Hosted graph, operational security and restored application proof |
| 7 | 3–5 days for evaluation/private candidate | If MSIX incompatibilities require major migration, re-scope before proceeding |
| 8 | 3–5 days | Full integration, exact clean-source CI and release inventory |
| 9 | At least two calendar dates across actual Singapore midnight | User availability/hardware required; fix/retest effort depends on findings |

Phases 1–8 provisionally total 25–42 engineering days if sequential. Parallel scheduling
of independent work may reduce elapsed time but is not assumed. There is no new promised
November acceptance date. The original November first-draft target is historical context,
not a deadline for this expanded iteration. Server/hardware waiting time is not included.

Checkpoint A: phase 1 can start with current source and protected test fixtures.
Checkpoint B: phases 2–5 can proceed using provisional visual defaults/simulated displays;
actual user hardware checks remain pending. Checkpoint C: do not deploy publicly without
E01–E04 and appropriate session authorization. Checkpoint D: phase 8 admits only completed
core scope with reproducible evidence; phase 9 is the user's consolidated test.

## Candidate, recovery and UAT strategy

1. Preserve original first-draft identifiers/notes; do not reset its environment, edit its
   immutable binding or check off its pending tests to represent later work.
2. Use separately identified disposable environments for automated mutations and new
   scope testing. Never copy DPAPI recovery to another user/device or delete scope conflicts.
3. Keep phase evidence linked to revision/input/configuration identities; no new release
   claim from a dirty worktree. No commit/push is performed by this assessment.
4. For phase 8, freeze a coherent clean candidate, run relevant joined/regression gates and
   exact-source CI, then create a matching package and newly bound UAT record. Extend the
   existing 16-case schema/report to cover R01–R10 or add a validated companion record;
   the current UAT tool cannot be assumed to accept new arbitrary CaseIds.
5. Keep the next test environment through actual Singapore midnight. Capture safe user
   observations for both monitors/PDFs/resume and next-day settlement. UI/device actions
   are not passed by automation or conversation approval alone.
6. Fixes that change inputs require matching verification and new candidate binding with
   traceable retests. Restore rehearsals run in isolation; do not replace the only user
   environment or roll back committed financial history.

QR/kiosk, manual refunds, table/bill expansion, providers/fiscal changes, physical devices,
full offline synchronization, Store publication and actual cloud cutover remain outside
this core implementation unless separately selected. Keep proposed enhancements separate
from existing capabilities and actual acceptance evidence.

## Phase-0 verification and documentation

- Inspected source paths and related component/API/deployment docs above, existing test
  suites and the original private UAT/handover records. No credentials were retrieved.
- Recomputed current executable input identity and original ZIP hash. Archived local/CI
  records are evidence references, not fresh full-suite execution or independent signatures.
- Focused existing `PosSessionPolicyTests` and `PosAuthenticationTests`: 10 passed,
  0 failed, 0 skipped on 21 October 2026. Command: `dotnet test
  tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter
  'FullyQualifiedName~PosSessionPolicyTests|FullyQualifiedName~PosAuthenticationTests'`.
  The sandbox attempt exited without diagnostics; the normal local-access rerun passed.
  These isolate policy/refresh/lock-marker behavior, not WPF interaction, same-user resume
  or hardware. No new behavioral tests or application changes were made.
- Review links, Markdown fences and final diff; run the documentation-maintainer audit.
- Updated this baseline and the next-iteration plan; linked it from the existing roadmap.
  Root README, project overview, canonical project architecture and restaurant architecture
  were reviewed unchanged: no runtime, topology, public contract or architectural decision
  is implemented by phase 0. No new ADR is warranted for the planning assessment.

Phase-0 completion means a reproducible assessment, traceable scope and explicit open
prerequisites. It does not mean server sizing, hardware selection, new features or user
acceptance are complete.
