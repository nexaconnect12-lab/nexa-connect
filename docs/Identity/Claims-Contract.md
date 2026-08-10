# Identity Claims Contract

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

## Validation rules

- Validate signature, issuer, audience, lifetime, and signing-key rollover.
- Fetch signing keys only from the configured authority's discovery metadata.
- Require HTTPS metadata outside local development.
- Keep access tokens short-lived; the development realm uses five minutes.
- Do not log raw access, identity, or refresh tokens.
- Use `sub` rather than username or email as the durable identity reference.
- Treat missing authorization context as denial.
