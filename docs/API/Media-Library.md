# Shared product-photo library

Media management provides private staff browsing, safe thumbnails and shared uploads. See [operator workflow and limits](../Deployment/Media-Library.md) and [ADR-063](../Architecture/Decisions/ADR-063-shared-photo-library.md).

## Routes and bounds

| Customer BFF | Owning Media service |
| --- | --- |
| `GET /bff/customer/media-library` | `GET /api/media/v1/customer/organizations/{organization}/library` |
| `GET /bff/customer/media-library/assets/{asset}/thumbnail` | `GET /api/media/v1/customer/organizations/{organization}/library/assets/{asset}/thumbnail` |
| `POST /bff/customer/media/uploads` | `POST /api/media/v1/customer/organizations/{organization}/assets/uploads` |
| `POST /bff/customer/media/{asset}/complete` | `POST /api/media/v1/customer/organizations/{organization}/assets/{asset}/complete` |

An organization-library upload uses `ownerService: "media"`, `ownerType: "photo_library"`
and `ownerId` equal to the protected selected organization. Media validates that equality
before allocating a key. The existing `media.asset.manage` grant, file metadata, signed
PUT with checksum, completion version, scanning and quotas apply per file. The portal
submits at most ten files in sequence and reports each result. A legacy `catalog` /
`product` upload still validates its actual Catalog owner.

Browse accepts `search` (trimmed, at most 100 characters, no control characters), `page` (1–10000, default 1), and `readyOnly` (default false). Search is a literal case-insensitive filename substring, not SQL wildcards. Response is `{items,hasMore,page}` with at most 24 existing asset summaries. It includes original filename, MIME, byte size, processing state, upload/processing timestamps, version and original owner identity; it does not contain storage keys or signed URLs. Results include live `catalog` / `product` and same-organization `media` / `photo_library` assets. Deleted assets and bank QR purposes are excluded. Newest upload timestamp then descending asset UUID determines order. There is no total count or immutable browsing snapshot.

The ready-only picker cannot select an unprocessed asset. Thumbnail delivery independently requires a same-organization ready product photo, reuses `MenuPhotoDelivery`'s lifecycle/version/key rechecks and emits a metadata-free PNG bounded to 512 px and 512 KiB. The BFF accepts only `image/png` up to 524288 bytes or browse `application/json` up to 98304 bytes. Responses are no-store, thumbnails use `nosniff`, and no original object URL is embedded in the grid.

## Authority and failures

CustomerSession and the protected subject-bound tenant cookie select the BFF organization. The Application use case rechecks Directory membership for that subject and `nexa_connect`; a query organization cannot override it. Media requires an existing customer realm role plus live organization `media.asset.read` before and after repository/storage access. Browse/thumbnail rights do not grant upload, deletion, branch approval or financial authority. Existing download/delete routes retain their own live permissions and version checks; see the Media component README.

Invalid queries return 400; missing/invalid staff session returns 401; membership/permission denial returns 403; missing, deleted or unready thumbnails return 404. Dependency, invalid-content and bounded-timeout failures return 503 without downstream bodies. Media owns a 12-second deadline and the BFF a 20-second deadline; the SPA also aborts reads/actions after 20 seconds. Refresh is explicit after an uncertain mutation; selecting a photo never retries or commits an approval.

## Observability

Existing shared JSON/OTLP telemetry uses `nexaconnect-media` and `nexaconnect-customer-bff`. Query `{service_name="nexaconnect-media"} |= "Media library"` or `{service_name="nexaconnect-customer-bff"} |= "Media library BFF"`, then filter the validated correlation ID. Logs contain safe denial/status/failure categories, not filenames, search strings, object keys, bodies, cookies or tokens. Dedicated `MediaLibraryDirectory` and `MediaLibraryMedia` clients propagate validated correlation, disable redirects and remove URI-bearing default client loggers. They reuse `Services:PlatformDirectory` and `Services:Media`; there is no new configuration or ingress exposure.
