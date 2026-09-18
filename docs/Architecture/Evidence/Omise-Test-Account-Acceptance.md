# Omise direct test-account acceptance

The operator's guarded direct Omise test-account gate passed on 2026-09-17. The retained sanitized summary was inspected after the operator reported success.

- Runner: `scripts/test-payment-omise-live-sandbox.ps1 -ConfirmSandboxTransactions`.
- Run: `c044f25cfa85423b94423d4c23fb7f37`.
- Evidence: `.runstate/payment-omise-live/c044f25cfa85423b94423d4c23fb7f37/summary.json`.
- Completion: `2026-09-17T15:20:49.1108771+00:00`.
- Provider: Omise; `testMode=true`; `passed=true`.

The summary confirms authorization replay, lost authorization-reference lookup, capture replay and status, pre-capture reversal replay and status, rejection of reversal after capture, and provider retention of trusted full-capture creation context. Credentials, tokens and provider references were not retained in the summary.

This closes the direct test-account adapter gate, including the signed-context compatibility path described in [ADR-008](../Decisions/ADR-008-omise-full-capture-context.md). It does not change the treatment of older unmarked charges or establish settlement evidence.

`hostedProcessInterruptionVerified=false`: this run uses direct application/adapter calls with in-memory intent persistence. It does not verify real Order/Payment host interruption, PostgreSQL leases, transactional outbox publication or RabbitMQ/Order inbox delivery against Omise. The earlier GenericHttp simulator matrix does not certify those Omise paths. The [Omise-compatible hosted acceptance harness](../../Deployment/Omise-Hosted-Recovery-Acceptance.md) is now implemented with explicit adapter selection and fresh-token fixtures; credentialed four-scenario execution passed on 2026-09-17; pre-authorization interruption and production release acceptance remain open. GenericHttp URL substitution alone is not sufficient.

POS/Order card-token handoff, 3DS, PromptPay, refunds, settlement, credential rotation/rate limits, webhooks and production onboarding remain outside this evidence. See the [operating guide](../../Deployment/Omise-Test-Account-Acceptance.md).

The separate [four-scenario hosted Omise gate](Omise-Hosted-Recovery-Acceptance.md) subsequently passed on 2026-09-17. This does not change this direct run's historical scope or its false hosted-interruption flag.
