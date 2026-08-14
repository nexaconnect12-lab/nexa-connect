# Authorization Service

Owns product-scoped authorization decisions and role assignments. Operational telemetry uses service name `nexaconnect-authorization`; decision logs contain permission and UUID scope but never bearer tokens, identity credentials, request bodies, customer PII, or payment details.

`POST /api/authorization/v1/role-assignments` accepts legacy `system-admin` and current `platform-owner`/`platform-admin` control-plane roles. Platform Admin BFF exposes a same-body proxy while Authorization remains the persistence owner. Assignment scope is hierarchical: `tenant-admin` is organization-scoped, `store-manager` is restaurant-scoped, and operational roles are branch-scoped. Organization-wide list APIs therefore require an organization-scoped assignment; restaurant-scoped assignments apply only when the authorization request carries their restaurant.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation and denial queries in the [observability guide](../../../docs/Deployment/Observability.md).
