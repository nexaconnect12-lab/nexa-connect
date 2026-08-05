-- requires-schema-version: 1
-- Covers the remaining Catalog tables with fictional operational examples.

INSERT INTO product_barcodes
    (id, restaurant_id, product_id, product_variant_id, barcode_value, barcode_type,
     is_primary, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000801', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000301', NULL, '8880000000001', 'ean13', true,
     '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000802', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000302', 'ca000000-0000-0000-0000-000000000401',
     '8880000000002', 'ean13', true, '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (id) DO UPDATE SET
    barcode_value = EXCLUDED.barcode_value,
    barcode_type = EXCLUDED.barcode_type,
    is_primary = EXCLUDED.is_primary;

INSERT INTO product_availability
    (id, restaurant_id, branch_id, product_id, product_variant_id,
     availability_status, reason, available_from_utc, available_to_utc,
     created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000811', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000301',
     NULL, 'available', NULL, '2026-01-01T00:00:00Z', NULL,
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000812', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000302',
     'ca000000-0000-0000-0000-000000000401', 'available', NULL,
     '2026-01-01T00:00:00Z', NULL, '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    availability_status = EXCLUDED.availability_status,
    reason = EXCLUDED.reason,
    available_from_utc = EXCLUDED.available_from_utc,
    available_to_utc = EXCLUDED.available_to_utc,
    updated_at_utc = EXCLUDED.updated_at_utc,
    updated_by = EXCLUDED.updated_by;

INSERT INTO preparation_routes
    (id, restaurant_id, branch_id, product_id, product_variant_id,
     preparation_station_id, priority, status, created_at_utc, created_by,
     updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000821', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000301',
     NULL, 'ca000000-0000-0000-0000-000000000003', 10, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000822', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000302',
     'ca000000-0000-0000-0000-000000000401',
     'ca000000-0000-0000-0000-000000000004', 20, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    preparation_station_id = EXCLUDED.preparation_station_id,
    priority = EXCLUDED.priority,
    status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc,
    updated_by = EXCLUDED.updated_by;

INSERT INTO product_images
    (id, restaurant_id, product_id, media_asset_id, alt_text, display_order,
     is_primary, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000831', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000301', 'ca000000-0000-0000-0000-000000000901',
     'Fictional Harbour Burger sample image', 10, true,
     '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000832', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000302', 'ca000000-0000-0000-0000-000000000902',
     'Fictional Orchard Coffee sample image', 10, true,
     '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (id) DO UPDATE SET
    media_asset_id = EXCLUDED.media_asset_id,
    alt_text = EXCLUDED.alt_text,
    display_order = EXCLUDED.display_order,
    is_primary = EXCLUDED.is_primary;

-- Marked published so development message relays do not emit this illustrative event.
INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     correlation_id, causation_id, occurred_at_utc, published_at_utc, retry_count,
     next_attempt_at_utc, last_error_category)
VALUES
    ('ca000000-0000-0000-0000-000000000841', 'catalog.sample-data.loaded', 1,
     'catalog', 'ca000000-0000-0000-0000-000000000001',
     '{"source":"sample-data","fictional":true}'::jsonb,
     'sample-data-catalog', NULL, '2026-01-01T00:00:00Z',
     '2026-01-01T00:00:00Z', 0, NULL, NULL)
ON CONFLICT (id) DO UPDATE SET
    payload = EXCLUDED.payload,
    published_at_utc = EXCLUDED.published_at_utc,
    retry_count = EXCLUDED.retry_count,
    next_attempt_at_utc = EXCLUDED.next_attempt_at_utc,
    last_error_category = EXCLUDED.last_error_category;
