# First-draft release hardening (slice 6)

This local Windows gate covers the THB / Asia/Singapore one-branch cash/manual PromptPay
draft. It builds on the [manager gate](First-Draft-Manager.md). It does not certify production,
bank transfers, manual-tender refunds, offline operation, fiscal compliance or physical devices.

## Candidate and verification

Use the [draft prerequisites](First-Draft-Test-Stack.md), including PowerShell 7.4+, local
Docker Desktop and installed Chromium. From the repository root:

```powershell
./scripts/test-first-draft-release-guards.ps1
./scripts/test-first-draft-release.ps1 -ConfirmDisposableInfrastructure
```

The gate creates `.runstate/first-draft-release/<id>`. `candidate.json` records HEAD,
working-tree status, executable input paths/hashes and a combined SHA-256. Inputs include
tracked and nonignored new files in source, tests, scripts, Docker and workflows, plus root
build configuration. Markdown and image/PDF documentation are excluded so execution evidence
can be recorded afterward. This is an input identity, not a signature, cache hash or clean Git
release. Stop editing executable inputs while the gate runs: changed hashes or child HEADs
fail the gate. `build-artifacts.json` records published draft service/POS binary and portal
asset hashes. Child receipts record their scope and cleanup separately.

Required gates are:

- Lifecycle, terminal, manager, release and UAT safety guards.
- Unit with Windows DPAPI acceptance enabled, Architecture and Payment health HTTP tests;
  every required TRX test must execute and pass without skips.
- Fresh `npm ci --ignore-scripts`, TypeScript check, frontend tests, both portal builds,
  moderate-or-higher production/full dependency audits and browser settings/reporter guards.
- Day-seal/source PostgreSQL matrix, including reachable/stale/unavailable Payment database
  readiness; settlement Reporting and restricted replay PostgreSQL/RabbitMQ matrices. Typed
  child receipts require exact generated paths, source revision, counts and verified cleanup.
  The PostgreSQL-only gate keeps its documented hosted-broker exclusions; separate broker
  gates and joined scenarios exercise the selected Reporting/replay/delivery behavior.
- Cash-close Prometheus rule validation and synthetic rule evaluation.
- Five real-OIDC cash-close portal cases with six Authorization persistence cases,
  including cash-review-authorized branch selection and live denial/outage behavior.
- Normal-clock cashier/Kitchen, placement/settlement replay, cancellation, process loss,
  broker interruption, manager review, isolated restore and resumed evidence reads/replay.
- Eighteen real-OIDC historical Singapore cutoff/seal/approval/finalization/CSV cases and six
  Authorization persistence cases, with no skips/retries and exact cleanup.

Failures retain failed receipts; prior success cannot satisfy a new run. Final
`verification.json` describes **local** success only. Clean-checkout CI, external receiver
delivery, printing, real next-day and production acceptance remain separate false flags.
See [executed evidence](../Architecture/Evidence/First-Draft-Slice-6.md) for actual outcomes.
Bundle-size advisories do not replace independent dependency-audit exit-code gates.

Before distribution, commit the reviewed candidate, rerun from the intended clean checkout
and retain passing CI for that exact commit. Base-commit CI does not attest these working-tree
changes. No automatic commit, push or workflow dispatch occurs.

Use the [Slice 7 UAT record and handover](First-Draft-UAT.md) to bind the new candidate
receipt to a retained user environment and a package with the same source input hash.
All required user cases remain explicit operator attestations; a local gate never marks
Windows/PDF, actual next-day or final user acceptance as passed.

## Isolated restore drill

The release runner invokes `test-first-draft-restore.ps1` after disposable manager checkout.
Do not use the sole retained user-test environment. The script requires its matching checkout
journal and an exclusively stopped owned draft. Lifecycle Stop verifies process identity and
stops applications/infrastructure; the drill holds the lifecycle lock, validates scope and
starts only both database containers. Keycloak, application writers and dispatchers stay
stopped during backup. Financial rows are not seeded, rewritten or downgraded.

It captures roles/password hashes and custom logical dumps of ten owning service databases
and the complete Keycloak database. Two fresh PostgreSQL clones use each source's exact image
ID, generated bootstrap roles, no network, no published ports and temporary storage. Restore
uses stop-on-error for roles, owning databases, schema, data and permissions. Data-only dump
hashes compare all emitted data and sequence state against the quiesced originals; only
pg_dump's random restriction guards are omitted. Separate schema-only dumps compare emitted
owner, GRANT, REVOKE and default-privilege statements. Each database receipt records
`dataAndSequencesMatch`, `ownershipAndPermissionsMatch` and `schemaRestoreVerified`; the last
means the original archive's DDL restored successfully with `pg_restore --exit-on-error`.
PostgreSQL can flatten equivalent nested AND expressions when parsing CHECK constraints,
so this drill does not claim byte-identical schema dumps or an independent semantic
comparison of constraint expressions. It neither removes CHECK expressions nor changes
their restoration. This is administrative recovery tooling, not cross-owner application persistence.

