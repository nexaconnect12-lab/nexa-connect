# ADR-029: Durable branch-day settlement and late-work custody

Status: Accepted and implemented; local 81-case PostgreSQL and sixteen-scenario real-OIDC acceptance passed with verified cleanup, 2026-10-08. Remote CI and production acceptance remain unverified.

## Context

ADR-028 preparation leases expire. Final settlement needs an irreversible decision that survives response loss, source outages and a manager losing access, while preserving the exact reviewed financial evidence. No transaction can hold locks in several owning databases across HTTP.

## Decision

POS owns a durable authorized intent, commit/abort decision, immutable receipt, append-only audit and original `pos.branch-day-settled.v1` publication. Admission requires live `pos.day-close.read` and `pos.day-close.finalize`, a freshly validated approval, and exact reviewed preparation/approval versions and operation identities. The local intent transaction locks seal, approval and preparation before creating settlement. Customer retries require the original actor and exact command; Authorization decision IDs and workload recovery metadata remain server-side.

Order, Payment and POS each own a permanent barrier against their exact preparation lease, approval, source seal and financial window. The owning Domain admits transitions. Initial arming needs a live lease with a 15-second reserve. Once armed, neither expiry nor customer cancellation releases protection. A terminal commit or abort binds a decision UUID and never reverses. Aborting an unarmed source retains a tombstone and cancels its original lease. Source generation changes serialize old repeatable-read writers; financial state and journal changes roll back together on rejection. Known unrelated next-day/other-branch changes remain legal.

The existing POS client-credentials identity drives the internal source protocol. Each source requires the validated API audience, authenticated `azp=nexaconnect-pos-service` and `preferred_username=service-account-nexaconnect-pos-service`. Customer bearer tokens cannot drive it. This identity is trusted to convey the POS-owned durable decision; sources do not query the POS database or call back during a transaction. Its credential is therefore a coordinator capability and must remain restricted to POS.

Only three matching armed acknowledgements permit commit. POS writes decision, original receipt, audit and outbox row in one transaction. The receipt copies the original approved snapshot, not newly calculated totals. Status `committing` means a durable commit exists but source acknowledgement delivery is incomplete; `finalized` requires all three commit acknowledgements. A permanent arming conflict chooses abort; transport, credential, proof-validation and storage failures retain the intent for recovery. Competing workers must return the already-recorded decision before sending its source phase. No opposite decision or timeout-based unlock is permitted.

The opt-in POS recovery worker processes bounded batches without a customer session. Authorization is delegated by the already committed intent; revocation stops new commands/retries, but does not prevent completion of that intent. All source calls occur outside local database transactions. Recovery preserves the validated correlation context and emits bounded JSON/OTLP telemetry. An aborted attempt remains retained; a new attempt requires fresh preparation and a new operation identity.

Allow-listed late financial deliveries use independently owned append-only custody. POS manual tender, Order payment reconciliation and canonical Payment webhook recovery catch durable-barrier rejection only after the owning transaction rolls back. They resolve actual owning record/parent state and apply the owning financial-window policy; payload custody is normalized and bounded, with exact identity/content hash and immutable barrier links. Arbitrary provider failure text, provider identifiers and webhook bodies are excluded. An armed-only hold remains unacknowledged and retries; abort permits the original delivery to apply normally. A committed barrier transfers delivery ownership only after custody commits, without changing money, journal, original receipt or snapshot. Duplicate custody is exact and changed content conflicts. Held history remains retained after abort.

## Consequences

Permanent barriers intentionally fail closed during dependency outages. Operators restore credentials, hosts and databases; they do not remove barriers or rewrite decisions. GET returns durable progress and attempts fresh source counts; unavailable counts are explicitly marked saved. Accountants have read-only access by default. There is no new Reporting projection: the outbox event is a versioned integration fact for future consumers. Correction posting, fiscal adjustment, accounting exports and branch-offline finalization remain separate work.

The protocol favors retained evidence and recovery over automatic unlock. It is not a provider transfer, tax settlement or distributed database transaction. Deploy the coordinated migrations, credentials, source endpoints, recovery worker and matching portal together.

See [API](../../API/Day-Close-Settlements.md), [rollout and recovery](../../Deployment/Day-Close-Settlements.md) and [acceptance evidence](../Evidence/Day-Settlement-Acceptance.md).
