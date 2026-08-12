# Product Owner Portal

The React/Vite Product Owner Portal is the browser UI for the separate `platform-admin-bff` session boundary. It implements the Phase 7 control-plane workflow in this order: session shell, organizations, memberships, product registry, organization product enablement, platform users/roles, audit, support elevation, platform summaries, and controlled links to separately deployed product administration portals.

The browser calls only `/bff/platform-admin/*` with the owning secure cookie. It never stores tokens, connects to databases, embeds product administration screens, or exposes direct customer product operations. UI capabilities are presentation hints derived from the platform roles returned by `/bff/platform-admin/me`; BFF and Platform Directory policies remain authoritative.

## Configuration

- `PRODUCT_OWNER_BFF_URL` is an optional local Vite proxy target, for example `https://localhost:58527`.
- `VITE_PRODUCT_ADMIN_LINKS` is a comma-separated deployment allow-list in `application-code|label|https-url` form. Same-origin relative links are also accepted. Non-HTTPS cross-origin URLs and malformed entries are discarded.

Example:

```text
VITE_PRODUCT_ADMIN_LINKS=nexa-connect|NexaConnect Admin|https://admin.example.com
```

Run `npm run dev --workspace @nexaconnect/product-owner-portal` for local UI development. Run `npm run check`, `npm test`, and `npm run build` from `src/Frontend` for verification. Deploy the built `dist` assets with routes to the Platform Admin BFF on the same origin, or through an equivalent trusted reverse proxy. Do not reuse the Customer Portal or product-admin cookies, OIDC client, scopes, or secrets.
