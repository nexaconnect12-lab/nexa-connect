-- requires-schema-version: 1

INSERT INTO menus
    (id, restaurant_id, branch_id, code, name, scope_type, valid_from_utc, valid_to_utc,
     status, created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000701', 'ca000000-0000-0000-0000-000000000001',
     NULL, 'all_day', 'All Day Menu', 'restaurant', '2026-01-01T00:00:00Z', NULL,
     'active', '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z',
     'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO menu_channels
    (restaurant_id, menu_id, channel, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000701',
     'pos', '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000701',
     'web', '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (restaurant_id, menu_id, channel) DO NOTHING;

INSERT INTO menu_categories
    (restaurant_id, menu_id, category_id, display_order, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000701',
     'ca000000-0000-0000-0000-000000000201', 10, '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000701',
     'ca000000-0000-0000-0000-000000000202', 20, '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (restaurant_id, menu_id, category_id) DO UPDATE SET
    display_order = EXCLUDED.display_order;

INSERT INTO menu_items
    (id, restaurant_id, menu_id, product_id, product_variant_id, display_order, status,
     created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000711', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000701', 'ca000000-0000-0000-0000-000000000301',
     NULL, 10, 'active', '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000712', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000701', 'ca000000-0000-0000-0000-000000000302',
     NULL, 20, 'active', '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000713', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000701', 'ca000000-0000-0000-0000-000000000302',
     'ca000000-0000-0000-0000-000000000401', 30, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;
