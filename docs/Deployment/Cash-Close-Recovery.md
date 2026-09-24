# Cash-close Reporting recovery and replay

The replay CLI republishes retained original POS `pos.cash-close.snapshot.v1` events for one organization/branch/store and a bounded capture-time range. It changes only POS replay audit tables; it does not change financial records, publication checkpoints, source event payloads, event IDs or outbox publication flags. Reporting remains an eventually consistent projection. See [the API contract](../API/Cash-Close-Reporting.md) and [ADR-012](../Architecture/Decisions/ADR-012-cash-close-snapshot-reporting.md).

## Prerequisites and authority

Apply POS migration 7 with application version at least `0.16.0`; Reporting remains at migration 15. Use a dedicated operational database login with schema usage, `SELECT` on `outbox_messages`, and `INSERT` on `cash_close_replay_runs` and `cash_close_replay_attempts`. Give audit investigators `SELECT` on those audit tables. The tool needs no UPDATE/DELETE on the outbox, financial tables or checkpoints, no schema-owner privileges and no Reporting database credentials. Use a separate migration identity for DDL. Preview needs only source SELECT access.

The replay CLI is an administrative database/broker operation, not a Customer API. Its operator UUID is self-asserted attribution, not authenticated identity. The immutable run records also store PostgreSQL `session_user` as `database_actor`; controlled login issuance and operational authorization are essential. There is no OIDC or product-permission check in the CLI. Keep credentials in the execution environment/secret store, never command arguments, shell history or logs.

Use the intended broker virtual host and exchange. The shared transport declares the durable topic exchange and publishes persistent messages with mandatory routing and publisher confirms; grant configure/write permissions scoped to that exchange. It does not create the Reporting queue. Ensure the consumer has declared and bound its main/dead-letter queues and started consuming before replay. Every subscriber matching the routing key must tolerate original-event replay: publication to the exchange broadcasts to all matching bindings, not exclusively Reporting.

A publisher confirmation proves broker acceptance and routing to at least one queue. It does not prove the Reporting queue was bound, that projection succeeded, or that the store is complete. Independently verify Reporting binding/readiness before execution and facts/receipts afterward. `WaitUntilReadyAsync` is an in-process first-start signal after declarations, bindings, QoS and consumer registration; it is not a continuously evaluated health endpoint.

## Preview and execute

Build once from the repository root:

```powershell
dotnet build src/Tools/NexaConnect.CashCloseReplay/NexaConnect.CashCloseReplay.csproj
```

Inject `NEXACONNECT_CASH_CLOSE_REPLAY_DB` as the POS connection string. Execution also needs `NEXACONNECT_CASH_CLOSE_REPLAY_BROKER`; optional `NEXACONNECT_CASH_CLOSE_REPLAY_EXCHANGE` defaults to `nexaconnect.events`. Preview does not contact the broker. Set `OTEL_EXPORTER_OTLP_ENDPOINT` only when optional OTLP log export is required; JSON console logging is always available under `nexaconnect-cash-close-replay`. No populated credential file is required.

Replace the example UUIDs with the authorized scope. Times must end in `Z`. The inclusive/exclusive capture range is at most 31 days; it filters event occurrence/capture time, not cash-session closure time. A limit of 1–1000 is mandatory. More matching events than the limit rejects the selection instead of truncating it; narrow the range.

```powershell
$replayDll = 'src/Tools/NexaConnect.CashCloseReplay/bin/Debug/net10.0/NexaConnect.CashCloseReplay.dll'
$selection = @(
  '--organization', '11111111-1111-1111-1111-111111111111',
  '--branch', '22222222-2222-2222-2222-222222222222',
  '--store', '33333333-3333-3333-3333-333333333333',
  '--from', '2026-09-01T00:00:00Z',
  '--to', '2026-09-02T00:00:00Z',
  '--limit', '1000'
)
dotnet $replayDll @selection
```

The default read-only preview prints `{ "mode": "preview", "Manifest": "...", "Count": ... }`, without source payloads or amounts. Review scope and count and retain its SHA-256 manifest. The manifest binds exact filters, limit, event IDs and retained payload hashes. Execution reselects and refuses a changed manifest before creating audit intent or sending events. A zero count does not prove a complete report. New captures within a moving range may require another preview.

