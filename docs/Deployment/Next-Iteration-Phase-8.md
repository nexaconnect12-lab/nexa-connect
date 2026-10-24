# Phase 8 — integration, hardening and release freeze

## Delivery and freeze status

Phase 8 adds integrated verification, a shared artifact/source identity, strict release
readiness checks, CI verification and a frozen-candidate second-test handoff. It does not
assert that the release is frozen. E01–E04 server facts/target operation and Phase 7 signing/
VM/device gates remain pending until measured. No retained draft, DPAPI state, deployment,
Git commit/push, Store account/submission or original UAT record is changed automatically.

Use [the release commands](../../scripts/release/README.md) and
[ADR-043](../Architecture/Decisions/ADR-043-next-iteration-release-evidence.md).
The [Phase 6](Next-Iteration-Phase-6.md) and [Phase 7](Next-Iteration-Phase-7.md)
runbooks define target prerequisites and unsupported migration boundaries.

## Agreement review and current boundaries

| Agreement | Implementation/evidence route | Remaining acceptance |
| --- | --- | --- |
| R01 direct Kitchen | Existing portal `/kitchen`, live branch/station/version commands; Kitchen browser/HTTP gates | Actual remote station/device |
| R02 staff/customer POS | One current transaction projection, topology safety | Real displays/DPI/unplug/reconnect |
| R03 idle/resume | Original cashier authenticated lock/resume and active renewal; DPAPI tests | Actual operator/browser timeout/restart |
| R04–R05 lock/gallery | Separate playlists, approved scoped media, lease/cache/fallback; UI/domain/persistence checks | Real storage/scanner/display and withdrawal behavior |
| R06 UI | Portal/native foundations and rendered source evidence; regression browser tests | User usability at actual hardware |
| R07 own server | Guarded artifacts/operations procedures | Confirmed facts, actual TLS/login/reboot/restore/alerts/capacity |
| R08 installation | Private EXE, custody/schema/DPAPI and maintenance checks | Trusted signing, actual VM/device roundtrip/interruption/callback |
| R09 portability | Shared source/configuration/schema/artifact binding and gated freeze | Exact committed clean CI and hosted readiness |
| R10 consolidated test | New release-bound fifteen-case pending companion | User observations and acceptance in Phase 9 |

No new QR/kiosk, refunds, table/bill rules, provider payments, fiscal integration, offline
sales, Store publication or cloud cutover is included. Phase 8 exercises existing financial
rules; it does not repair data or bypass live permissions to make a gate pass.

## Local verification and financial invariants

`test-next-iteration.ps1 -Profile Source` runs all unit/architecture tests, selected actual
BFF/POS HTTP boundaries, frontend TypeScript/unit/build, production/full dependency audits,
hosting/release/identity/installer guards, and Kitchen/gallery/UI browser suites. Tests
must execute without skips, todos, retries or expected failures. Source is bound before
and after; stdout/stderr and raw reports stay in owner-restricted private logs. Only a
bounded summary with source identity/counts is appropriate to share.

Run hosting/installer publication and Source/Joined verification sequentially within
one checkout. BFF publication installs and builds the shared frontend workspace;
concurrent publication or dependency installation can invalidate another gate's files.
An interrupted or competing install requires restoring locked dependencies and rerunning
affected verification after cleanup, rather than accepting a partial receipt.

`-Profile Joined -ConfirmDisposableInfrastructure` repeats Source within one unchanged
candidate interval and adds actual normal-clock cash/manual PromptPay checkout→Kitchen→
cash/POS/reporting effects, interrupted original-command recovery, manager cash reconciliation,
local quiesced SQL restore/resumed reads, durable settlement reporting/replay and historical
Singapore cutoff/settlement/ledger/CSV fixtures. Branch gallery persistence uses a generated
loopback PostgreSQL database/schema and verifies tenant/version/pricing/audit/outbox behavior.
Only generated run-owned infrastructure may be stopped/removed; failed cleanup fails the gate.

Existing owning aggregate/use-case authorization, tenant isolation, financial bounds,
idempotency identities, transaction/audit/outbox commitments and state/version contention
remain authoritative. No test runner updates another service's live database, resets sold
stock, repairs financial history or changes a retained operation ID. Test fixtures are
isolated and explicitly not live provider transfers. Correlation and safe structured
service diagnostics retain their existing shared observability identities.

