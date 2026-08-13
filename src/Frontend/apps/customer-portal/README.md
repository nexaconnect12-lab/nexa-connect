# NexaConnect Customer Portal

Phase 8 provides the independently buildable tenant-scoped Customer Portal context/navigation shell. Authentication is owned by `NexaConnect.CustomerBff`; the browser receives only opaque cookies and selects one organization plus enabled product from `/bff/customer/access`. `/tenant` and `/features/*` revalidate the protected server-readable selection.

The portal includes organization profile, product switching, memberships, branch lifecycle management, typed branch configuration, Reporting-owned branch dashboards and sales reports, and Media-owned metadata listing. Reporting shows the latest global projector checkpoint, not branch-specific freshness. Media upload/processing and unified activity remain staged. Authenticated forwarding, PostgreSQL predicates/audit, and interaction coverage remain Phase 11 hardening.

Run `npm run dev --workspace @nexaconnect/customer-portal` with `CUSTOMER_BFF_URL` set for local proxying. Run `npm run check`, `npm test`, and `npm run build --workspace @nexaconnect/customer-portal` from `src/Frontend`. Publishing the Customer BFF builds and embeds the SPA unless `SkipCustomerPortalBuild=true` is used after independently supplying verified assets.
