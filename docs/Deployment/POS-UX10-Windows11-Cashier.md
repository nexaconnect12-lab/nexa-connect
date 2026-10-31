# POS UX10 — Windows 11-inspired cashier workspace

UX16 subsequently refines this visual direction with flatter panels, restrained corners, a dark labeled navigation rail and compact filters. The workflow and security boundaries below remain; see [current flat presentation and rollout status](POS-UX16-Flat-Cashier.md).

## Scope and rollout status

Implemented in source: a clearer Windows 11-inspired native WPF cashier shell using the
existing NexaConnect green accent and neutral light surfaces. This uses the current WPF
client, not a WinUI/framework migration. Native window controls remain available. No service,
API, database, permission, tender, pricing, idempotency or financial recovery change is added.
The customer display and portal applications retain their existing presentation.

Matching package/activation status is recorded below after verification. Existing installed
releases are immutable; source changes alone do not change the running POS. Use the normal
source-bound candidate/package workflow, finish work and close POS before installation.
Preserve scope, original-cashier markers, orders/shifts, galleries and preferences. No reset,
new financial action or physical drawer activation is part of a visual rollout.

## Cashier-visible changes

### Terminal sections follow-up

The follow-up source layout separates **Terminal & sync** into four inner tabs, with
**Overview** initially selected. Each has its own vertical scroll area:

| Inner tab | Contents |
| --- | --- |
| Overview | Terminal setup overview, explicit service/device checks and support export. |
| Input & devices | Optional touch keyboard, density, receipt printer and display roles. |
| Cash drawer | Disabled physical-opening preparation and authorized read-only attempt history. |
| Sync & recovery | Terminal management and existing movement synchronization/recovery actions. |

Overview input/display/printer shortcuts select Input & devices before bringing their target
into view. The barcode shortcut still opens Checkout without enabling scanning. Changing an
inner tab hides the on-screen keyboard to avoid editing a hidden setting. Section selection
does not run checks, print, open hardware or synchronize automatically. Current authorization,
busy/lock/privacy guards and all control names/handlers remain unchanged. Source layout and
shortcut/keyboard fixtures are verified separately from installation; see the rollout record.

To render the isolated terminal overview, run
`dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --ux7-preview`.
It writes the synthetic `.runstate/pos-ux7-overview-preview.png`, not an installed-pilot screenshot.

### Shared cashier shell

- A scrollable left navigation rail replaces the crowded top strip. All eight destinations
  keep their existing names, ordering and handlers: Checkout, Payment, Orders, QR collection,
  Shift & cash, Cash review, Receipts and Terminal & sync. Text labels remain alongside
  decorative Windows glyphs; larger-density labels wrap instead of clipping.
- Selected navigation uses both a surface and vertical indicator. Hover, disabled and
  keyboard focus states remain distinguishable; shared buttons also show pressed feedback.
  Selection accents are confined to the
  navigation label/icon, never inherited as transaction-status color by the workspace.
- The header separates product identity, cashier context and session status. A signed-in
  disabled button reads Signed in. The direct Lock button pauses the existing original-cashier
  workspace through `LockSession`; it does not sign out, close custody, collect, replay or delete
  pending work. Resume/authentication and original-owner enforcement are unchanged.
- Shared buttons, rounded fields, non-editable selectors, menu tiles and content surfaces use
  consistent spacing, neutral borders and the existing primary accent. Dropdowns retain native
  ComboBox selection/keyboard behavior and a bounded scrollable popup. Current selectors are
  non-editable; future editable selectors need an appropriate template.
- Current order, estimate/server labels, tender selection and pinned workflow/payment actions
  keep their existing meanings. Defaults remain Standard density and keyboard/mouse operation.
  Touch density and automatic keyboard opt-in remain independent saved preferences.

Navigation and header remain inside the protected operational surface. Lock/sign-out, expiry
and display-safety blocking still hide private work. The full-screen gallery and interaction-
triggered centered authentication panel retain their behavior. No private identity/payment
proof is added to the customer projection.

## Verification

The isolated native fixture checks navigation/content separation at window dimensions
1000×680,1366×768 and1920×1080 device-independent units, all eight labeled destinations,
selected indicators and a dropdown popup at least as wide as its field. Existing density/input fixtures test
independent keyboard/density combinations, menu/cart/Orders/payment layouts, printing, scanner
entry, drawer/history privacy and busy/locked guards. Session fixtures also click the new Lock
button and verify protected owner/shift/cash/pending-payment preservation without identity
refresh or business/device requests.

