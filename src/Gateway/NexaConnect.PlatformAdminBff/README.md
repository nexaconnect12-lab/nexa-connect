# Platform Admin BFF

Separate Product Owner/Platform Admin session boundary. Control-plane mutations require `platform-owner` or `platform-admin`; support requests additionally allow `platform-support`, and elevation inspection allows `platform-auditor`. The BFF forwards the server-held access token to Platform Directory and never accesses PostgreSQL directly. Configure `Bff:ClientSecret` from a secret store. Development/Test use an in-memory ticket cache; other environments require Redis through `ConnectionStrings:BffSessionCache`, with optional key isolation through `BffSessionCache:InstanceName`.

The recorded local origin is `https://localhost:58627` (`http://localhost:58628` is the secondary development listener). The Keycloak development client uses `https://localhost:58627/signin-oidc` and origin `https://localhost:58627`. The Phase 8 development launcher publishes and starts this BFF with the hosted Product Owner Portal using `PLATFORM_ADMIN_BFF_CLIENT_SECRET` from the ignored `.env`.

Endpoints:

- `GET /bff/platform-admin/login`, `/logout`, and `/me` manage or inspect the dedicated Platform Admin session. `/me` returns the subject, username, and normalized platform roles needed for presentation-only portal navigation; server policies remain authoritative.
- `GET` and `POST /bff/platform-admin/organizations` plus `PATCH /bff/platform-admin/organizations/{organizationId}` proxy organization listing, creation, and updates.
- `PUT /bff/platform-admin/organizations/{organizationId}/members/{subjectId}` assigns organization membership.
- `POST /bff/platform-admin/products` registers a product, and `PUT /bff/platform-admin/organizations/{organizationId}/products` changes organization product access.
- `GET|POST /bff/platform-admin/restaurants`, `GET|POST /bff/platform-admin/restaurants/{restaurantId}/branches`, and `POST /bff/platform-admin/authorization/role-assignments` browse/provision Restaurant-owned hierarchy and assign Authorization-owned customer product roles through their owning APIs. Restaurant listing requires an `organizationId` query value. The hosted Product Owner compatibility portal exposes hierarchy and product-role forms; tenant administrators are organization-scoped, store managers are restaurant-scoped, and other roles are branch-scoped.
- `/bff/platform-admin/support-elevations` proxies request, effective lookup, audit read, approval, and revocation operations with endpoint-specific platform-role policies.
- `/bff/platform-admin/platform/users`, `/roles`, `/audit`, and `/summary` proxy Phase 3 user administration, permission-catalog, audit-query, and directory-summary operations. The BFF forwards its server-held token and never holds Keycloak administration credentials.

`GET /health` is an anonymous process-liveness endpoint. It does not currently assert Platform Directory or Redis readiness.

Unauthenticated BFF API requests return `401 Unauthorized` without a cross-origin redirect so the browser shell can initiate a top-level Platform Admin login. Authenticated users without the endpoint's platform role receive `403 Forbidden`. If the server-held access token is missing, the proxy returns `401`; otherwise it preserves the Platform Directory response status and body.

Authenticated state-changing requests must carry a same-origin `Origin` header. Missing, malformed, or cross-origin values are rejected with `403 Forbidden` before authorization or proxying as the BFF's cookie-request forgery defense.

The BFF refreshes expiring access tokens and updates the server-side ticket. A rejected refresh clears the session and proxy calls return `401`. Bodyless downstream responses such as `204` and `304` are forwarded without content. Configure `Services:PlatformDirectory`, `Services:Restaurant`, and `Services:Authorization`; Development uses direct HTTPS endpoints to avoid losing bearer tokens during redirects.

Request bodies are buffered before forwarding so mutation payloads are replayable. Configure `Services:PlatformDirectory` with the final internal HTTPS address; authenticated redirects are unsupported because HTTP clients can remove the bearer token while following them. Development uses `https://localhost:53356/` to avoid redirecting from Platform Directory's HTTP launch URL.

The OIDC client requests the `nexaconnect-api` scope. Its realm-role mapper must also add the multi-valued `roles` claim to the ID token; the BFF additionally normalizes a nested `realm_access.roles` claim when present. Assign only the appropriate `platform-owner`, `platform-admin`, `platform-support`, or `platform-auditor` role; sign out and sign in again after changing the mapper or role.

Operational telemetry uses the shared observability foundation. JSON console output is always available; set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317` for the local collector. Correlation logging intentionally excludes bodies, query strings, authorization headers, cookies, and tokens.

`dotnet publish` builds the Product Owner Portal workspace and includes its output under the BFF's `wwwroot`. Set `SkipProductOwnerPortalBuild=true` only when a trusted release pipeline has already supplied equivalent assets. The BFF serves `index.html` without caching, fingerprints assets with immutable caching, and applies CSP, frame, referrer, and MIME-sniffing response protections.
