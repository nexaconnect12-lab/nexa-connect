-- requires-schema-version: 1

INSERT INTO restaurants
    (id, organization_id, code, name, legal_name, default_currency, default_time_zone,
     status, created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES ('ca000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-000000000001',
    'harbour_bistro', 'Harbour Bistro', 'Demo Hospitality Pte. Ltd.', 'SGD',
    'Asia/Singapore', 'active', '2026-01-01T00:00:00Z', 'sample-data',
    '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO branches
    (id, restaurant_id, code, name, time_zone, currency, phone_number, email_address,
     address_line_1, city, postal_code, country_code, business_configuration, status,
     opened_at_utc, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES ('ca000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000001',
    'marina', 'Marina Branch', 'Asia/Singapore', 'SGD', '+6500000000',
    'hello@example.test', '1 Fictional Quay', 'Singapore', '000001', 'SG',
    '{"serviceChargePercent":10}'::jsonb, 'active', '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO dining_areas
    (id, branch_id, code, name, display_order, status, created_at_utc, created_by,
     updated_at_utc, updated_by, concurrency_version)
VALUES ('ca000000-0000-0000-0000-000000000010', 'ca000000-0000-0000-0000-000000000002',
    'main_hall', 'Main Hall', 10, 'active', '2026-01-01T00:00:00Z', 'sample-data',
    '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO dining_tables
    (id, branch_id, dining_area_id, code, display_name, capacity, qr_context_id,
     display_order, status, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES ('ca000000-0000-0000-0000-000000000011', 'ca000000-0000-0000-0000-000000000002',
    'ca000000-0000-0000-0000-000000000010', 't01', 'Table 1', 4,
    'ca000000-0000-0000-0000-000000000012', 10, 'available',
    '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO business_hours
    (id, branch_id, schedule_kind, day_of_week, effective_date, interval_sequence,
     opens_at, closes_at, is_closed, label, created_at_utc, created_by, updated_at_utc,
     updated_by, concurrency_version)
VALUES ('ca000000-0000-0000-0000-000000000013', 'ca000000-0000-0000-0000-000000000002',
    'weekly', 1, NULL, 1, '09:00', '22:00', false, 'Monday service',
    '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO preparation_stations
    (id, branch_id, code, name, station_type, display_order, status, created_at_utc,
     created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000003', 'ca000000-0000-0000-0000-000000000002',
     'hot_kitchen', 'Hot Kitchen', 'kitchen', 10, 'active', '2026-01-01T00:00:00Z',
     'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000004', 'ca000000-0000-0000-0000-000000000002',
     'coffee_bar', 'Coffee Bar', 'bar', 20, 'active', '2026-01-01T00:00:00Z',
     'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('ca000000-0000-0000-0000-000000000014', 'restaurant.sample-data.loaded', 1,
    'restaurant', 'ca000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
