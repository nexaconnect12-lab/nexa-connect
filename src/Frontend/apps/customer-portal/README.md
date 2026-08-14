# NexaConnect Customer Portal

Phase 8 provides the independently buildable tenant-scoped Customer Portal context/navigation shell. Authentication is owned by `NexaConnect.CustomerBff`; the browser receives only opaque cookies and selects one organization plus enabled product from `/bff/customer/access`. `/tenant` and `/features/*` revalidate the protected server-readable selection.

The portal includes profile, product switching, memberships, branches, configuration, Reporting dashboards/sales/activity preview, and Media-owned preview list/upload/download/delete. Uploads go directly to the signed S3-compatible URL and are completed through the BFF. They are size/checksum-verified but not malware-scanned; variants and reconciliation remain staged.

Run `npm run dev --workspace @nexaconnect/customer-portal` with `CUSTOMER_BFF_URL` set for local proxying. Run `npm run check`, `npm test`, and `npm run build --workspace @nexaconnect/customer-portal` from `src/Frontend`. Publishing the Customer BFF builds and embeds the SPA unless `SkipCustomerPortalBuild=true` is used after independently supplying verified assets.
