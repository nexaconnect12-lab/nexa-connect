# Phase 11 Payment capture-recovery evidence

## Local execution

On 2026-08-26, the coordinated capture-recovery matrix passed locally against PostgreSQL 17 and RabbitMQ 4 using generated disposable databases, isolated schemas/exchanges/queues, and a temporary login role with `CREATEDB` but no superuser privilege. The two generated base databases, runner-created databases, broker resources, and temporary role were removed after execution.

The final coordinated run passed 14 tests with no skips. It covered:

- Payment clean installation and `0→6→5→6` migration history/checksum/schema behavior;
- Order clean installation and `0→2→1→2`, including refusal to downgrade pending financial uncertainty;
- Reporting migration-11 capture-reconciliation projection removal and replay eligibility;
- authorization/capture concurrency and transaction boundaries;
- persisted `capture_unknown` recovery through a fresh repository instance, representing worker restart after uncertain capture;
- transactional `payment.capture.reconciled` audit plus lifecycle/audit outbox messages;
- RabbitMQ confirmed persistent publication over a new recovery connection after an unreachable connection attempt;
- tenant-leading reads, idempotent intent replay, append-only audit, and forced outbox rollback.

Focused automated coverage additionally proves malformed captured provider status without a provider reference remains unknown, authorization reconciliation cannot mark an Order paid, duplicate terminal delivery is a no-op, and a partial Inventory/Kitchen compensation failure retains `payment_pending` and succeeds on redelivery.

## Remaining release-environment gates

This local evidence does not stop a live worker process between provider response and database commit, use production provider credentials, or prove automatic recovery of an already-established RabbitMQ connection. Each release environment must still execute provider delayed/dropped response cases, process termination, TLS/credential/rate-limit validation, established-connection recovery, metrics-backend alert delivery, and the rollback/forward-recovery rehearsal in the Payment capture-recovery runbook.
