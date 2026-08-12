# Authorization Service

Owns product-scoped authorization decisions and role assignments. Operational telemetry uses service name `nexaconnect-authorization`; decision logs contain permission and UUID scope but never bearer tokens, identity credentials, request bodies, customer PII, or payment details.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation and denial queries in the [observability guide](../../../docs/Deployment/Observability.md).