The historical business-clock fixture is not actual midnight. The local restore proves
matching service/Keycloak SQL restoration, not a cloned running broker/cache/object/identity/
application graph. Both limits remain explicit in the release/Phase 9 checklist.

## Hardening discovered during verification

The initial Source run passed backend/HTTP/frontend unit/build gates but failed the full
npm dependency audit on the former Vite 7.3.6 / Vitest 4.1.11 pins. Phase 8 updates exact
pins/lockfile to Vite 7.3.7 and Vitest 4.1.12, without audit suppression or runtime dependency
changes. See the maintainers' [Vite advisory](https://github.com/vitejs/vite/security/advisories/GHSA-9jrq-w75r-8gcw)
and [Vitest advisory](https://github.com/vitest-dev/vitest/security/advisories/GHSA-qxg6-89mg-rqv8).
The production and full audit gates still reject moderate-or-higher findings and network
failures. Audit results are time-dependent, not complete security certification.

## Candidate/artifact/configuration binding

Create a fresh candidate **before** verification/builds. Both hosting and installer
receipts carry the same Git-clean source inventory, including packaging, root settings
and CI files. Do not attach old Phase 6/7 artifacts by HEAD alone: they require rebuilding.
Actual bundle/schema and installer/payload bytes are checked when binding and again on
status/freeze; unsigned test output stays marked unsigned. There is no automatic signing
certificate acquisition or unsigned-release waiver.

After the actual server plan is confirmed, bind production configs/certificates through
`configure`. It invokes the existing Phase 6 structural guards and hashes sensitive files
without copying/printing their values. A template for eight exact operator observations
is then bound to candidate, artifacts and configuration. Record actual safe evidence UUIDs
and UTC times only; arbitrary headers/bodies/URLs/notes never enter the exported record.
These observations are operator-attested. Do not label written runbook steps or a mock
HTTPS response as measured target success.

The eight observations cover hosted login/denial, intended remote POS/Kitchen, actual
host reboot/outage recovery, off-server full-graph restoration with measured RPO/RTO,
real alert recipient firing/resolution, measured capacity, installed roundtrip/callback
and interrupted-install repair with unchanged recovery. Source tests cannot fill them.

## Clean CI, freeze and replacement

The new `next-iteration-release-verification.yml` has Windows source/DPAPI/POS, Linux
portal/source and real-OIDC joined financial jobs. Workflow code is authored evidence
only until an actual run completes. After a coherent commit/push is authorized, build/
verify the exact clean candidate and dispatch that workflow. The release tool reads the
configured origin through `gh api`, requires the exact SHA/workflow_dispatch/path and
actual successful jobs, and rechecks the attempt at freeze/UAT preparation. Do not use
an old/PR merge SHA or a local `passed=true` file as clean CI.

`status` reports blockers without mutating anything. `freeze` refuses dirty source,
stale/changed/unbound artifacts/configuration, unsigned/untrusted PEs, missing source or
joined gates, missing exact CI or unpassed target observations. No frozen/UAT record is
written on refusal. Successful freeze writes a new immutable-path manifest binding every
identity and evidence digest, with acceptance still pending. Existing files cannot be
overwritten by the CLI; preserve directories/permissions and archive manifests privately.

Changed executable source, settings/certificates, artifacts or operator evidence require
replacement binding/affected retests and a new candidate. Retain older records/observations
for traceability. Never edit a success receipt, copy DPAPI between accounts, downgrade
immutable financial history or run competing server writers to preserve an old release ID.
Actual cutover/restore remains governed by the owning Phase 6 procedures and session
authorization, not by a green local release summary.

## Consolidated second-test handoff

After a valid freeze, `prepare-uat` revalidates everything and creates a separate record
and human checklist bound to frozen-manifest/setup hashes. All fifteen cases start pending:
platform/customer isolation, direct Kitchen, original-cashier lock/resume, dual displays/
hotplug, approved gallery/cache/fallback, installer update, cash and fictional PromptPay,
Kitchen transitions, PDF/reprint, original-command recovery, cash variance, scope/revocation
denial, actual Singapore next-day, and settlement restart/ledger/CSV.

Use the frozen installer and hosted pilot on the actual intended ordinary Windows account.
Record device observations/PDFs and span real Singapore midnight on unchanged infrastructure.
Do not reinterpret fixture clocks, a solo developer source pass or prior first-draft
acceptance as these observations. Preserve the original sixteen-case UAT; its schema is
not extended by passing arbitrary new CaseIds. Phase 9 owns actual execution, defect/retest
recording and the user's final release decision. The generated companion never auto-accepts.

## Verification and unresolved gates

23 October 2026: the private review candidate is
`e740bafa-5499-49e5-8a5e-70cc4e60c3fc`, at HEAD
`3f47c61689f2d03540b1fc4a093cb4f6e5adf3d4`, **dirty source**. Its shared input hash is
`ce49accc61ec87fde76ac426fb4fcebc585a1b7e36ece6953a3a31e901c25e4f`.
The rebuilt Linux x64 bundle contains 945 files and thirteen schema targets. The rebuilt
private 0.8.0 installer remains unsigned, with sixteen unsigned payload PEs. The bound
artifact proof digest is `e896bb6cd559e5b236701eb728c8ee9117ea63ee6f11a7a9159f51dcbb165cad`.
These are review artifacts, not a frozen or installed release.

Earlier source-changing runs correctly failed the unchanged-source guard. A competing
publication also caused a frontend dependency-install failure; its fixture cleanup
passed, but the run did not pass verification. Dependencies were restored and publication/
verification were separated. Those failed/obsolete runs do not certify this candidate.
Final sequential `Joined` run `a630aef3-9a3c-4d5b-8b68-ca6ecfd38980` passed at
`2026-10-23T07:40:20.5246364+00:00` with the unchanged source binding above:

- 937 unit, 6 architecture and 30 selected HTTP-boundary tests.
- 46 hosting, 41 release, 10 production-identity and 15 installer guards.
- Frontend TypeScript, 76 unit tests, both portal builds and both dependency audits;
  production/full audits each reported zero vulnerabilities at execution time.
- 19 Kitchen, 7 gallery and 10 UI browser cases, without skips/retries/expected failures.
- Settlement reporting 9 cases, original-event replay 6 cases and gallery persistence 1
  case, all with verified owned-fixture cleanup.
- Normal-clock checkout/restore run `594618d869d941569c879cb6b8e6334f` passed all nine
  named checkout/interruption/broker/manager/restore checks with cleanup verified.
- Historical Singapore cutoff run `55bb7001b9a3462b8881a368f99898e5` passed eighteen
  browser scenarios and its Authorization persistence gate, with cleanup verified.

The private bounded receipt is
`.runstate/next-iteration-verification/a630aef3-9a3c-4d5b-8b68-ca6ecfd38980/verification.json`;
its SHA-256 `501d6c1348a3f9c87c943cc95d25cc29527e5a86d07e3abb4d516402e3da3d21` is bound
in the candidate's `verification-joined.json`. Readiness reports no missing artifact or
local source/joined-verification gate. Freeze and second-test preparation were actually
refused: neither `frozen.json` nor a second-test output directory was created.

Server facts, external hosted proof, publisher signing and VM/device cases remain
unconfirmed; clean exact-source remote CI has not been executed for this working tree.
The freeze tool retains those blockers instead of fabricating a frozen release or a
passed second user test.

## Documentation handoff

This change updates the root README, project overview and canonical project architecture
for shared source binding, release gates and their limits. ADR-043 records the decision.
The release-tool README, this guide, deployment guide, next-iteration roadmap, hosting and
Windows packaging READMEs describe commands, prerequisites and pending readiness. Frontend
workspace and both portal READMEs, the dependency policy and a dated historical-evidence
follow-up describe the patched build/test-tool pins without rewriting earlier results.

The canonical Restaurant/POS architecture, API contracts, database ownership/migrations
and identity guidance were reviewed and remain unchanged: this phase adds release/test
orchestration and additive artifact metadata, with no runtime workflow, service boundary,
HTTP contract, schema, claim, grant or authentication change. Existing Phase 6/7 operating
procedures continue to own deployment, restoration, installation and signing acceptance.
