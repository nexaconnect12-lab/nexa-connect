# Media service

Phase 5 adds metadata readiness and branch-authorized gallery bundle delivery. Set
`Services__Restaurant`; Media asks Restaurant for the caller's live menu-readable branch
selection, then revalidates ready, non-deleted Catalog product display variants. It converts
only those images to bounded PNGs and issues a maximum five-minute lease; no unrestricted
Media read grant is given to cashiers. All new HTTP clients use shared correlation and safe
structured Gallery boundary events. See [contract](../../../docs/API/Screensaver-Gallery.md)
and [limits/configuration/verification](../../../docs/Deployment/Next-Iteration-Phase-5.md).

Media owns tenant-scoped metadata and the S3-compatible object lifecycle. Apply migrations 1 through 4 and configure `ConnectionStrings__Media`, Platform Directory, Authorization, Catalog, object storage, safety, and quota settings.

`POST .../uploads` validates a Catalog product, accepts JPEG/PNG/WebP up to 10 MiB, and returns a ten-minute signed PUT. Upload start serializes organization quota checks. `MediaQuota__MaximumPendingUploads` defaults to 20; `MediaQuota__MaximumStoredBytes` defaults to 1 GiB of pending/ready original bytes. Generated variants are excluded and require separate bucket-capacity monitoring. Quota exhaustion returns `409`.

Completion requires provider-returned SHA-256 and size, reads no more than the declared size, checks the file signature, and scans with ClamAV. Unsafe content becomes non-downloadable `quarantined` metadata and enters durable deletion. Scanner or storage failure returns `503` without making the asset ready. Expired pending uploads become `failed`, emit `media.asset.upload-expired`, and enter deletion.

Successful completion transactionally enqueues migration-4 processing. `MediaMaintenanceWorker` alternates expiry and variant priority so either backlog progresses, rejects decoded images above 12,000 pixels per side or 40 million pixels, and uses SkiaSharp to generate non-upscaled WebP `thumbnail` (maximum 320x320) and `display` (maximum 1280x1280) variants with deterministic keys and retry-safe upserts. Variant finalization locks and rechecks the asset, so a concurrent delete either queues an existing variant or the worker queues its just-written object. Claim readers are closed before an empty-queue rollback or lease commit; provider work runs only after the lease transaction commits. Variant metadata is available at `GET .../{id}/variants`; original and variant downloads return five-minute signed URLs.

Production requires `MediaSafety__MalwareScanEnabled=true`, a private healthy ClamAV endpoint, bucket-scoped credentials, TLS, and portal-origin CORS. Startup requires migration 4. Local MinIO uses ports 9100/9101 and ClamAV uses loopback port 3310.

Service name: `nexaconnect-media`. Safe events include authorization denial, dependency failure, expired-upload cleanup, deletion retry/completion, and variant retry/completion. They log identifiers and failure categories, never tokens, object keys, filenames, checksums, or payloads.

Verification:

- Run `MediaManagementTests` and `MediaContentSafetyTests`.
- Run `MediaAuthenticatedHttpAcceptanceTests`.
- Set `NEXACONNECT_MEDIA_INTEGRATION_DB` to a disposable Development/Test PostgreSQL database and run `MediaPostgresAcceptanceTests`.
- Start MinIO/ClamAV, set `NEXA_MINIO_ACCEPTANCE=1` and `NEXA_CLAMAV_ACCEPTANCE=1`, and run `MediaObjectStorageAcceptanceTests`.
- Start the disposable Phase 8 stack and run `npm run test:e2e:phase8` from `src/Frontend` with the settings in [the joined browser acceptance guide](../../Frontend/e2e/phase8/README.md). This joins the configured Media service to the authenticated portal, BFF, storage, scanner, database, and worker; provider/configuration claims depend on the launched environment and the run does not replace recovery, load, TLS, CORS, credential, or operational validation.

Deletion and processing jobs back off exponentially for ten attempts. Monitor `media_object_deletions` and `media_processing_jobs` ordered by `next_attempt_at_utc`; terminal rows require operator review. Drain both tables and stop Media before downgrading migration 4 or 3.
## Dedicated-server pilot preparation

The [Phase 6 pilot](../../../docs/Deployment/Next-Iteration-Phase-6.md) bundles Media
and enables scanning, persistent PostgreSQL, workload authentication and outbox delivery.
Provision private S3-compatible storage with independent least-privileged keys,
ClamAV with current definitions and the existing `nexaconnect-media-service` client.
Keep Catalog/Restaurant/Authorization/Platform Directory dependencies on verified private
HTTPS; only authorized `/api/media/` routes pass the public application edge.
Back up object versions together with Media metadata/jobs, configuration and key rings.
The portable Linux build does not prove native image processing, storage/scanner access,
authenticated gallery delivery or recovery; run those target checks before exposure.
