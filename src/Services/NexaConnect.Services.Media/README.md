# Media service

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

Deletion and processing jobs back off exponentially for ten attempts. Monitor `media_object_deletions` and `media_processing_jobs` ordered by `next_attempt_at_utc`; terminal rows require operator review. Drain both tables and stop Media before downgrading migration 4 or 3.
