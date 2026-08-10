# Keycloak Client Matrix

| Client | Owner | Client type | Enabled flow | Local redirect URI |
|---|---|---|---|---|
| `nexaconnect-web-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7100/signin-oidc` |
| `nexaconnect-admin-bff` | NexaConnect | Confidential | Authorization Code | `https://localhost:7200/signin-oidc` |
| `nexaconnect-mobile` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect://oauth/callback` |
| `nexaconnect-pos` | NexaConnect | Public | Authorization Code + PKCE S256 | `nexaconnect-pos://oauth/callback` |
| `platform-admin-bff` | Shared platform | Confidential | Authorization Code | Defined and deployed by the platform repository |
| Workload clients | Owning workload | Confidential | Client Credentials | None |

## Rules

- Register exact redirect and post-logout redirect URIs for every environment; do not use broad wildcards.
- Store confidential-client secrets in the environment's secret manager and rotate them.
- Public clients never receive a client secret and must require PKCE S256.
- Password/direct-access grants and implicit flow remain disabled.
- Full scope is disabled. Assign only explicit client scopes and role scope mappings.
- Create one service-account client per concrete workload rather than sharing a machine credential.
- Do not register `platform-admin-bff` in NexaConnect-managed configuration.

The checked-in development realm implements the four NexaConnect interactive clients and the `nexaconnect-api` bearer-only audience. Workload clients are added only when a concrete caller and least-privilege scope have been defined.

Portal boundary: `nexaconnect-web-bff` is the current Customer Portal session boundary, while `nexaconnect-admin-bff` is the NexaConnect product administration boundary. The ecosystem-wide Product Owner Portal uses `platform-admin-bff`, owned by the shared platform. Do not reuse customer cookies, audiences, scopes, or secrets for Product Owner sessions. A future rename from `nexaconnect-web-bff` to `nexaconnect-customer-bff` must be deployed as an explicit client migration with overlapping redirect URIs only for the approved transition window.

The WPF POS client opens Keycloak in the system browser and receives the authorization response through the exact `nexaconnect-pos://oauth/callback` custom scheme. Its installer or device-enrollment procedure must register that scheme for the deployed executable; do not use a wildcard or a second redirect URI. Callback forwarding is restricted to the current Windows user, bounded in size, and state-validated for a single sign-in attempt.