Execute the reviewed selection with the exact manifest:

```powershell
dotnet $replayDll @selection --execute `
  --operator '44444444-4444-4444-4444-444444444444' `
  --reason rebuild `
  --manifest '<Manifest returned by preview>'
```

Allowed reason codes are `rebuild` and `retry`; arbitrary notes are not accepted. The process is bounded to ten minutes and Ctrl+C requests cancellation. Successful execution logs the run UUID and returns exit code 0; failure returns 1 with a safe diagnostic. Audit/send interruption logs the run UUID; if initial audit persistence failed, the corresponding row may not exist. The tool sorts the selected events by UUID for deterministic replay; Reporting version checks, not publish order, preserve the newest fact.

## Failure recovery and audit

An append-only run records scope, capture range, selection limit, operator/database attribution, reason, manifest and count before any broker call. Each event records `started` before send and `confirmed` after the broker confirmation. A run may be partial. `started` without `confirmed` means uncertain delivery, including when the broker accepted but the audit confirmation failed. Do not interpret it as a failed projection or mint replacement event IDs.

Inspect audit outcomes using a restricted connection, without selecting financial payloads:

```sql
SELECT r.id, r.database_actor, r.operator_id, r.reason, r.event_count,
       count(*) FILTER (WHERE a.outcome = 'started') AS started,
       count(*) FILTER (WHERE a.outcome = 'confirmed') AS confirmed
