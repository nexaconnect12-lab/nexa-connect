# Omise hosted recovery acceptance

The operator reported successful hosted Omise recovery acceptance. The retained sanitized summary and detailed matrix evidence were inspected on 2026-09-18; the run itself completed on 2026-09-17.

- Run: `f62f96cbb36b4ccbb1eb366bf930cea9`.
- Completion: `2026-09-17T15:46:19.5403593+00:00`.
- Summary: `.runstate/order-omise-recovery-live/f62f96cbb36b4ccbb1eb366bf930cea9/summary.json`.
- Details: the same directory's `provider-recovery-evidence.json`.
- Provider: Omise; test mode; four scenarios; matrix and cleanup passed.

Four provider-response boundary interruptions and four Payment-host interruptions passed. The scenarios cover lost authorization-reference recovery, capture response loss, pre-capture reversal response loss for an unpaid Order, and a defensive already-paid Order fixture receiving delayed reversal reconciliation. Real hosted Payment recovery, PostgreSQL transactional-outbox persistence during broker outage, RabbitMQ restart/publication, persistent evidence delivery and Order inbox completion passed. Reversal duplicate delivery and paid-Order protection passed. No duplicate durable command starts were detected. The retained summary reports no secrets or raw service logs; detailed evidence excludes provider references.

This closes the four-scenario hosted Omise test-account recovery gate described in the [operating guide](../../Deployment/Omise-Hosted-Recovery-Acceptance.md), alongside the earlier [direct adapter gate](Omise-Test-Account-Acceptance.md). The direct run's historical `hostedProcessInterruptionVerified=false` remains accurate for that separate run.

`intentCreatedBeforeAuthorizationVerified=false` remains an explicit exclusion: the harness supplies an ephemeral token for initial authorization, and production POS/Order token forwarding is not implemented. The paid-protection scenario is an artificial delayed-event fixture, not reversal of a captured provider charge. Evidence counts durable local command starts and instrumented arm calls, not every hosted provider HTTP request. Workload authentication uses the local harness fixture, not live OIDC.

POS/Order card-token handoff and pre-authorization interruption, production release-environment acceptance, webhooks, key rotation, 3DS, PromptPay, partial capture, refunds, settlement and partial-compensation process-loss acceptance remain separate work. No financial commands were issued while inspecting or recording this result.
