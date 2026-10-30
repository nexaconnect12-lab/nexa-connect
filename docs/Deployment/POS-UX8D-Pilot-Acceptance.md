# POS UX8D — local pilot activation and acceptance

[UX9](POS-UX9-Acceptance.md) provides the subsequent repeatable regression runner and installed-release operator case/defect/retest pack. Its tooling requires no reinstall; the UX8D deployment and protected state below remain unchanged. Source fixtures do not complete the pending human acceptance checks.

## Scope and status

UX8D adds a separate default-off, history-only local profile and prepares matching UX8A–UX8C
backend/native packages. `DrawerIntents:HistoryEnabled=true` allows only the bounded list and
selected `/review` GETs; their existing live permission, hierarchy, actor and terminal checks
remain mandatory. `DrawerIntents:Enabled=false` continues to reject reserve, outcome and legacy
single-intent routes. An existing explicitly enabled authority profile still permits history.
The native production factory remains `DisabledDrawerDriver`. This slice introduces no schema,
permission, device command or financial change. POS25 / Authorization16 and compatibility0.40.0
are prerequisites from earlier slices.

Activation and operator acceptance are separate. Until measured evidence is recorded below,
do not infer installation, real transactions, hardware or phone acceptance from source tests.

## Prepare and activate the retained pilot

1. Run the complete Windows `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` gate.
   Its required drawer profile guards verify default-off, Boolean selection, stopped/build-only
   activation, restart persistence, service isolation and unchanged payment capabilities. HTTP
   tests verify that the history-only profile cannot reserve/report an attempt or bypass denial.
2. Record the current installed manifest, retained stack/QR activation, exact five-ID custody
   scope, URLs, profile flags, reference configuration and certificate hashes. After POS is
   closed, hash protected recovery, preferences, playlists and any drawer journal/context.
   Preserve the original Windows user and its DPAPI keys. Do not copy secrets into evidence.
3. Stage a source-bound terminal package using `first-draft-terminal.ps1 -Action Package
   -RunId <retained-run-id>`. Obtain closure confirmation before stopping services/installing.
4. Stop the owned QR edge and retained stack using their lifecycle commands. Never Reset,
   reprovision business data, delete recovery or reuse an earlier candidate's activation.
5. Start the same run with build enabled and `-EnableDrawerHistory -ConfirmTestResources`.
   Existing Media, branch PromptPay and Omise opt-ins inherit unchanged. Start explicitly passes
   the latest catalog compatibility to the owning migration runner; verify POS25/Authorization16
   and Ready service health. This applies the previous slices' additive migrations.
6. Prepare a fresh QR pilot binding to the new candidate and the same stack/scope/origin. Retain
   the exact certificate/key/CA files and reported device prerequisites; carry no workflow passes.
   Configure/start the owned edge and run `qr-acceptance.ps1 -Action Diagnose` plus Prepare.
7. Install the matching terminal package through `first-draft-terminal.ps1 -Action Install`.
   Its manifest, scope, retained-state validation and closed-process guards must pass. Compare
   all protected file hashes before launching; record any missing/unexpected change as a failure.
8. Verify anonymous readiness, TLS/guest route and protected-route rejection, then launch the
   exact installed executable. Neither startup nor a responsive window proves cashier UAT.

`-EnableDrawerHistory` is accepted only for Start/Validate; actual activation requires stopped
processes and a build-enabled Start. Restart inherits the saved Boolean. The setting belongs
only to POS service environment; it does not appear in native settings, Payment, Order or BFF.
Hosted profiles remain unchanged. Do not edit immutable installed releases/activation receipts.

## Acceptance checklist

Record each check as passed, failed or pending against the new release/candidate. Only record
what was observed. Use synthetic/dedicated test orders for any operator financial tests.

