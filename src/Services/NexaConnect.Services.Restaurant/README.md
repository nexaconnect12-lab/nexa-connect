# Restaurant Service

Owns restaurant and branch hierarchy used by tenant authorization. Operational telemetry uses service name `nexaconnect-restaurant`; request logs exclude query strings, bodies, authorization headers, cookies, and arbitrary headers.

Platform owners and administrators provision hierarchy through `POST /api/restaurant/v1/restaurants` and `POST /api/restaurant/v1/restaurants/{restaurantId}/branches`. Both operations are idempotent by owner/code and keep PostgreSQL writes inside Restaurant Infrastructure. Provisioning logs record scoped UUIDs but no tokens or request bodies.

Customer owners/admins manage tenant branches through `/api/restaurant/v1/customer/organizations/{organizationId}/branches`. Restaurant requires active Platform Directory access plus `restaurant.branch.read` or `restaurant.branch.manage` from Authorization and enforces ownership in organization-leading SQL. Creates and versioned updates transactionally append immutable branch audit records. Apply Restaurant migration 2 and configure direct HTTPS `Services:PlatformDirectory` and `Services:Authorization`; dependency failure is fail-closed.

Typed branch product configuration is exposed at `/api/restaurant/v1/customer/organizations/{organizationId}/configuration/branches/{branchId}`. It controls dine-in, takeaway, table requirements, and service-charge percentage. Versioned writes require `restaurant.configuration.manage`, Restaurant migration 3, and append `branch.configuration.updated` in the same transaction.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation query in the [observability guide](../../../docs/Deployment/Observability.md).