Protected backups live under `.runstate/first-draft/<run-id>/restore-drill/<id>`. Lifecycle
metadata, protected credentials, TLS certificate, reference/POS settings and Customer BFF key
ring are copied to a separate configuration tree and checksum-checked. Credential decryption
is verified under the **same Windows account**. Only label-verified clones are removed and
their absence checked. Original retained binaries/ports resume; fresh PKCE identities read
receipts/review and replay the original settlement. The outer checkout retires its own draft.

This verifies database/configuration restoration, not login or a restored application graph
against cloned stores. Broker and Redis storage are not restored; resumption uses original
volumes. Pending outbox recovery is tested separately. Application rehydration, broker-loss
rebuild/replay and cross-machine recovery require another reviewed deployment drill. No
RPO/RTO or point-in-time recovery is claimed. Real coordinated recovery must quiesce **all**
writers; independent sequential database dumps otherwise provide no cross-service cut.

Backups contain financial records, identity password hashes, keys and credentials. Keep
owner/SYSTEM ACLs, retain locally and exclude from Git, shared evidence and CI artifacts.
The allowlisted receipt is separate from private diagnostics. Use exact verified paths for
authorized retention cleanup. Failed drills retain stopped source data/private backups for
investigation; the outer disposable checkout still attempts cleanup. Do not overwrite
original databases or delete immutable financial history to manufacture recovery.

## Windows POS and forward recovery

The drill never copies `%LOCALAPPDATA%\NexaConnect\POS`. Protected SQLite journals, pending
checkout/settlement identities and tokens belong to the original account/scope; server
backup does not make the terminal portable to another device/account. Preserve the terminal
and use [read-only recovery preflight](First-Draft-Windows-POS.md). Do not purge state to
change branches, reinstall or clear uncertain collection. DPAPI tests use disposable state.

For financial incidents, suspend new writes, preserve receipts/audit/barriers and identify
the original operation before retry. Use owning recovery workers and reviewed original-event
replay, followed by delivery comparison. Broker confirmation alone is not projection proof.
Used financial schemas can refuse downgrade: deploy a compatible forward fix, never forced
history removal. Negative cash movements/late corrections are not manual-tender refunds.
See [settlement recovery](Day-Close-Settlements.md), [replay](Settlement-Replay.md) and
[cash-close recovery](Cash-Close-Recovery.md).

## Readiness, telemetry and operator response

Lifecycle `Ready` is not current dependency readiness. Inspect process identity, Payment
`/health/ready`, schema targets, workload audiences and financial bindings/consumers separately.
Order/POS/Reporting `/health/live` proves host liveness only. Joined workflows provide
separate authorized financial evidence. The existing Reporting OpenAPI schema-export defect
is not used as readiness and remains tracked in the stack guide.

Shared observability tests cover correlation validation and redaction. Structured JSON logs
remain private and never replace financial audit. Follow validated `CorrelationId` across
`nexaconnect-customer-bff`, `nexaconnect-order`, `nexaconnect-pos`, `nexaconnect-reporting`.
When centralized export is explicitly configured, use:

```logql
{service_name=~"nexaconnect-(customer-bff|order|pos|reporting)"} | CorrelationId="draft-investigation-001"
```

Use [cash-close queries and six alert rules](Cash-Close-Recovery.md) for outbox age,
collection failure, queue/dead-letter depth and publication/consumer retries. For uncertain
checkout, reconcile the original receipt before collecting again. On source outages, suspend
finalization/export, repair the dependency and reload evidence. For delivery gaps, inspect
bindings/consumers before reviewed replay. Never share tokens, cookies, bodies, payment data,
unrestricted personal data or raw private logs.

Standalone draft OTLP export is disabled and no alert stack is provisioned. Repository local
Alertmanager sends **no external notifications**. Synthetic rule tests do not prove inbox
delivery, paging or acknowledgement. The solo tester watches status/results and investigates
failures manually. Production needs an authenticated receiver and delivery drill; see
[observability](Observability.md) and [Alertmanager](../../docker/alertmanager/README.md).

The [manager next-day drill](First-Draft-Manager.md) and interactive Windows/PDF acceptance
remain user gates before Slice 7 handover.
