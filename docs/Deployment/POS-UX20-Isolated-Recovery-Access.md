# POS UX20 — isolated recovery and access-control verification

## Delivered tooling and boundaries

`scripts/test-pos-recovery-access.ps1` runs existing POS regression and disposable owning-service
recovery fixtures serially under one before/after source binding. It rejects unconfirmed
disposable operations, non-Windows execution, explicit `DOCKER_HOST`, unknown/remote Docker
contexts and missing local fixture images. It clears inherited `NEXACONNECT_*` settings in
its own process before launching fixtures and restores them afterward; retained service
processes and their environment are not changed.

The generated fixtures create separate local PostgreSQL/RabbitMQ resources, synthetic scopes,
orders and test identities. Native/HTTP identities and dependent services are simulated;
this does not provision interactive Keycloak users or a second operator POS installation.
Real identity-provider expiry, actual operator recovery, installed-device acceptance and
production readiness remain separate. The existing [UX19 operator pack](POS-UX19-Cashier-Walkthrough.md)
is not edited or promoted by this runner. No reinstall or retained-stack restart is required.

## Run

Prerequisites: Windows with .NET 10 Desktop, PowerShell 7.4+, restored repository dependencies,
and local Docker Desktop with `postgres:17-alpine` and `rabbitmq:4-management` already available.
Use the current development account. Do not run competing verification/build producers in
this checkout while the command executes.

```powershell
pwsh -NoProfile -File scripts/test-pos-recovery-access-guards.ps1
pwsh -NoProfile -File scripts/test-pos-recovery-access.ps1 -ConfirmDisposableInfrastructure
```

The confirmation authorizes generated fixture resources, fixture-only migration rollback,
controlled termination of generated Order child processes and project-scoped cleanup.
It never authorizes interruption of the retained pilot. No external payment-provider request,
retained account login, real print job or physical drawer command is performed.

## Verification matrix

| Stage | Evidence and limits |
| --- | --- |
| UX20 producer guards | Reject missing/failed producer results, missing cleanup, incomplete counts and inappropriate readiness/operator flags; require explicit infrastructure opt-in. |
| Existing full POS regression | Unit tests, POS HTTP boundaries, isolated WPF and receipt-layout fixtures. Temporary protected state and fake identity responses exercise expiry, original-cashier fallback, original pending method/amount/idempotency recovery, authorization and tenant boundaries. See [UX9](POS-UX9-Acceptance.md). |
| Native drawer-history privacy | Existing tests cover denied access, selected-record review and delayed responses after privacy/identity change. UX20 adds reload after denial and history-service outage clearing, with unchanged journal bytes and no hardware invocation. |
| Disposable cashier recovery | Existing guarded cashier launcher creates random loopback PostgreSQL/RabbitMQ. At least eighteen protected SQLite/recovery tests and three owning POS tests verify original recovery, settlement projection/replay, broker recovery and fixture migration lifecycle; four Order receipt/authorization/migration cases and verifier contract tests run separately. Cleanup must pass. |
| Disposable Order recovery | Existing launcher starts a real generated Order host with fixture dependencies, interrupts it twice and exercises dependency outage/recovery. It verifies duplicate suppression, durable state and correlated workflow recovery; this is not an actual pending POS payment or external capture interruption. Cleanup must pass. |

Every producing child must exit successfully and create exactly one new run in its expected
directory. UX20 validates the child's required receipt fields, checks/counts and cleanup
markers, retains its run ID and receipt hash, and rejects source changes across execution.
No previous successful receipt is substituted. Failures remain failed; there is no automatic
retry. Existing producers own cleanup in their `finally` blocks; incomplete cleanup prevents
the combined pass and must be investigated using the generated project identity.

UX20 corrects stale fixture assumptions found during execution: protected SQLite coverage
now has eighteen tests, so recovery evidence records actual eighteen-plus-three counts rather
than expecting seventeen. The Order receipt/authorization/migration matrix now has four cases, so its launcher also records actual counts; current Order repository calls require the Order22 QR collection
table. The migration test retains its historical upgrade/rollback assertions before upgrading
to22 for current settlement behavior. It seeds a domain-created historical provider receipt
at schema11 so retained-receipt downgrade refusals remain exercised independently of current
manual settlement; no provider request occurs. The real-host recovery fixture also applies22. No new
application migration or retained schema change is introduced. Failed execution receipts remain
failed and are not relabelled after these corrections.

Execution checks guards first, then disposable cashier recovery, disposable Order recovery
and the complete POS regression, so owning-fixture failures stop before repeating broader checks.

## Evidence and remaining acceptance

The current-user-protected `.runstate/pos-recovery-access/<run-id>/` contains private producer
logs and `verification.json`. The receipt records source identity, executed producer results
and receipt hashes. It is `automated-pos-recovery-access`, with `operatorAcceptance=false`,
`retainedPilotMutated=false`, `liveOidcVerified=false`, `providerRequests=false`,
`releaseReady=false` and `productionVerified=false`. The existing child evidence stays in
its own generated directories; it is local diagnostic evidence, not a signed release artifact.

Passing this command does not record any of the remaining installed operator cases. Genuine
session expiry, interrupted-payment recovery, controlled network uncertainty, permission denial
and drawer-history privacy still need their documented actual observations in an appropriate
separate test context. Never shorten the retained policy, edit tokens, delete recovery, create
a replacement payment during uncertainty or grant broader production permissions to obtain a pass.
Scanner, dual-display and full staff/customer gallery acceptance remain deferred as documented
in UX19. Physical drawer opening remains disabled.

## Observed verification

Combined run `717a550de1a343e2acf59ce0db775e36` passed on 31 October 2026 at 23:10 Singapore
time. Its before/after source binding matches revision `2a486b90098d3479e2d512227e0487a53720d6d3`
(dirty working tree) and input hash
`12f138d00e4d401139b6add996f4823db15cdc7c1cd246edc9e004a4a1cf6697`.

- Twenty-nine UX20 producer/opt-in guards passed.
- Cashier run `01ad50bdb6bf40a4b73160bc58249a8f` passed 21 protected recovery/projection cases,
  four Order receipt/authorization/migration cases and fifteen verifier checks, with cleanup verified.
- Order run `3608e81ae7be469eb9a18cd9beb7632e` passed the real-host matrix: two process
  interruptions, dependency retry, preserved correlation, recovered settlement and zero duplicate
  transitions. Generated process/Compose cleanup passed.
- POS regression `bc8c3bc26c8f4643b55e9a8195ce3662` passed 1,390 unit tests, 49 HTTP cases,
  forty-nine acceptance/worklist guards and native input/session/shutdown/receipt-layout fixtures.
  Required .NET cases executed without skips. The new native history-outage assertions passed.

Earlier failed combined receipts remain failed. No application reinstall, retained-stack
restart, operator observation or production release was performed by this verification.
The installed operator pack remains nine passed, six pending and two blocked; a read-only
binding check confirmed its record unchanged. Real OIDC/operator/hardware acceptance remains open.
