# Platform Admin BFF

Separate Product Owner/Platform Admin session boundary. Every management proxy requires the `system-admin` role and forwards the server-held access token to Platform Directory; the BFF never accesses PostgreSQL directly. Configure `Bff:ClientSecret` from a secret store. Development/Test use an in-memory ticket cache; other environments require Redis through `ConnectionStrings:BffSessionCache`, with optional key isolation through `BffSessionCache:InstanceName`.

Endpoints:

- `GET /bff/platform-admin/login`, `/logout`, and `/me` manage or inspect the dedicated Platform Admin session.
- `POST /bff/platform-admin/organizations` and `PATCH /bff/platform-admin/organizations/{organizationId}` proxy organization creation and updates.
- `PUT /bff/platform-admin/organizations/{organizationId}/members/{subjectId}` assigns organization membership.
- `POST /bff/platform-admin/products` registers a product, and `PUT /bff/platform-admin/organizations/{organizationId}/products` changes organization product access.

Unauthenticated management requests are challenged through the Platform Admin login flow. Authenticated users without `system-admin` receive `403 Forbidden`. If the server-held access token is missing, the proxy returns `401 Unauthorized`; otherwise it preserves the Platform Directory response status and body.

The OIDC client requests the `nexaconnect-api` scope. Its realm-role mapper must also add the multi-valued `roles` claim to the ID token; the BFF additionally normalizes a nested `realm_access.roles` claim when present. The `system-admin` realm role must be assigned to the user; sign out and sign in again after changing the mapper or role.
