-- requires-schema-version: 1

INSERT INTO orders
    (id, restaurant_id, branch_id, customer_id, order_number, currency, channel,
     service_type, guest_count, subtotal_amount, discount_amount, service_charge_amount,
     tax_amount, total_amount, status, submitted_at_utc, completed_at_utc,
     cancelled_at_utc, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES ('0d000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', 'c5000000-0000-0000-0000-000000000001',
    'DEMO-ORDER-0001', 'SGD', 'pos', 'dine_in', 2, 16.4000, 0.0000, 1.6400,
    1.4760, 19.5160, 'completed', '2026-01-01T12:00:00Z',
    '2026-01-01T12:30:00Z', NULL, '2026-01-01T11:55:00Z', 'sample-data',
    '2026-01-01T12:30:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO order_lines
    (id, restaurant_id, branch_id, order_id, line_number, product_id,
     product_variant_id, sku_snapshot, name_snapshot, variant_name_snapshot,
     quantity, unit_price, discount_amount, tax_amount, line_total, notes, status,
     created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES ('0d000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    1, 'ca000000-0000-0000-0000-000000000301', NULL, 'DEMO-BURGER',
    'Harbour Burger', NULL, 1.000, 14.9000, 0.0000, 1.3410, 16.2410,
    'Fictional sample order', 'returned', '2026-01-01T11:55:00Z', 'sample-data',
    '2026-01-01T12:40:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO order_line_modifiers
    (id, order_id, order_line_id, modifier_group_id, modifier_option_id,
     group_name_snapshot, option_name_snapshot, quantity, unit_price, total_amount,
     created_at_utc, created_by)
VALUES ('0d000000-0000-0000-0000-000000000003', '0d000000-0000-0000-0000-000000000001',
    '0d000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000501',
    'ca000000-0000-0000-0000-000000000511', 'Burger Extras', 'Extra Cheese',
    1.000, 1.5000, 1.5000, '2026-01-01T11:55:00Z', 'sample-data')
ON CONFLICT DO NOTHING;

INSERT INTO order_status_history
    (id, order_id, from_status, to_status, reason_code, notes, changed_at_utc, changed_by)
VALUES ('0d000000-0000-0000-0000-000000000004', '0d000000-0000-0000-0000-000000000001',
    'ready', 'completed', NULL, 'Sample order completed', '2026-01-01T12:30:00Z',
    'sample-data')
ON CONFLICT DO NOTHING;

INSERT INTO order_channel_contexts
    (order_id, terminal_id, device_id, dining_table_id, employee_identity_subject_id,
     client_operation_id, collection_number, context)
VALUES ('0d000000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000002',
    NULL, 'ca000000-0000-0000-0000-000000000011', 'sample-user-001',
    '0d000000-0000-0000-0000-000000000005', 'D001', '{"fictional":true}'::jsonb)
ON CONFLICT DO NOTHING;

INSERT INTO returns
    (id, restaurant_id, branch_id, order_id, return_number, reason_code, total_amount,
     status, authorized_by, authorized_at_utc, completed_at_utc, created_at_utc,
     created_by, updated_at_utc, updated_by, concurrency_version)
VALUES ('0d000000-0000-0000-0000-000000000006', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    'DEMO-RETURN-0001', 'sample_quality', 19.5160, 'completed', 'sample-user-001',
    '2026-01-01T12:35:00Z', '2026-01-01T12:40:00Z', '2026-01-01T12:35:00Z',
    'sample-data', '2026-01-01T12:40:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO return_lines
    (id, order_id, return_id, order_line_id, quantity, amount, reason_code,
     created_at_utc, created_by)
VALUES ('0d000000-0000-0000-0000-000000000007', '0d000000-0000-0000-0000-000000000001',
    '0d000000-0000-0000-0000-000000000006', '0d000000-0000-0000-0000-000000000002',
    1.000, 19.5160, 'sample_quality', '2026-01-01T12:35:00Z', 'sample-data')
ON CONFLICT DO NOTHING;

INSERT INTO idempotency_records
    (operation_scope, idempotency_key, request_hash, response_status, response_body,
     resource_id, created_at_utc, expires_at_utc)
VALUES ('create-order', 'sample-order-0001', 'sample-hash-0001', 201,
    '{"sample":true}'::jsonb, '0d000000-0000-0000-0000-000000000001',
    '2026-01-01T00:00:00Z', '2099-01-01T00:00:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('0d000000-0000-0000-0000-000000000008', 'order.sample-data.loaded', 1,
    'order', '0d000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
