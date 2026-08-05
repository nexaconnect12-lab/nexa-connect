-- requires-schema-version: 1
-- Fictional, deterministic development data. Stable IDs are intentionally explicit.

INSERT INTO tax_classifications
    (id, restaurant_id, code, name, status, created_at_utc, created_by,
     updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000101', 'ca000000-0000-0000-0000-000000000001',
     'standard', 'Standard Tax', 'active', '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO categories
    (id, restaurant_id, parent_category_id, code, name, description, display_order,
     status, created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000201', 'ca000000-0000-0000-0000-000000000001',
     NULL, 'mains', 'Main Dishes', 'Fictional sample main dishes', 10, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000202', 'ca000000-0000-0000-0000-000000000001',
     NULL, 'drinks', 'Drinks', 'Fictional sample beverages', 20, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, description = EXCLUDED.description,
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO products
    (id, restaurant_id, tax_classification_id, sku, name, description, base_unit,
     attributes, status, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000301', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000101', 'DEMO-BURGER', 'Harbour Burger',
     'Fictional grilled burger', 'each', '{"spiceLevel":"mild"}'::jsonb, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000302', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000101', 'DEMO-COFFEE', 'Orchard Coffee',
     'Fictional house coffee', 'cup', '{"containsCaffeine":true}'::jsonb, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    tax_classification_id = EXCLUDED.tax_classification_id, sku = EXCLUDED.sku,
    name = EXCLUDED.name, description = EXCLUDED.description,
    base_unit = EXCLUDED.base_unit, attributes = EXCLUDED.attributes,
    status = EXCLUDED.status, updated_at_utc = EXCLUDED.updated_at_utc,
    updated_by = EXCLUDED.updated_by;

INSERT INTO product_variants
    (id, restaurant_id, product_id, code, name, attributes, display_order, status,
     created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000401', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000302', 'large', 'Large',
     '{"volumeMl":350}'::jsonb, 10, 'active', '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, attributes = EXCLUDED.attributes,
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO product_categories
    (restaurant_id, product_id, category_id, display_order, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000301',
     'ca000000-0000-0000-0000-000000000201', 10, '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000302',
     'ca000000-0000-0000-0000-000000000202', 10, '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (restaurant_id, product_id, category_id) DO UPDATE SET
    display_order = EXCLUDED.display_order;

INSERT INTO modifier_groups
    (id, restaurant_id, code, name, minimum_selections, maximum_selections,
     display_order, status, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000501', 'ca000000-0000-0000-0000-000000000001',
     'extras', 'Burger Extras', 0, 2, 10, 'active', '2026-01-01T00:00:00Z',
     'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name,
    minimum_selections = EXCLUDED.minimum_selections,
    maximum_selections = EXCLUDED.maximum_selections,
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO modifier_options
    (id, restaurant_id, modifier_group_id, code, name, is_default, display_order,
     status, created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000511', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000501', 'cheese', 'Extra Cheese', false, 10,
     'active', '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1),
    ('ca000000-0000-0000-0000-000000000512', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000501', 'jalapeno', 'Jalapeno', false, 20,
     'active', '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, is_default = EXCLUDED.is_default,
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO product_modifier_groups
    (id, restaurant_id, product_id, product_variant_id, modifier_group_id,
     display_order, status, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000521', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000301', NULL,
     'ca000000-0000-0000-0000-000000000501', 10, 'active',
     '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    display_order = EXCLUDED.display_order, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO price_lists
    (id, restaurant_id, branch_id, code, name, scope_type, currency, valid_from_utc,
     valid_to_utc, priority, status, created_at_utc, created_by, updated_at_utc,
     updated_by, concurrency_version)
VALUES
    ('ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000001',
     NULL, 'standard', 'Standard Prices', 'restaurant', 'SGD', '2026-01-01T00:00:00Z',
     NULL, 10, 'active', '2026-01-01T00:00:00Z', 'sample-data',
     '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT (id) DO UPDATE SET
    code = EXCLUDED.code, name = EXCLUDED.name, currency = EXCLUDED.currency,
    priority = EXCLUDED.priority, status = EXCLUDED.status,
    updated_at_utc = EXCLUDED.updated_at_utc, updated_by = EXCLUDED.updated_by;

INSERT INTO product_prices
    (id, restaurant_id, price_list_id, product_id, product_variant_id, amount,
     effective_from_utc, effective_to_utc, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000611', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000301',
     NULL, 14.9000, '2026-01-01T00:00:00Z', NULL, '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000612', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000302',
     NULL, 4.5000, '2026-01-01T00:00:00Z', NULL, '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000613', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000302',
     'ca000000-0000-0000-0000-000000000401', 5.5000, '2026-01-01T00:00:00Z', NULL,
     '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (id) DO UPDATE SET amount = EXCLUDED.amount;

INSERT INTO modifier_option_prices
    (id, restaurant_id, price_list_id, modifier_group_id, modifier_option_id,
     amount, effective_from_utc, effective_to_utc, created_at_utc, created_by)
VALUES
    ('ca000000-0000-0000-0000-000000000621', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000501',
     'ca000000-0000-0000-0000-000000000511', 1.5000, '2026-01-01T00:00:00Z', NULL,
     '2026-01-01T00:00:00Z', 'sample-data'),
    ('ca000000-0000-0000-0000-000000000622', 'ca000000-0000-0000-0000-000000000001',
     'ca000000-0000-0000-0000-000000000601', 'ca000000-0000-0000-0000-000000000501',
     'ca000000-0000-0000-0000-000000000512', 0.7500, '2026-01-01T00:00:00Z', NULL,
     '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT (id) DO UPDATE SET amount = EXCLUDED.amount;
