# Menu image selection and display

UX15 consolidates the manager workflow below into **Menu management**: choose a branch,
**Load menu**, select an item, then open **Image**. The standalone workflow below is retained
for historical/compatibility deep links. See [current workflow and draft guards](POS-UX15-Menu-Management.md).

The retained local pilot was refreshed on 30 October 2026 with Catalog6 and the matching
POS package. All twelve services are Ready; the existing QR origin and trusted certificates
are retained. Eight protected local files remained unchanged before launch, and existing
Catalog/POS/Restaurant/Order/Media business data passed preservation comparison. Read-only
processed PNG delivery, exact-workload denial and runtime table grants passed. No product
photo was automatically assigned or approved. Actual manager/phone/POS image workflows
remain operator acceptance; fresh POS and phone acceptance records contain pending cases.

## Manager workflow

If menu photo settings return503 and image selection remains disabled, check Catalog's
safe `Menu photo dependency unavailable InvalidOperationException` events. The branch-scope
adapter previously reset HttpClient.BaseAddress on every call; the second live authorization
check failed before photo settings could load. The fix uses a configured absolute request
URI without mutating a started client, retaining every live authorization check. Regression
tests cover repeated/parallel scope reads and the real adapter through photo read, preview,
save and delivery. This does not change grants, image approvals or database schema.
The adapter correction was subsequently verified and activated in release
`147b1231138b4febb9cf7e9c0822f316`, with eight installed live-scope reads checked. That
rollout is distinct from UX11's later source-only category implementation.

1. Sign in to Customer Portal, select the restaurant workspace and open **Media management**.
2. Under **Menu images**, choose **Menu image branch**, then **Load menu and images**.
3. Click a menu item's name. Choose **Use for selected item** from the uploaded product image list, or choose a ready image by filename.
4. For a new file, choose **Upload a new product image**, then **Upload for selected menu item**. JPEG/PNG/WebP up to10MiB follow existing checksums, malware/safety checks and variant processing. Wait for `ready`, click **Refresh images**, then select it. No UUID typing is needed.
5. Check the preview, tick the public-menu approval and click **Save menu image**. To clear, choose **Remove image assignment**, approve and save. A conflict or uncertain response discards the editable version: select the product again to reload before saving.
6. Refresh the guest QR menu and Windows POS menu. Photos appear alongside names/prices. Missing/deleted/unready photos leave the text menu usable. Screensaver approval and manual PromptPay bank QR configuration remain separate.

## Prerequisites and rollout

Apply Catalog migration6 with compatibility0.41.0 before running the matching Catalog, Media, Order, Customer BFF, frontend, local edge and POS package. Other schemas are unchanged. Back up Catalog and preserve existing orders, shifts, payment recovery, playlists, preferences, certificates and storage. Existing default runtime grants must include the new Catalog tables. Do not activate a mismatched native package or interrupt an active cashier sale.

The optional local Media profile must be enabled, healthy and processing variants. Catalog now uses `Services:Media` and its existing `WorkloadIdentity:*` credentials; its token must pass Media's configured API audience and exact Catalog client check. Order reuses `QrGuestCatalog` and its Order workload identity. BFF reuses `Services:Catalog`, Directory and existing guest Order client. Staff direct uploads still require the configured `Bff:MediaUploadOrigin`/storage CORS. No new public storage bucket, grant, QR flag or native endpoint setting is introduced. The retained first-draft launcher derives the highest compatibility dynamically; the standalone migration launcher default remains0.38.0.

Managers need branch menu.read/write and organization media.asset.read for selection/preview, plus existing media.asset.manage for upload/delete. Cashier selected-photo delivery needs only live branch menu.read. Phone photos need an active guest visit. No anonymous raw Media route or staff authority is exposed at the guest origin. See [API/security](../API/Menu-Photos.md).

