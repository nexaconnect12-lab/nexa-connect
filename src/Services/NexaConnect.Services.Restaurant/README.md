# Restaurant Service

Owns restaurant and branch hierarchy used by tenant authorization. Operational telemetry uses service name `nexaconnect-restaurant`; request logs exclude query strings, bodies, authorization headers, cookies, and arbitrary headers.

Platform owners and administrators provision hierarchy through `POST /api/restaurant/v1/restaurants` and `POST /api/restaurant/v1/restaurants/{restaurantId}/branches`. Both operations are idempotent by owner/code and keep PostgreSQL writes inside Restaurant Infrastructure. Provisioning logs record scoped UUIDs but no tokens or request bodies.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation query in the [observability guide](../../../docs/Deployment/Observability.md).
