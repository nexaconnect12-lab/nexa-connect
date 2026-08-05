-- requires-schema-version: 1

INSERT INTO media_assets
    (id, organization_id, owner_service, owner_type, owner_id, object_key,
     original_file_name, content_type, size_bytes, checksum_sha256, width_pixels,
     height_pixels, processing_status, uploaded_at_utc, processed_at_utc,
     deleted_at_utc, created_by, updated_at_utc, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000901', 'd0000000-0000-0000-0000-000000000001',
     'Catalog', 'product', 'ca000000-0000-0000-0000-000000000301',
     'sample/catalog/harbour-burger.jpg', 'harbour-burger.jpg', 'image/jpeg', 1024,
     'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 800, 600,
     'ready', '2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z', NULL,
     'sample-data', '2026-01-01T00:01:00Z', 1),
    ('ca000000-0000-0000-0000-000000000902', 'd0000000-0000-0000-0000-000000000001',
     'Catalog', 'product', 'ca000000-0000-0000-0000-000000000302',
     'sample/catalog/orchard-coffee.jpg', 'orchard-coffee.jpg', 'image/jpeg', 1024,
     'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB', 800, 600,
     'ready', '2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z', NULL,
     'sample-data', '2026-01-01T00:01:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO media_variants
    (id, media_asset_id, variant_name, object_key, content_type, size_bytes,
     checksum_sha256, width_pixels, height_pixels, status, created_at_utc)
VALUES ('a4000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000901',
    'thumbnail', 'sample/catalog/harbour-burger-thumbnail.jpg', 'image/jpeg', 256,
    'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC',
    200, 150, 'ready', '2026-01-01T00:01:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO media_processing_attempts
    (id, media_asset_id, attempt_number, worker_id, outcome, error_category,
     error_message, started_at_utc, completed_at_utc)
VALUES ('a4000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000901',
    1, 'sample-media-worker', 'succeeded', NULL, NULL,
    '2026-01-01T00:00:30Z', '2026-01-01T00:01:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('a4000000-0000-0000-0000-000000000003', 'media.sample-data.loaded', 1,
    'media_asset', 'ca000000-0000-0000-0000-000000000901',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
