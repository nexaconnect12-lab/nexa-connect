# Phase 11 Payment capture-recovery evidence

## Local execution

The coordinated capture-recovery matrix passed locally against PostgreSQL 17 and RabbitMQ 4 using generated disposable databases, isolated schemas/exchanges/queues, and a temporary login role with `CREATEDB` but no superuser privilege. The two generated base databases, runner-created databases, broker resources, and temporary role were removed after execution. The original 14-case run passed on 2026-08-26; the expanded 16-case run passed on 2026-08-28.

The current coordinated run passed 16 tests with no skips. It covered:

- Payment clean installation and `0→6→5→6` migration history/checksum/schema behavior;
- Order clean installation and `0→2→1→2`, including refusal to downgrade pending financial uncertainty;
- Reporting migration-11 capture-reconciliation projection removal and replay eligibility;
- authorization/capture concurrency and transaction boundaries;
- persisted `capture_unknown` recovery through a fresh repository instance, representing worker restart after uncertain capture;
- transactional `payment.capture.reconciled` audit plus lifecycle/audit outbox messages;
- RabbitMQ confirmed persistent publication over a new recovery connection after an unreachable connection attempt;
- tenant-leading reads, idempotent intent replay, append-only audit, and forced outbox rollback.

Focused automated coverage additionally proves malformed captured provider status without a provider reference remains unknown, authorization reconciliation cannot mark an Order paid, duplicate terminal delivery is a no-op, and a partial Inventory/Kitchen compensation failure retains `payment_pending` and succeeds on redelivery.

On 2026-08-28, the expanded coordinated matrix passed all 16 integration cases with no skips, and the focused compensation matrix passed all seven cases. The added crash-boundary case records an in-flight capture, receives `captured`, deliberately omits the local completion transaction, reconstructs the repository/recovery service after lease expiry, and reconciles the provider status without a second capture call. The added connection case establishes the production RabbitMQ transport, publishes one persistent event, closes the established client connection, and verifies a newly created connection publishes a second persistent event. Local Prometheus also loaded all eleven checked-in recovery rules and reported both the Collector and RabbitMQ scrape targets healthy.

The subsequently added destructive fault harness also passed locally on 2026-08-28. It launched a separate test process, exercised `HttpPaymentProvider` against a loopback HTTP capture fixture, emitted its marker only after the adapter returned a captured transaction, and intentionally killed that arm process. A fresh process then used a newly started scripted status fixture that reported the recorded intent as captured and passed the recovery test without repeating the capture command. This proves the HTTP adapter and local durable recovery boundary, not provider-side persistence semantics. Its broker test established the shared transport, verified that `nexa-connect-rabbitmq-1` was the Compose `rabbitmq` service publishing the configured loopback AMQP port, restarted it, waited for `rabbitmq-diagnostics ping`, published through the existing transport boundary, and consumed both persistent messages. The recovery and broker tests each passed with no skips; the arm process was deliberately terminated after its marker.

## Remaining release-environment gates

The follow-on provider-contract hardening adds a fail-closed `Disabled` default, explicit `GenericHttp` selection, HTTPS-only startup validation for that adapter, secret-injected bearer authentication, bounded timeouts, safe structured uncertainty logs, and conservative capture/void handling for transport failures, HTTP 408/429/5xx, and malformed successful payloads. Disabled mode makes no provider calls and does not register recovery workers. Run `scripts/test-payment-provider-sandbox.ps1 -ConfirmLocalSandbox`; it retains a summary plus TRX under `.runstate/payment-provider-sandbox/<run-id>/` and currently requires exactly 18 passing contract cases.

This local evidence now includes an operating-system child-process kill between HTTP provider response and database completion plus a full local broker-container restart. The synthetic contract suite does not perform a live TLS handshake against a concrete provider account and does not prove provider-side persistence, certificate-chain policy, credential rotation, or contractual quotas. Each release environment must still validate those concrete-provider concerns, metrics-backend alert delivery, and rollback/forward recovery; repeat broker restart when required by that environment's topology.

An opt-in concrete-provider harness is now available at `scripts/test-payment-provider-live-sandbox.ps1`. It requires an explicit transaction confirmation and secret-injected sandbox URL/key plus distinct disposable capture and void authorization references, enforced before execution. A successful run proves the real HTTPS handshake, acceptance of the configured bearer credential, and capture/void command replay and status lookup. Its successful JSON evidence contains booleans only; retained TRX and failed output are restricted diagnostic artifacts and must be reviewed before sharing. The harness has not been executed in this repository workspace because no provider account or disposable references are configured. Its summary deliberately keeps rate-limit and lost-response evidence false, and it does not claim invalid-credential rejection; provider-specific non-locking controls are needed to close those gates.
