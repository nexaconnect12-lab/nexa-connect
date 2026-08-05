-- requires-schema-version: 1

INSERT INTO kitchen_tickets
    (id, restaurant_id, branch_id, order_id, preparation_station_id, ticket_number,
     service_sequence, priority, status, queued_at_utc, started_at_utc, ready_at_utc,
     completed_at_utc, cancelled_at_utc, created_at_utc, updated_at_utc,
     concurrency_version)
VALUES ('a1000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000003', 'DEMO-KOT-0001', 1, 10, 'completed',
    '2026-01-01T12:00:00Z', '2026-01-01T12:02:00Z', '2026-01-01T12:15:00Z',
    '2026-01-01T12:16:00Z', NULL, '2026-01-01T12:00:00Z',
    '2026-01-01T12:16:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO kitchen_ticket_items
    (id, kitchen_ticket_id, order_line_id, product_id, product_variant_id,
     item_name_snapshot, variant_name_snapshot, modifiers_snapshot, quantity, notes,
     status, queued_at_utc, started_at_utc, ready_at_utc, completed_at_utc,
     cancelled_at_utc, updated_at_utc, concurrency_version)
VALUES ('a1000000-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000001',
    '0d000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000301',
    NULL, 'Harbour Burger', NULL, '[{"name":"Extra Cheese"}]'::jsonb, 1.000,
    'Fictional sample ticket', 'completed', '2026-01-01T12:00:00Z',
    '2026-01-01T12:02:00Z', '2026-01-01T12:15:00Z', '2026-01-01T12:16:00Z',
    NULL, '2026-01-01T12:16:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO kitchen_status_history
    (id, kitchen_ticket_id, kitchen_ticket_item_id, entity_type, from_status,
     to_status, reason_code, changed_at_utc, changed_by)
VALUES ('a1000000-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000001',
    'a1000000-0000-0000-0000-000000000002', 'item', 'ready', 'completed', NULL,
    '2026-01-01T12:16:00Z', 'sample-data')
ON CONFLICT DO NOTHING;

INSERT INTO kitchen_adjustments
    (id, source_message_id, order_id, order_line_id, adjustment_type, quantity_delta,
     instructions, received_at_utc, applied_at_utc, status)
VALUES ('a1000000-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000005',
    '0d000000-0000-0000-0000-000000000001', '0d000000-0000-0000-0000-000000000002',
    'quantity_change', 1.000, '{"sample":true}'::jsonb,
    '2026-01-01T12:01:00Z', '2026-01-01T12:01:30Z', 'applied')
ON CONFLICT DO NOTHING;

INSERT INTO processed_messages (message_id, consumer_name, processed_at_utc)
VALUES ('a1000000-0000-0000-0000-000000000005', 'sample-kitchen-consumer',
    '2026-01-01T12:01:30Z')
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('a1000000-0000-0000-0000-000000000006', 'kitchen.sample-data.loaded', 1,
    'kitchen_ticket', 'a1000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
