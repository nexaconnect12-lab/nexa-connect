# NexaConnect Customer Portal

Phase 8 provides the independently buildable tenant-scoped Customer Portal context/navigation shell. Authentication is owned by `NexaConnect.CustomerBff`; the browser receives only opaque cookies and selects one organization plus enabled product from `/bff/customer/access`. `/tenant` and `/features/*` revalidate the protected server-readable selection.

The portal includes all requested navigation. Organization profile and enabled-product switching use real access data. Dashboards currently show context only, while users, configuration, branches, reports, media, and activity show `contract-pending`. Catalog, Inventory, and Order BFF adapters exist but no portal screen consumes them yet.

Run `npm run dev --workspace @nexaconnect/customer-portal` with `CUSTOMER_BFF_URL` set for local proxying. Run `npm run check`, `npm test`, and `npm run build --workspace @nexaconnect/customer-portal` from `src/Frontend`. Publishing the Customer BFF builds and embeds the SPA unless `SkipCustomerPortalBuild=true` is used after independently supplying verified assets.
