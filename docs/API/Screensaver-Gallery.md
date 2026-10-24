# Branch-approved screensaver gallery v1

The Customer Portal **Media management → Approved screensaver gallery** configures one
branch. Upload/scan/process using existing Media management first. Select separate ordered
staff/customer lists, optional plain-text titles/captions and a 5–60 second interval. Each
list has at most six unique asset IDs; title ≤80 and caption ≤160 characters, without controls.
Check explicit public-display approval before saving. Empty lists withdraw the slideshow.
Unconfirmed/conflicting writes require reload; neither portal nor POS automatically repeats writes.

| Route | Authority and behavior |
| --- | --- |
| `GET /api/restaurant/v1/customer/organizations/{org}/branches/{branch}/screensaver-gallery` | Live membership and branch `catalog.menu.read`; active branch/restaurant only |
| `PUT` at the same route | Branch `restaurant.configuration.manage`; `{ expectedVersion, staff, customer, secondsPerSlide }`; checks selected Media readiness with caller's Media read authority |
| `GET /api/media/v1/customer/organizations/{org}/gallery-assets/{asset}` | `media.asset.read`; readiness metadata only, ready Catalog product with ready display variant |
| `GET /api/media/v1/customer/organizations/{org}/branches/{branch}/screensaver-gallery` | Calls the Restaurant read route with caller bearer, then supplies only currently eligible selected images |
| `GET /bff/customer/screensaver-gallery/csrf` | Customer session; existing protected antiforgery cookie/request token |
| `GET` / `PUT /bff/customer/screensaver-gallery/branches/{branch}` | Subject-bound protected tenant, current Directory access and `nexa_connect`; PUT requires `X-Nexa-CSRF` |

The owning services repeat authority; discovery, cookies and selected IDs are not grants.
The BFF derives the organization, suppresses downstream error bodies and buffers selection
responses up to 16 KiB. PUT body limit is 16 KiB. No browser-supplied actor/tenant is forwarded.
Responses are no-store. Anonymous API calls return 401; denied scope 403; absent active branch
or asset 404; invalid selection/readiness 400; stale/unavailable branch on write 409;
dependency/delivery failure 503 (unexpected provider failures can surface as generic server errors).

`BranchGallery` contains `organizationId`, `branchId`, current branch `version`, `staff` and
`customer` arrays of `{assetId,title,caption}`, and `secondsPerSlide`. Default selection is empty.
Other branch updates also advance this version. Saves update only `ScreensaverGallery`, so
pricing/configuration remain intact. Audit and `restaurant.audit.v1` outbox commit atomically
with the selection under the existing `branch.configuration.updated` vocabulary.

`GalleryBundle` contains `selection`, `issuedAtUtc`, `expiresAtUtc` and `images` of
`{assetId,sha256,png}` (base64 JSON bytes). Media converts ready display variants to complete
PNG images, maximum 1280×1280, 2 MiB each and 16 MiB total. Missing/deleted/unready images and
their selection entries are removed before delivery. The readiness check is repeated after
the object read; concurrent deletion after response and token revocation are bounded by
the maximum five-minute lease, not claimed as instantaneous withdrawal. It contains no
storage keys, signed URLs, credentials, transaction or staff data.

POS verifies exact scope, lease, list bounds, image IDs/hash/signature/dimensions and full
decode before use. The HTTP response bound is 24 MiB, including base64 overhead, with a
20-second whole-download deadline and redirects disabled. Images/captions remain presentation;
they cannot alter activity, authentication, shift, payment or original recovery identities.

Service names: `nexaconnect-restaurant`, `nexaconnect-media`, `nexaconnect-customer-bff`,
`nexaconnect-pos-client`. Existing correlation propagates between named/typed HTTP clients.
Safe structured events use `Gallery configuration`, `Gallery asset`, `Gallery selection`,
`Gallery delivery`, `Gallery BFF` and the client operation `gallery.read`. Filter these in
configured logs by correlation ID. No token, cookie, arbitrary header, object key, caption,
filename, image body or payment data is logged.

See [ADR-040](../Architecture/Decisions/ADR-040-branch-approved-screensaver-gallery.md)
and [configuration/cache/verification](../Deployment/Next-Iteration-Phase-5.md).