Run `scripts/test-pos-acceptance.ps1` for isolated regressions and the complete
`scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` gate for a source-bound rollout candidate.
For a synthetic screenshot, run:

```powershell
dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --shell-preview
```

The preview uses temporary synthetic menu/cart/custody data, hidden non-activating fixture
windows and fake service/device ports. It writes `.runstate/pos-win11-cashier-preview.png`;
it is not an installed or operator-tested release. It submits no sale, payment, bank proof,
printer job or physical drawer command.

Actual monitor assignment, scaling/DPI, touch, keyboard navigation, screen reader, high contrast
and printer/scanner acceptance remain operator/device checks. Fixture dimensions are not
measured resolutions of the purchased screens. Re-run relevant [UX9 cases](POS-UX9-Acceptance.md)
against the new installed release; do not carry old passes onto a changed binding. Actual
financial/provider/phone acceptance remains separate.

## Measured results and activation

Terminal-sections follow-up: candidate `4cceddc8-8890-4090-86d5-49657266eed7` passed the
complete source gate on 30 October 2026 at 12:00 Singapore time: 1,332 unit tests,91 HTTP
cases,66 QR browser cases and the required architecture/frontend/Windows/security checks.
Nine owning QR cases verified cleanup/broker recovery and four drawer cases verified cleanup.
The focused Windows fixture and final synthetic overview preview also passed, including
cross-section shortcuts and hidden-field keyboard clearing. Package
`f9b338825b9348ed8bf0a286730c2059` was installed after user-confirmed closure on 30 October
2026 at 12:11 Singapore time and reopened with a responsive window. Matching pilot
`7b5f7ff80db949e8b80825658785b523` retains the same origin, certificate files and branch scope.
All 12 services report Ready. Eight protected files matched before launch and all 55 pre-existing
POS data COPY sections remained unchanged; POS25/Authorization16, reference data, service URLs,
secrets, galleries and payment/input/density profiles were preserved. Windows trusted TLS,
guest200/staff404, POS liveness200 and anonymous history401 checks passed. No financial or drawer
action was submitted. A fresh operator pack `aa2a938150b34774960f12fb421954f1` starts all 17 cases
pending; prior-release observations were not copied. Physical/accessibility/DPI/operator and
public-release acceptance remain separate. Earlier installed UX10 evidence below is historical.

Candidate `3375e612-6711-4515-9ec5-b4c786dd5057` completed the full source gate on
30 October 2026 at 11:34 Singapore time: 1,332 unit tests, six architecture checks,
91 HTTP cases, 90 frontend tests, 66 QR browser cases and all required Windows, hosting,
security/profile and financial browser checks passed. Nine QR owning cases verified cleanup
and broker recovery; four separately hashed drawer owning cases verified cleanup. The
verification hash matches the candidate; production verification and release readiness
remain false. Earlier pre-final layout regression results remain historical.

The final synthetic preview was visually reviewed with the labeled rail, wrapped navigation,
local selection accent, styled selectors and header Lock action. This is presentation
evidence, not operator/device acceptance. Matching package
`ba9aa759b8054201b94512d617327462` was subsequently installed after user-confirmed closure
on 30 October 2026 at 11:47 Singapore time. New pilot `96a6b806a0074870a198f13bdb6ed2f0`
uses the same retained stack/scope. All 12 services report Ready; POS25 / Authorization16 are
unchanged. Eight protected files matched before launch and all 55 pre-existing POS table
COPY sections remained unchanged. Scope, URLs, references, secrets, certificates and profile
are preserved: cash_manual default, Omise sandbox, branch PromptPay and history enabled;
opening authority and the physical driver disabled. The exact installed native window reopened
responsive. Windows trusted TLS/guest200/staff404, documented POS liveness200 and anonymous
history401 checks passed. Original 0.38 run metadata remains historical; catalog compatibility
for the rollout remains 0.40.0.

Fresh installed-target pack `04ac87d1e3f1475d92dfc980f2e5e47b` has a valid binding and all
17 cases pending. Its `qrCandidateFresh=true` while `currentSourceVerified=false`; operator/public-release
and production acceptance remain separate. No agent financial/drawer action was performed.
Actual DPI/scaling, keyboard navigation, screen reader, high contrast, phone, peripheral,
financial and operator acceptance remain pending.

Operational follow-up: after the user closed the previous POS window, its exact process remained
windowless for 15 seconds. With stable protected-file snapshots and verified PID/start/path/no
window, that leftover process was stopped before deployment. Its shutdown cause is undiagnosed;
this visual slice does not claim to fix it. Investigate separately before professional release.
