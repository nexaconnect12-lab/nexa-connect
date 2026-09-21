# ADR-010: Verified Omise test webhook recovery

Status: Accepted for Development/Testing, 2026-09-18.

## Context

Five-scenario hosted Omise recovery and local POS card verification have passed. Payment still depends on status polling after response loss. Incoming provider notifications need authentication, tenant binding, durable deduplication and failure recovery without a second financial command. Omise supports timestamped HMAC-SHA256 signatures and authenticated event retrieval; an event's historical embedded charge is not necessarily its current financial state.

## Decision

Payment owns an opt-in test-only signed webhook route and migration-8 inbox. Verify timestamped raw-body signatures before enqueueing only a bounded test event ID and validated correlation ID. A scoped worker retrieves the canonical event through the official authenticated Events API, validates test mode/type/metadata and binds it to the exact local intent, Order, organization, amount/currency and existing provider authorization reference when present.

Application behavior acquires the existing expired financial recovery claim and invokes existing status-only reconciliation. It does not issue authorize/capture/reverse/refund commands or trust delivered/snapshot financial flags. Payment's aggregate, tenant-leading persistence, version fencing and transactional audit/outbox remain the financial authority. Normal Order orchestration can resume its own capture after authoritative authorization reconciliation.

Inbox completion is a separate fenced transaction. A crash after Payment commits but before inbox acknowledgement replays safely against terminal state. Active financial leases defer processing. Unsupported/foreign events are rejected; temporary errors retry with bounds; exhausted deliveries remain review evidence while polling continues. No raw bodies, signatures, charge references, keys or card details are persisted in the inbox or telemetry.

The inbox retains the validated trace correlation string alongside the financial correlation UUID. Incoming UUIDs are reused; other trace identifiers receive a generated financial UUID bridge. The worker exports a bounded Consumer span and outcome metric under `nexaconnect-payment`; automatic outbound HTTP instrumentation is suppressed to prevent provider references in URL telemetry while correlation headers still propagate.

## Alternatives and consequences

Trusting delivered financial flags would permit stale or forged transitions. Event retrieval alone verifies provider data but cannot protect ingress from forged queue/API load; signatures and a bounded per-process limiter protect this test endpoint. Directly mutating Payment state from historical event payloads would bypass existing financial invariants and outbox guarantees. Reusing existing recovery claims avoids a second financial state machine.

Production enablement remains rejected. Public HTTPS delivery/configuration, actual signed Omise delivery acceptance, catch-up from Events API, distributed rate limits, retention/purge, audited replay and production webhook-secret operations remain separate work. Omise does not guarantee delivery retries, so notifications supplement polling rather than replacing it. See the [deployment guide](../../Deployment/Omise-Webhooks.md) and [official signature specification](https://docs.omise.co/api-webhooks/thailand).