Browsers remount same-origin, no-store images every60 seconds and after visibility changes. Native POS fetches optional photos after **Refresh menu**, up to200 items/four concurrent requests, and decodes to192px in memory. Photos have a60-second lease checked by five-second UI timer ticks; late responses cannot extend it. It clears images/cancels stale responses on lock, sign-out, menu reload and existing receipt privacy transitions. Refresh retries occur only in an active signed-in Checkout; a locked screensaver uses its existing independent gallery. No photo is persisted in native recovery and no photo failure changes the original item, quote, cart or tender.

## Verification and limits

The subsequent disabled-selection correction is installed from verified candidate
`3f405c5c-e19d-4de1-ad29-b2ad39561b6c`, with release
`147b1231138b4febb9cf7e9c0822f316` and pilot `aa876384f4074641a4427d4fa77479c8`.
Its full gate passes1,357 unit tests,119 HTTP cases,11 gallery/menu-photo browser cases
and the required existing matrices. Both real branch-scope adapter regression tests fail
before the fix and pass afterward. Eight consecutive live scope lookups using the installed
Catalog assembly and unchanged workload identity pass. No schema or grant change is added.
Fresh backups and comparison preserve eight local files and26 Catalog/54 POS/10 Restaurant/
31 Order/five Media pre-existing business-table sections before launch. Fresh installed
acceptance pack `5f2a77ebdb794f09b7d71657d9ec862d` has all17 cases pending; phone cases also
remain pending. The separate documentation subagent was unavailable; the primary agent
reviewed the correction, component guidance and architecture consistency.

After this update, refresh the portal, reload the branch menu/images and select the menu
item again to discard the earlier failed settings load. Select a ready image, inspect its
preview and explicitly approve/save. Uploading alone does not approve an image. No retained
image assignment or payment is created by the diagnostic checks.

Source candidate `ce9b662d-5c9a-4d7e-91eb-198be7b9b21e` completed the full gate on
30 October 2026 at 13:05 Singapore time: 1,355 unit tests, 119 HTTP cases, 11 gallery/menu-photo
browser cases and all existing required matrices passed. Ten owning cases verified cleanup
and broker recovery; four drawer owning cases verified cleanup. The verification hash matches
the candidate, with release/production readiness remaining false.

After user-confirmed POS closure, release `cd1a5e8b39da4234a4bbcf7c57018862` and fresh pilot
`82010ae7aa8f40e194ccdc5fd16b8853` were activated at 13:16 Singapore time. Read-only checks
returned processed PNG200, wrong workload403 and unapproved product404. Runtime grants and
Catalog6 were verified. Preservation compared 24 Catalog, 54 POS, 10 Restaurant, 31 Order and
five Media pre-existing business-table COPY sections, all unchanged; migration/outbox/inbox
metadata is excluded. Fresh operator pack `b78fc403f49a4717919a08e97f8432f0` has a valid
installed binding with all 17 cases pending; all 24 phone cases are pending. No assignment,
approval, financial or hardware action was performed by the agent.

Focused tests: `dotnet test tests/Unit/NexaConnect.UnitTests --filter "FullyQualifiedName~MenuPhoto|FullyQualifiedName~MediaManagementTests|FullyQualifiedName~QrMenuTests"`; HTTP `MenuPhotoBoundaryTests`, `MenuPhotoGuestBoundaryTests` and `QrPilotEdgeTests`; owning PostgreSQL via `scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure`; browser `npm run test:e2e:gallery` from `src/Frontend`; native presentation via `dotnet run --project tests/Windows/NexaConnect.PosTouchTests`. The complete source-bound gate is `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` and includes menu-image HTTP/persistence/gallery coverage.

Use synthetic files and disposable data for automated tests. Record actual manager upload, same-organization reuse, Safari/Chrome QR display and installed POS photo/lock behavior separately after activation. Source tests never pass operator acceptance, release readiness, production hosting or physical printer/drawer checks. Offline image caching, multi-image product galleries and automatic migration of old uploads into approved selections are not implemented. Existing uploads need explicit selection/approval.
