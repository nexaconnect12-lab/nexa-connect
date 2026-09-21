# Omise five-scenario hosted recovery acceptance

The operator reported **Order Omise recovery live acceptance passed**. The inspected sanitized summary is `.runstate/order-omise-recovery-live/955b56bdd53d4de499b4a3988341eea2/summary.json`, completed `2026-09-18T02:27:07.8969080+00:00`.

Five scenarios passed, including `intent_created` before authorization and four provider-response interruption fixtures. The summary reports `matrixPassed`, `cleanupPassed`, `intentCreatedBeforeAuthorizationVerified`, `cardTokenHandoffVerified`, hosted Payment recovery, outbox restart, authorization/capture/void boundaries, duplicate void handling and paid-Order protection true. Five provider-boundary and five Payment-host interruptions were counted. No duplicate durable command starts were detected, and secrets/raw service logs were not retained.

This pass closes the credentialed pre-authorization/token-handoff gate. It does not certify external webhooks, production card collection, 3DS, PromptPay, refunds, settlement or production activation. The fifth fixture uses workload authentication and direct authoritative responses; it does not claim cashier OIDC or a reconciliation inbox event for an operation that never became uncertain. [POS card verification](Omise-POS-Card-Acceptance.md) is separately recorded with operator-attested UI/OIDC/sign-out.

The historical [four-scenario evidence](Omise-Hosted-Recovery-Acceptance.md) remains unchanged. See the [hosted procedure](../../Deployment/Omise-Hosted-Recovery-Acceptance.md) for configuration and failure handling.
