# Branch-approved screensaver gallery v1

## Gallery-only readers (POS UX4)

`POST /api/media/v1/customer/organizations/{org}/branches/{branch}/screensaver-gallery/readers`
requires the existing customer-role bearer and live branch menu-read authorization. No request
body is needed. Response `GalleryReaderCredential` contains `credential`, `organizationId`,
`branchId`, `issuedAtUtc`, `expiresAtUtc` (at most 24 hours). POS buffers the issue response
up to 16 KiB and validates credential/scope/lifetime before use.
The credential binds the authenticated stable subject; callers cannot supply another subject.

`GET /api/media/v1/gallery-readers/organizations/{org}/branches/{branch}` accepts exactly one
`X-Nexa-Gallery-Reader` header. It does not require a cashier bearer and returns the existing
`GalleryBundle`. Query parameters are rejected; Bearer/cookie credentials are not substitutes.
Both routes are no-store. Invalid header/query shape returns 401; invalid/tampered/expired or
foreign credentials and live access denial return 403; missing selection returns 404;
dependency/selection-change failure returns 503. Reads are limited in memory to 60 per IP
and 600 globally per minute, with at most 4096 keys; excess returns 429. No CORS/public
anonymous image-listing authority is introduced by this capability route.

New internal POST routes accept only the existing authenticated Media workload identity
(`azp=nexaconnect-media-service`, normal API audience/issuer). Missing authentication returns
401; other identities return 403. Bodies are capped at 4096 bytes:

| Service route | Body | Result |
| --- | --- | --- |
| `/api/platform-directory/v1/internal/gallery-access` | `{organizationId,subject}` | `{granted}` for live organization/product membership |
| `/api/authorization/v1/internal/gallery-access` | `{organizationId,restaurantId,branchId,subject}` | `{granted}` for fixed `catalog.menu.read`, existing decision audit |
| `/api/restaurant/v1/internal/organizations/{org}/branches/{branch}/screensaver-gallery` | `{subject}` | Current `BranchGallery` after active hierarchy and both live checks |

Subjects must be nonblank, at most 256 characters, without controls. No arbitrary permission,
amount or membership-detail request exists. Invalid membership/permission queries return
`granted:false`; Restaurant denies invalid scope/subject with 403. Media repeats delegated
checks after provider reads and refuses a changed selection/version. The five-minute bundle
lease is clipped to reader expiry. Revocation after checks is bounded by that issued lease;
OIDC session logout/account disable alone does not revoke a reader. It cannot self-renew.

Client operations `gallery.reader.issue`/`gallery.reader.read` and structured `Gallery reader`,
`Gallery authority`, `Gallery membership`, `Gallery permission` events use shared correlation.
Service names additionally include `nexaconnect-platform-directory`/`nexaconnect-authorization`.
No reader, subject, body or header is logged. See
[ADR-051](../Architecture/Decisions/ADR-051-locked-gallery-readers.md) and
[cache, rollout and acceptance](../Deployment/POS-UX4-Locked-Gallery.md).

The Customer Portal **Media management → Approved screensaver gallery** configures one
branch. Upload/scan/process using existing Media management first. Select separate ordered
staff/customer lists, optional plain-text titles/captions and a 5–60 second interval. Each
list has at most six unique asset IDs; title ≤80 and caption ≤160 characters, without controls.
Check explicit public-display approval before saving. Empty lists withdraw the slideshow.
Unconfirmed/conflicting writes require reload; neither portal nor POS automatically repeats writes.

Restaurant resolves the requested branch's active owning restaurant within the selected
organization before evaluating either permission. The live decision includes organization,
restaurant and branch, so an existing restaurant-scoped manager assignment applies to its
branches. An unavailable, inactive or foreign hierarchy is denied with 403. This adds no
permission grant; existing live denials remain authoritative.

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
Responses are no-store. Anonymous API calls return 401. Gallery configuration returns 403
for denied scope or an unavailable, foreign or inactive branch/restaurant detected before
authorization. A branch disappearing after authorization can return 404 on read or 409 on
write; stale versions also return 409. Asset-readiness lookup returns 404 for an unavailable
or unready asset, and Media delivery can return 404 when selection is unavailable. Invalid
selection/readiness returns 400; dependency/delivery failure returns 503 (unexpected provider
failures can surface as generic server errors).

`BranchGallery` contains `organizationId`, `branchId`, current branch `version`, `staff` and
`customer` arrays of `{assetId,title,caption}`, and `secondsPerSlide`. Default selection is empty.
Other branch updates also advance this version. Saves update only `ScreensaverGallery`, so
pricing/configuration remain intact. Audit and `restaurant.audit.v1` outbox commit atomically
with the selection under the existing `branch.configuration.updated` vocabulary.

`GalleryBundle` contains `selection`, `issuedAtUtc`, `expiresAtUtc` and `images` of
`{assetId,sha256,png}` (base64 JSON bytes). Media converts ready display variants to complete
PNG images, maximum 1280×1280, 2 MiB each and 16 MiB total. A compressed processed photo can
expand beyond the PNG limit. Media tries at most three PNG encodings: full size, then
three-quarter dimensions twice (a 1280-pixel side becomes 960, then 720), preserving aspect
ratio within integer rounding. Only the delivery copy changes; stored originals and variants
remain unchanged. Hashes describe the delivered PNG bytes. If encoding still cannot satisfy
the bound, delivery fails closed rather than raising the limit. Missing/deleted/unready images and
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
