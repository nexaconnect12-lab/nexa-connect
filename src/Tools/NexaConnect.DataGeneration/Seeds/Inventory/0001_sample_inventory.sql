-- requires-schema-version: 1

INSERT INTO warehouses
    (id, restaurant_id, branch_id, code, name, warehouse_type, status, created_at_utc,
     created_by, updated_at_utc, updated_by, concurrency_version)
VALUES ('1a000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', 'marina_main', 'Marina Main Warehouse',
    'branch', 'active', '2026-01-01T00:00:00Z', 'sample-data',
    '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO stock_items
    (id, restaurant_id, warehouse_id, product_id, product_variant_id, unit_of_measure,
     on_hand_quantity, reserved_quantity, reorder_level, updated_at_utc, concurrency_version)
VALUES ('1a000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000001',
    '1a000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000301',
    NULL, 'each', 50.0000, 1.0000, 10.0000, '2026-01-01T00:00:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO stock_movements
    (id, stock_item_id, movement_type, quantity_delta, balance_after, reference_type,
     reference_id, reason_code, occurred_at_utc, recorded_by)
VALUES ('1a000000-0000-0000-0000-000000000003', '1a000000-0000-0000-0000-000000000002',
    'receipt', 50.0000, 50.0000, 'sample_load', NULL, 'initial_sample_stock',
    '2026-01-01T00:00:00Z', 'sample-data')
ON CONFLICT DO NOTHING;

INSERT INTO stock_reservations
    (id, stock_item_id, order_id, order_line_id, quantity, status, reserved_at_utc,
     expires_at_utc, completed_at_utc, concurrency_version)
VALUES ('1a000000-0000-0000-0000-000000000004', '1a000000-0000-0000-0000-000000000002',
    '0d000000-0000-0000-0000-000000000001', '0d000000-0000-0000-0000-000000000002',
    1.0000, 'active', '2026-01-01T00:00:00Z', '2099-01-01T00:00:00Z', NULL, 1)
ON CONFLICT DO NOTHING;

INSERT INTO replenishment_requests
    (id, restaurant_id, warehouse_id, stock_item_id, requested_quantity,
     fulfilled_quantity, status, requested_at_utc, requested_by, completed_at_utc,
     updated_at_utc, concurrency_version)
VALUES ('1a000000-0000-0000-0000-000000000005', 'ca000000-0000-0000-0000-000000000001',
    '1a000000-0000-0000-0000-000000000001', '1a000000-0000-0000-0000-000000000002',
    20.0000, 0.0000, 'requested', '2026-01-01T00:00:00Z', 'sample-data', NULL,
    '2026-01-01T00:00:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO processed_messages (message_id, consumer_name, processed_at_utc)
VALUES ('1a000000-0000-0000-0000-000000000006', 'sample-inventory-consumer',
    '2026-01-01T00:00:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('1a000000-0000-0000-0000-000000000007', 'inventory.sample-data.loaded', 1,
    'warehouse', '1a000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
