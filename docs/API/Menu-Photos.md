# Approved menu photos

Catalog owns the primary image selection per organization, branch and menu product. Media owns the original file, processing lifecycle and variants. A ready Catalog/product image may be reused for another menu product in the same organization; its immutable Media owner does not change. Saving explicitly approves that image for public menu display at this branch. Screensaver and bank QR selections are independent.

## Staff API

Catalog base: `/api/catalog/v1/customer/branches/{branch}/products/{product}`. Require a user bearer plus `X-Nexa-Organization-Id` and `X-Nexa-Application-Code: nexa_connect`. There is no trusted-workload bypass on these routes.

| Method/suffix | Behavior and live authorization |
| --- | --- |
| GET `photo-selection` | Return organizationId, branchId, productId, nullable assetId, version; branch `catalog.menu.read` |
| PUT `photo-selection` | Body `{assetId: UUID or null, expectedVersion: integer, approved: true}`; branch `catalog.menu.write`; non-null selection also requires organization `media.asset.read` and a ready processed Catalog/product image |
| GET `photo-preview/{asset}` | Preview a candidate for an existing product; branch menu.write plus organization media.asset.read |
| GET `photo` | Deliver selected image only; branch menu.read; no general Media grant needed |

No selection returns assetId null/version0. Clearing retains the versioned configuration and audit. Stale expectedVersion returns409 `menu_photo_changed`; exact current no-op preserves version/audit count. Invalid approval/image returns400; absent product or photo404; live denial403; dependency failure503. Image responses are metadata-free PNG, at most512 pixels per dimension and512KiB, no-store/nosniff. Catalog rereads the selection after Media reads; permission checks repeat at delivery boundaries.

Customer BFF base `/bff/customer/menu-photos`: GET `csrf`; GET/PUT `branches/{branch}/products/{product}`; GET same product `/image` or `/preview/{asset}`. Cookie-authenticated CustomerSession, selected tenant/stable subject and current Directory membership are required. PUT additionally requires `X-Nexa-CSRF` and at most4096 request bytes. JSON responses are bounded4096 bytes and photo responses512KiB. Browser code never receives bearer tokens or private storage URLs for menu display. Existing signed staff upload/download routes remain unchanged.

## Guest and workload delivery

Guest GET `/bff/guest/qr/menu/{product}/photo` requires enabled QR entry/menu, exact configured HTTPS origin and the secure guest cookie. It forwards only the guest capability to Order GET `/api/order/v1/guest/qr/menu/{product}/photo`. Order validates the live visit and product membership before/after photo retrieval. No staff token, organization selector or object URL is accepted from the phone. Missing photos fall back to text menus; photo errors do not change quote/cart/payment contracts.

Order calls GET `/api/catalog/v1/internal/organizations/{org}/branches/{branch}/products/{product}/photo` using its existing workload identity. The route requires ServiceWorkload and exact `azp=nexaconnect-order-service`. Catalog calls GET `/api/media/v1/internal/organizations/{org}/catalog-menu-images/{asset}` with exact Catalog workload identity. Media requires ServiceWorkload and `azp=nexaconnect-catalog-service`; it serves only a same-organization ready Catalog/product processed thumbnail, reencoded without metadata, and rechecks lifecycle/version/key after storage access. Originals, screensavers and bank QR assets cannot be served through this path.

Photo traffic has separate fixed-window budgets from guest ordering: BFF/edge240 per source IP/minute and1000 globally; Order1000/minute. The local edge accepts only the exact product GUID GET path without query parameters. Existing private API exposure and workload audience validation remain mandatory.

## Observability

Services `nexaconnect-catalog`, `nexaconnect-media`, `nexaconnect-order`, `nexaconnect-customer-bff` and `nexaconnect-qr-pilot-edge` use the shared structured JSON/OTLP foundation and validated outbound correlation propagation. Search safe `Menu photo`, `QR menu photo` or `Menu photo BFF` events by `service.name` and correlation ID; native operation is `catalog.menu-photo`. Inspect status/category and authorization-denial events. Never capture guest capabilities, tokens, cookies, image bodies, payment data or signed URLs. See [deployment](../Deployment/Menu-Photos.md), [persistence](../Database/Menu-Photos.md) and [ADR-058](../Architecture/Decisions/ADR-058-approved-menu-photos.md).
