# Phase 10 Notification provider-delivery evidence

## Scope

The Notification slice keeps PostgreSQL authoritative from request acceptance through provider receipt. Migration 3 adds leased submission/reconciliation, append-only attempt history, provider references, bounded retry state, and terminal timestamps. The Application policy owns accepted, retry, delivered, and failed decisions; Infrastructure claims and persists those decisions. Provider calls use Notification ID as the client reference and `Idempotency-Key`.

The public tenant API no longer accepts internal `SourceEventId` and requires matching organization/application context. `NotificationRequestedV1` remains the workload boundary. Safe `notification.delivery-status-changed.v1` and `notification.audit.v1` events contain identifiers/status only; recipient, subject, body, provider responses, and credentials are excluded.

## Live acceptance

On August 16, 2026, seven component cases passed against disposable PostgreSQL 17 and RabbitMQ 4:

1. Provider acceptance followed by receipt reconciliation to `delivered`, with append-only attempts/audit and no message content in outbox payloads.
2. Matching source-event replay returned the original notification while cross-organization reuse failed.
3. Two transient submissions exhausted the configured bound and atomically produced terminal failure audit/event state.
4. Notification migration 3 downgraded and reapplied cleanly while preserving migration-2 ownership.
5. An expired submission lease was reclaimed with the same Notification ID and a new lease.
6. Forced outbox failure rolled back provider state, attempt, and lifecycle audit together.
7. After a deliberately unreachable broker connection attempt, four queued/accepted lifecycle messages were published with confirms as persistent messages to an isolated queue and marked published.

The actual migration runner separately passed `0→3→2→3` against a generated database, preserving queued/outbox state and safely restoring accepted receipt work after destructive downgrade. Reporting migration 7 passed projection, downgrade cleanup, inbox-marker removal, re-upgrade, and replay against PostgreSQL 17. The real HTTP host passed `401`, tenant-context `403`, public source-event exclusion, create, and read cases. Focused unit coverage passed for provider headers/authentication/idempotency, exception classification without message content, domain validation, and Application delivery policy. Generated databases, containers, schemas, queues, and exchanges were removed.

## Limits

The acceptance proves recovery over a newly established RabbitMQ connection, not automatic reconnection of an already-running dispatcher. The HTTP adapter contract is tested with a controlled provider response; each concrete production provider still requires credential, quota, rate-limit, and contract acceptance. Polling is implemented; no webhook endpoint exists until a concrete provider supplies a signed callback contract.