| Check | Expected observation |
| --- | --- |
| Original cashier sign-in | Existing account and custody work; no new shift or collection merely to test history. |
| History without open shift | Load/refresh is available while signed in, including when custody is closed. An empty history is valid because physical attempts remain disabled. |
| History authorization | Current permitted cashier sees only own terminal/actor rows; denied/outage clears private rows. Do not fabricate records in the retained database. |
| Privacy | Lock, sign-out and cashier change clear history and suppress delayed responses. |
| Restart | Preserve pending financial recovery and preferences; explicit history reload requires live authorization. |
| Hardware isolation | Drawer controls stay disabled, no real opening; backend command routes remain503 even for authenticated users in this profile. |
| Cash / manual PromptPay | Operator verifies original amounts, existing approved PNG, explicit receipt verification, single settlement and receipt; no duplicate collection after uncertainty. |
| Omise sandbox | Fresh test token and existing test-only capability; verify original order outcome and retry/recovery without duplicate charge. No live payment key. |
| QR collection | Staff acceptance, Kitchen visibility and original cashier collection work; guest sessions remain isolated. |
| Receipt / display | Existing printer/density/input/screensaver/customer-display preferences retained; PDF and actual displays checked separately. |

## Recovery and diagnostics

Keep prior terminal package/releases, stack state, protected credentials, scoped financial and
drawer journals, certificate files and acceptance evidence. A failed refresh is not permission
to reset or collect again. Diagnose category-only service logs under `nexaconnect-pos` and
`nexaconnect-pos-client` with validated correlation IDs; never log tokens, subjects, payloads,
payment data or QR secrets. Source/API history failure guidance remains in [UX8C](POS-UX8C-Drawer-History.md).

Rollback requires matching backend/native source and schema compatibility review. POS25 itself
is index-only reversible; POS24/Authorization16 contain durable intents/permissions and must not
be blindly downgraded or dropped. Preserve all evidence; stop the owned services and retain the
previous binaries/configuration before a reviewed restore. Never redirect the active manifest
to an arbitrary earlier package or change installed files. A local pilot is not a signed/public
release. Hardware integration still requires exact printer model, connector/electrical protocol,
vendor-supported command and vendor opening test.

## Measured verification and activation

Candidate `e374994a-14af-40c6-be33-c1b1a18d872d` completed the fresh full gate on
30 October 2026 at 06:41 Singapore time: 1,332 unit tests, six architecture checks,
91 HTTP cases, 90 frontend tests, 66 QR browser cases, all 22 day-seal cases and all other
required Windows, hosting, security/profile and financial browser checks passed. The required
drawer profile script passed 20 assertions. Nine QR owning cases verified cleanup and broker
recovery; four separately hashed drawer owning cases verified cleanup. The verification hash
matches the candidate; `productionVerified=false` and `releaseReady=false` remain explicit.

Earlier run `e818a89a` failed one day-seal page-setup timeout (21/22). The isolated whole-suite
recheck passed 22/22 as diagnostic evidence; neither was relabelled as the final passing gate.
The fresh complete candidate above provides the passing source evidence.

After the user confirmed POS closure, matching terminal release
`e594bd4e2b924cdfa1e02d28529a5823` was installed and reopened on 30 October 2026 at
07:23 Singapore time. Pilot `731bea81817848838d20f5c0f38c4f3b` uses the same retained stack
and scope; all 12 owned services report Ready. Actual database history is POS25 / Authorization16.
Eight protected recovery/preference/gallery files matched before launch, and all 52 pre-existing
POS table COPY sections matched byte-for-byte, excluding migration-history metadata. Scope,
URLs, references, protected secrets and certificate files are unchanged. Manual cash default,
Omise sandbox and branch PromptPay opt-ins are preserved. History is enabled; opening authority
and the physical driver remain disabled.

Windows trusted TLS and guest page200/staff404 checks passed. Anonymous native history returns401;
the documented POS `/health/live` endpoint returns200. An initial `/health/ready` request returned401
through the protected fallback; it is not a documented readiness endpoint or readiness proof.
The exact installed native window is responsive. No agent financial/drawer action or human
acceptance is inferred. Phone, signed-in operator, financial, PDF/display and physical equipment
acceptance remain pending; `releaseReady` and `productionVerified` remain false.

The retained run's original `applicationCompatibility=0.38.0` is historical initialization
metadata. This activation used the catalog-derived explicit migration compatibility0.40.0 and
verified the resulting database histories; old metadata and receipts were not rewritten.
