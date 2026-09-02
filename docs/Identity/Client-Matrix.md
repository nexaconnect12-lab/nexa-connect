# Keycloak Client Matrix

| Client | Owner | Client type | Enabled flow | Local redirect URI |
|---|---|---|---|---|
| `nexaconnect-web-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7100/signin-oidc` |
| `nexaconnect-admin-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7200/signin-oidc` |
| `nexaconnect-mobile` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect://oauth/callback` |
| `nexaconnect-pos` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect-pos://oauth/callback` |
| `platform-admin-bff` | Shared platform | Confidential | Authorization Code | Shared-platform value; local fixture uses `https://localhost:58627/signin-oidc` |
| `platform-directory-admin` | Shared platform directory | Confidential service account | Client Credentials | None |
| Workload clients (`nexaconnect-*-service`, including Kitchen and Media) | Owning workload | Confidential | Client Credentials | None |

## Rules

The [joined Payment Review browser acceptance](../../src/Frontend/e2e/payment-review-live/README.md) creates a disposable `nexa-review-it-<run-id>` realm with the normal Customer BFF/workload clients and audiences and distinct synthetic reader/resolver accounts. The Customer BFF client includes the subject and `nexaconnect-api` audience in access tokens; the Order workload client includes that API audience for authenticated downstream compensation. Exact loopback redirects are generated per run, and no development or production accounts/secrets are reused. Authorization migration 5 assigns branch-scoped `accountant` to the reader and restaurant-scoped `store-manager` to the resolver; both receive `order.payment-review.read`, while only the resolver receives `.resolve`. The local joined suite passed 7/7 on 2026-09-02. No new production client or authentication flow is introduced.

- Register exact redirect and post-logout redirect URIs for every environment; do not use broad wildcards.
- Store confidential-client secrets in the environment's secret manager and rotate them.
- BFFs use refresh tokens only from server-side authentication tickets. Successful renewal replaces the stored access/refresh tokens; rejected renewal clears the session and requires interactive login.
- Public clients never receive a client secret and must require PKCE S256.
- Password/direct-access grants and implicit flow remain disabled.
- Full scope is disabled. Assign only explicit client scopes and role scope mappings.
- Create one service-account client per concrete workload rather than sharing a machine credential.
- Do not use NexaConnect-managed configuration as the production source of truth for `platform-admin-bff`; the checked-in realm entry is a local integration fixture only.

The checked-in development realm implements the four NexaConnect interactive clients, the `nexaconnect-api` bearer-only audience, dedicated POS, Catalog, Order, Inventory, Kitchen, Payment, and Media workload clients, a local `platform-admin-bff` client with the `roles` scope enabled for role-claim testing, and the `platform-directory-admin` service account. The administration account has only `view-users`, `manage-users`, and `view-realm` from Keycloak `realm-management`. Each workload client uses a separately managed secret. Production registration and ownership of the platform clients remain with the shared platform.

The local realm also defines the Phase 2 platform roles (`platform-owner`, `platform-admin`, `platform-support`, `platform-auditor`) and customer roles (`customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, `customer-viewer`). Product roles such as cashier or store manager remain separate. The legacy `system-admin` role is retained only for compatibility with older non-portal administration paths and must not be assigned as a substitute for the platform role model.

Portal boundary: `nexaconnect-web-bff` is the current Customer Portal session boundary, while `nexaconnect-admin-bff` is the NexaConnect product administration boundary. The ecosystem-wide Product Owner Portal uses `platform-admin-bff`, owned by the shared platform; the checked-in development client is only a local integration fixture. Do not reuse customer cookies, audiences, scopes, or secrets for Product Owner sessions. A future rename from `nexaconnect-web-bff` to `nexaconnect-customer-bff` must be deployed as an explicit client migration with overlapping redirect URIs only for the approved transition window.

The WPF POS client opens Keycloak in the system browser and receives the authorization response through the exact `nexaconnect-pos://oauth/callback` custom scheme. Its installer or device-enrollment procedure must register that scheme for the deployed executable; do not use a wildcard or a second redirect URI. Callback forwarding is restricted to the current Windows user, bounded in size, and state-validated for a single sign-in attempt.
