# Authorization Service

Manual tender confirmation requires migration 6. Existing and newly assigned `cashier`, `store-manager`, and `tenant-admin` roles receive `order.manual-payment.confirm` at their normal hierarchical scopes. Downgrading `6→5` removes only this permission association and must follow disabling the manual-settlement route.

Owns product-scoped authorization decisions and role assignments. Operational telemetry uses service name `nexaconnect-authorization`; decision logs contain permission and UUID scope but never bearer tokens, identity credentials, request bodies, customer PII, or payment details.

`POST /api/authorization/v1/role-assignments` accepts legacy `system-admin` and current `platform-owner`/`platform-admin` control-plane roles. Platform Admin BFF exposes a same-body proxy while Authorization remains the persistence owner. Assignment scope is hierarchical: `tenant-admin` is organization-scoped, `store-manager` is restaurant-scoped, and operational roles are branch-scoped. Organization-wide list APIs therefore require an organization-scoped assignment; restaurant-scoped assignments apply only when the authorization request carries their restaurant.

Payment Review provisioning requires migration 5. Newly assigned `tenant-admin` and `store-manager` roles receive both `order.payment-review.read` and `order.payment-review.resolve`; branch-scoped `accountant` receives read only. Migration 5 backfills the accountant read grant for roles that already exist. This separation is used by joined operator acceptance and must not be replaced with platform roles or a gateway-only check.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation and denial queries in the [observability guide](../../../docs/Deployment/Observability.md).
