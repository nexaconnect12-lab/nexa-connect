# Phase 11 payment-review live verification

## Scope

The checked-in acceptance matrix closes the code-owned Payment Review operational gates without broadening the financial workflow. It covers Order migration `0→4→3→4`, transactional case resolution and downgrade refusal, Reporting migration-13 projection removal/replay, persisted Order outbox retry followed by confirmed persistent RabbitMQ publication over a new connection, and isolated Alertmanager firing/resolved delivery.

Run it only against disposable resources:

```powershell
.\scripts\test-payment-review-operations.ps1 -EvidenceLabel staging-disposable -ConfirmDisposableInfrastructure -ConfirmAlertDelivery -ConfirmDestructiveRollback
```

Inject `NEXACONNECT_ORDER_INTEGRATION_DB`, `NEXACONNECT_REPORTING_INTEGRATION_DB`, `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB`, and `NEXACONNECT_RABBITMQ_INTEGRATION_URI` through the process environment or secret mechanism. The script rejects missing or production-looking values, creates only generated acceptance resources, deletes its isolated broker resources and alert containers, restores opt-in environment variables, and retains a TRX under `.runstate/payment-review-operations/<run-id>/`. `EvidenceLabel` is descriptive metadata only; it does not select or validate infrastructure, so operators must independently record the target identity.

## Evidence boundary

The RabbitMQ case uses synthetic `{}` rows with the production outbox store and transport. It proves that unpublished rows remain retryable after a recorded transport failure and that a fresh connection publishes `order.payment-review-required.v1`, `order.payment-review-resolved.v1`, and `order.audit.v1` as persistent confirmed messages; it does not prove repository-produced payload serialization or hosted-dispatcher replay. Reporting replay uses the original event identity after removing the incompatible projection and inbox marker. The alert exercise proves the configured Alertmanager route can deliver both states to the isolated receiver.

This does not certify production receiver authentication, escalation, paging, acknowledgement, concrete SLOs, or the operator UI. Those remain environment/product-owned release evidence. A successful environment run must record its date, infrastructure identity (without secrets), TRX path, and production alert-routing evidence in the release record.
