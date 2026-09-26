# Identity Claims Contract

Cash-close Reporting reuses `pos.cash-review.read` through a live POS access probe for the exact organization/branch/store. It introduces no new grant, claim or client. The Customer BFF revalidates current product membership and protected tenant subject; Reporting requires matching tenant headers and independently calls POS with the user's bearer token. POS resolves Restaurant hierarchy and its store before requesting Authorization. Generic Reporting permissions do not grant this financial read. See [cash-close authorization](../API/Cash-Close-Reporting.md).

## Purpose

Keycloak authenticates users and workloads. NexaConnect validates access tokens and uses stable identity identifiers, while the Platform Directory owns cross-product organizations and memberships and each NexaConnect service owns its resource-level authorization.

## Required access-token claims

| Claim | Requirement | Use |
|---|---|---|
| `iss` | Exact environment-specific realm issuer | Prevent tokens from another authority |
| `sub` | Stable, non-empty Keycloak subject | External identity identifier stored by platform and product records |
| `aud` | Must contain `nexaconnect-api` | Prevent tokens intended for another API |
| `exp` | Required and validated | Limit token lifetime |
| `nbf` | Validated when present | Prevent premature use |
| `azp` | Expected on user tokens | Identify the client that obtained the token for auditing |
| `preferred_username` | Optional | Display and diagnostics only; never a database key or authorization input |
| `roles` | Optional, multi-valued | Coarse platform/application access only |

The contract is versioned by compatible additions. Removing or changing the meaning of a claim requires a migration plan for every token consumer.

## Authorization boundary

Realm roles may grant coarse access such as `report-viewer` or `tenant-admin`. They do not decide whether a user may refund a specific order, manage a particular restaurant, open a particular shift, or access another organization's data. Those decisions require current product-owned data and policies.

Organization membership is resolved through the Platform Directory API or an approved local projection. It is not inferred from email domains, usernames, client roles, or direct Keycloak database access.

The initial current-access API is `GET /api/platform-directory/v1/me/access`. It evaluates the caller's `sub`, active memberships, active organizations, and enabled `nexa_connect` application access, returning the organizations available to the current subject. The organization-specific `GET /api/platform-directory/v1/organizations/{organizationId}/access` endpoint remains available for a single access decision. Both are organization-level boundaries, not substitutes for product resource authorization.

The Product Owner Portal and Customer Portal are separate trust boundaries. Platform roles such as `platform-owner`, `platform-admin`, `platform-support`, and `platform-auditor` are not customer membership roles. Customer roles such as `customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, and `customer-viewer` are evaluated within an organization and enabled product. A platform role does not automatically authorize restaurant or other product operations.

The development realm defines these platform and customer role names and maps them into the `roles` claim for the API scope. `platform-owner` and `platform-admin` may change platform control-plane data and approve or revoke support elevation. `platform-support` may request elevation but cannot approve its own request. `platform-auditor` may inspect elevation records without mutation. Customer roles remain organization context and never substitute for product-owned authorization.

Support elevation is resolved from Platform Directory data, not minted as a long-lived Keycloak role. It is limited to one support subject, organization, and application; requires a reason and independent approval; lasts 5–240 minutes; and becomes ineffective at expiry or revocation. Every request, approval, and revocation is recorded in append-only audit history.

## Validation rules

Payment Review uses the existing Customer Portal identity boundary and introduces no claim, client, or role grant. The BFF revalidates the protected tenant's subject and current organization/product membership; Order then checks `order.payment-review.read` or `order.payment-review.resolve` against Restaurant-owned branch scope. Permission probes control presentation only and do not authorize later requests. Resolution also requires the session-bound anti-forgery cookie and `X-Nexa-CSRF` token; this requirement is scoped to the new Payment Review mutation route, not all existing BFF mutations. See [the operator contract](../API/Payment-Review-Operator-UI.md).

POS Cash Review uses the existing public `nexaconnect-pos` Authorization Code + PKCE identity and introduces no token claim or client secret. POS checks `pos.cash-review.read` or `pos.cash-review.resolve` through Authorization against the revalidated organization/restaurant/branch/store scope. Tenant administrators and store managers receive both permissions; accountants receive read only. Access probes control presentation only, and every list, detail, and decision call performs its own service-side scope and permission checks.

- Validate signature, issuer, audience, lifetime, and signing-key rollover.
- Fetch signing keys only from the configured authority's discovery metadata.
- Require HTTPS metadata outside local development.
- Keep access tokens short-lived; the development realm uses five minutes.
- Do not log raw access, identity, or refresh tokens.
- Use `sub` rather than username or email as the durable identity reference.
- Treat missing authorization context as denial.

The online Kitchen queue reuses `kitchen.ticket.read` and `kitchen.ticket.transition` with current product membership and Restaurant-owned branch scope. Customer BFF cookie/tenant binding and membership revalidation do not replace Kitchen authorization. The Order workload has no operator-route bypass. Kitchen transitions require the existing BFF antiforgery cookie plus `X-Nexa-CSRF`; see [the operator contract](../API/Kitchen-Queue.md). No new role, claim or permission migration is introduced.

The cash-close replay CLI uses controlled operational database/broker credentials, not Customer bearer tokens or `pos.cash-review.read`. Its operator UUID is self-asserted attribution; PostgreSQL `session_user` supplies database credential attribution. It creates no role/client/claim grant. Limit database rights to source reads and audit inserts, and broker rights to the required exchange. See [operational authority](../Deployment/Cash-Close-Recovery.md#prerequisites-and-authority).

Restricted replay credentials were verified in the disposable local matrix on 2026-09-24: the CLI used a PostgreSQL login limited to source SELECT/audit INSERT and a RabbitMQ identity limited to configure/write on one exchange. Denied-operation probes and recorded `session_user` passed. These are fixture-specific guarantees, not proof of deployed grants. See [evidence](../Architecture/Evidence/Cash-Close-Recovery-Acceptance.md).

The [joined cash-close portal fixture](../Deployment/Cash-Close-Portal-Acceptance.md) uses a restaurant-scoped manager and branch-scoped accountant, both with membership in a second tenant but no cash grants there. An explicit fixture-only read deny verifies authorization changes affect an existing signed-in session. The POS workload client emits `aud=nexaconnect-api` for Restaurant hierarchy lookup; no new permission or client is introduced.
