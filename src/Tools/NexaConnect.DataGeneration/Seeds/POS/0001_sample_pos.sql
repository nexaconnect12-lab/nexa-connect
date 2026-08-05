-- requires-schema-version: 1

INSERT INTO stores
    (id, restaurant_id, branch_id, code, name, operational_status, configuration,
     created_at_utc, created_by, updated_at_utc, updated_by, concurrency_version)
VALUES ('a3000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', 'marina_store', 'Marina POS Store',
    'active', '{"offlineMode":true}'::jsonb, '2026-01-01T00:00:00Z', 'sample-data',
    '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO terminals
    (id, restaurant_id, store_id, code, device_type, registration_status,
     registered_at_utc, revoked_at_utc, last_seen_at_utc, last_sync_at_utc,
     configuration, created_at_utc, updated_at_utc, concurrency_version)
VALUES ('a3000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000001',
    'a3000000-0000-0000-0000-000000000001', 'pos_01', 'pos', 'active',
    '2026-01-01T00:00:00Z', NULL, '2026-01-01T12:30:00Z',
    '2026-01-01T12:30:00Z', '{"testMode":true}'::jsonb,
    '2026-01-01T00:00:00Z', '2026-01-01T12:30:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO shifts
    (id, store_id, terminal_id, employee_identity_subject_id, shift_number, status,
     opened_at_utc, closed_at_utc, opened_by, closed_by, created_at_utc,
     updated_at_utc, concurrency_version)
VALUES ('a3000000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000001',
    'a3000000-0000-0000-0000-000000000002', 'sample-user-001', 'DEMO-SHIFT-0001',
    'closed', '2026-01-01T08:00:00Z', '2026-01-01T16:00:00Z', 'sample-user-001',
    'sample-user-001', '2026-01-01T08:00:00Z', '2026-01-01T16:00:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO cash_sessions
    (id, store_id, shift_id, currency, opening_amount, expected_closing_amount,
     actual_closing_amount, variance_amount, status, opened_at_utc, closed_at_utc,
     created_at_utc, updated_at_utc, concurrency_version)
VALUES ('a3000000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000001',
    'a3000000-0000-0000-0000-000000000003', 'SGD', 100.0000, 119.5160,
    119.5160, 0.0000, 'closed', '2026-01-01T08:00:00Z',
    '2026-01-01T16:00:00Z', '2026-01-01T08:00:00Z',
    '2026-01-01T16:00:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO cash_movements
    (id, cash_session_id, movement_type, amount, order_id, payment_id, reason_code,
     occurred_at_utc, recorded_by)
VALUES ('a3000000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000004',
    'sale', 19.5160, '0d000000-0000-0000-0000-000000000001',
    'a2000000-0000-0000-0000-000000000001', NULL, '2026-01-01T12:21:00Z',
    'sample-user-001')
ON CONFLICT DO NOTHING;

INSERT INTO sync_operations
    (id, terminal_id, client_operation_id, operation_type, payload_hash, status,
     response_status, response_reference_id, error_code, received_at_utc,
     completed_at_utc)
VALUES ('a3000000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000002',
    '0d000000-0000-0000-0000-000000000005', 'create_order', 'sample-hash-0001',
    'completed', 201, '0d000000-0000-0000-0000-000000000001', NULL,
    '2026-01-01T11:55:00Z', '2026-01-01T11:55:01Z')
ON CONFLICT DO NOTHING;

INSERT INTO sync_checkpoints
    (terminal_id, stream_name, cursor_value, synchronized_at_utc, concurrency_version)
VALUES ('a3000000-0000-0000-0000-000000000002', 'catalog', 'sample-cursor-0001',
    '2026-01-01T12:30:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('a3000000-0000-0000-0000-000000000007', 'pos.sample-data.loaded', 1,
    'store', 'a3000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
