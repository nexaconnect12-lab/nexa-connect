# Restaurant Service

Owns restaurant and branch hierarchy used by tenant authorization. Operational telemetry uses service name `nexaconnect-restaurant`; request logs exclude query strings, bodies, authorization headers, cookies, and arbitrary headers.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation query in the [observability guide](../../../docs/Deployment/Observability.md).