FROM cash_close_replay_runs r
LEFT JOIN cash_close_replay_attempts a ON a.run_id = r.id
WHERE r.id = @run_id
GROUP BY r.id;
```

Bind `@run_id` as a parameter in the operator's database client. Verify exchange/bindings and Reporting state, then preview again and execute `--reason retry` with the original retained events if necessary. A retry creates a new run; duplicate receipt/version checks protect Reporting. Neither a completed run nor its audit counts certify report completeness.

For a Reporting `15→14→15` rebuild, stop affected reads/consumption around the destructive migration, retain source events, re-upgrade, establish the consumer binding, and replay all required authorized capture windows before checking projected versions and scope. Pausing capture/dispatch can stabilize a manifest; use fixed historical end times. Repeating a full original-event selection is safe for replay-aware subscribers. The tool cannot rebuild events that were purged or never captured. Restore an authorized retained source before retrying; never delete publication checkpoints to manufacture new snapshots. It does not drain dead letters, purge source events or automatically reconcile missing ranges.

POS migration `7→6` refuses once any replay run exists, including an empty run; audit tables reject UPDATE, DELETE and TRUNCATE. Preserve audit and use forward recovery after use. Existing POS-6 publication-history downgrade protection remains unchanged. Retain source financial payloads and replay audit with access-controlled backups and an environment-owned retention policy; payloads must not enter ordinary evidence bundles.

## Monitoring

POS backlog monitoring starts when either publication or outbox dispatch is enabled and samples every 15 seconds. Gauges retain their last successful values on query failure; use the failure counter to identify stale metrics. These gauges cover captured unpublished cash-close outbox events, not uncaptured source work or Reporting completeness. Service OTLP export uses the existing `Observability__OtlpEnabled` and `Observability__OtlpEndpoint` settings. The CLI exports optional logs only; it does not add a standalone metrics host.

| Metric | Meaning |
| --- | --- |
| `pos_cash_close_outbox_pending` | Unpublished captured snapshots |
| `pos_cash_close_outbox_oldest_age_seconds` | Age of oldest unpublished snapshot |
| `pos_cash_close_metrics_failures_total` | Backlog collection failures |
| `pos_cash_close_publication_outcomes_total` | `queued`, `retry`, `scan_failure` |
| `reporting_cash_close_outcomes_total` | `applied`, `replayed`, `rejected`, `retry`, `connection_retry` |

Six [Prometheus rules](../../docker/prometheus/rules/cash-close-reporting.yaml) cover outbox age, collection failure, Reporting backlog/dead letters and consumer/publication retry. Queue rules assume the default main/dead-letter names and require RabbitMQ queue metrics; customize them for different names. Tune thresholds and validate exporter names, rule evaluation and receiver delivery in the release environment. Per-instance outbox gauges describe the same database; use `max`, not a sum, across replicas. Local Alertmanager does not page anyone. No completeness watermark is introduced.

Query Loki `{service_name="nexaconnect-pos"} |= "cash-close"`, `{service_name="nexaconnect-reporting"} |= "Cash-close"`, and `{service_name="nexaconnect-cash-close-replay"}`. Logs exclude amounts and retained payloads. Event correlation is preserved through replay; durable audit is the authority for partial replay outcomes.

## Disposable recovery acceptance

Run from the repository root with Docker Compose, .NET and capacity for disposable PostgreSQL 17/RabbitMQ 4:

```powershell
./scripts/test-cash-close-recovery.ps1 -ConfirmDisposableInfrastructure
```

The explicit switch authorizes generated disposable infrastructure, test child-process termination and removal of that project's volumes. The runner creates `cashclose-<guid>` with generated credentials and dynamically selected loopback-only database/broker/management ports explicitly bound across restart; it uses generated source/sink schemas, not existing application databases. It builds both tools and the integration project unless `-NoBuild` is used after a verified build. In `finally`, it removes its Compose resources/volumes and restores environment variables. Child-process cleanup belongs to the tests; a forced parent termination can bypass normal cleanup and requires investigation of the exact run resources.

Exactly two `CashCloseProjectionPostgresTests` cases must pass without skips. Coverage includes SQL lifecycle/rollback, concurrent publication, real outbox dispatcher and consumer processes, termination after projection commit before acknowledgement, broker outage/restart, late-review invalidation, reverse duplicate delivery, poison dead letters, Reporting-15 destructive rebuild through the replay CLI twice, audit immutability, restricted PostgreSQL/RabbitMQ credentials with denied-operation probes, recorded database login identity and unchanged source financial/checkpoint/outbox fingerprints. These fixtures call real repositories; they do not certify a live OIDC/portal/Restaurant workflow, full migration-runner acceptance, production topology, alert delivery or financial completeness.

The runner writes `cash-close.trx` and bounded marker files under `.runstate/cash-close/<run-id>` during execution, including failed runs. It writes allowlisted `verification.json` in cleanup with test counts, source revision/dirty status and cleanup outcome; `evidence.json` is written only after both tests and cleanup succeed. Restrict retained test artifacts; do not add connection strings or financial payloads. Local Windows recovery and restricted replay passed 2/2 with zero skips on 2026-09-24; see [evidence](../Architecture/Evidence/Cash-Close-Recovery-Acceptance.md). Docker is discovered in PATH or the per-user Windows Docker Desktop installation; earlier checks missed the latter. Fixture PostgreSQL clock timestamps avoid host/database skew. Remote CI, deployed least privilege, retained-source backup restoration, production acceptance and receiver delivery remain release gates.

## Automated gate and rule verification

Run `./scripts/test-cash-close-alerts.ps1` from the root for `promtool check rules` and `promtool test rules` in network-disabled `prom/prometheus:v3.5.0` with read-only rule files. All six healthy/pending/firing/resolved tests and the exact outbox threshold passed locally on 2026-09-24; live scraping and receiver delivery are separate checks.

The [GitHub workflow](../../.github/workflows/cash-close-verification.yml) runs alerts then the no-skips recovery matrix on Ubuntu 24.04 for pull requests/manual dispatch using .NET 10, read-only repository permissions and generated credentials. It uploads only recovery `verification.json`/`evidence.json` and alert `verification.json` for 14 days; raw TRX, marker folders, logs and payloads are excluded. Remote execution and required branch-protection status have not been configured or verified.

Restricted replay acceptance creates a PostgreSQL login with source SELECT/audit INSERT only and an exact-exchange configure/write RabbitMQ identity. Forbidden source/checkpoint/audit/schema operations and broker queue/other-exchange operations fail; the final matrix also proves default-exchange publication is denied. Separate investigators may receive audit SELECT. This verifies the fixture grants only, not production accounts.
