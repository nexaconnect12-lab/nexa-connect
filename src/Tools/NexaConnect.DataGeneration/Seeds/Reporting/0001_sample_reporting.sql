-- requires-schema-version: 1

INSERT INTO sales_facts
    (order_id, source_event_id, organization_id, restaurant_id, branch_id,
     terminal_id, employee_identity_subject_id, customer_id, channel, service_type,
     currency, subtotal_amount, discount_amount, service_charge_amount, tax_amount,
     total_amount, order_status, ordered_at_utc, completed_at_utc, projected_at_utc,
     source_event_version)
VALUES ('0d000000-0000-0000-0000-000000000001', 'b0000000-0000-0000-0000-000000000001',
    'd0000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000002',
    'sample-user-001', 'c5000000-0000-0000-0000-000000000001', 'pos', 'dine_in',
    'SGD', 16.4000, 0.0000, 1.6400, 1.4760, 19.5160, 'completed',
    '2026-01-01T11:55:00Z', '2026-01-01T12:30:00Z',
    '2026-01-01T12:31:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO item_sales_facts
    (order_line_id, order_id, source_event_id, organization_id, restaurant_id,
     branch_id, product_id, product_variant_id, product_name_snapshot, category_id,
     category_name_snapshot, quantity, gross_amount, discount_amount, tax_amount,
     net_amount, ordered_at_utc, projected_at_utc, source_event_version)
VALUES ('0d000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    'b0000000-0000-0000-0000-000000000002', 'd0000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000002',
    'ca000000-0000-0000-0000-000000000301', NULL, 'Harbour Burger',
    'ca000000-0000-0000-0000-000000000201', 'Main Dishes', 1.000, 16.4000,
    0.0000, 1.4760, 17.8760, '2026-01-01T11:55:00Z',
    '2026-01-01T12:31:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO payment_facts
    (payment_intent_id, source_event_id, organization_id, restaurant_id, branch_id,
     order_id, payment_method, provider_code, currency, paid_amount, refunded_amount,
     payment_status, paid_at_utc, projected_at_utc, source_event_version)
VALUES ('a2000000-0000-0000-0000-000000000001', 'b0000000-0000-0000-0000-000000000003',
    'd0000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    'card', 'sample_provider', 'SGD', 19.5160, 19.5160, 'refunded',
    '2026-01-01T12:21:00Z', '2026-01-01T12:41:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO kitchen_time_facts
    (kitchen_ticket_item_id, source_event_id, organization_id, restaurant_id,
     branch_id, order_id, order_line_id, preparation_station_id, queued_at_utc,
     started_at_utc, ready_at_utc, completed_at_utc, queue_seconds,
     preparation_seconds, total_seconds, final_status, projected_at_utc,
     source_event_version)
VALUES ('a1000000-0000-0000-0000-000000000002', 'b0000000-0000-0000-0000-000000000004',
    'd0000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    '0d000000-0000-0000-0000-000000000002', 'ca000000-0000-0000-0000-000000000003',
    '2026-01-01T12:00:00Z', '2026-01-01T12:02:00Z', '2026-01-01T12:15:00Z',
    '2026-01-01T12:16:00Z', 120, 780, 960, 'completed',
    '2026-01-01T12:31:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO shift_cash_facts
    (shift_id, source_event_id, organization_id, restaurant_id, branch_id,
     terminal_id, employee_identity_subject_id, currency, opening_amount,
     cash_sales_amount, cash_refunds_amount, pay_in_amount, pay_out_amount,
     expected_closing_amount, actual_closing_amount, variance_amount, opened_at_utc,
     closed_at_utc, projected_at_utc, source_event_version)
VALUES ('a3000000-0000-0000-0000-000000000003', 'b0000000-0000-0000-0000-000000000005',
    'd0000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000002',
    'sample-user-001', 'SGD', 100.0000, 19.5160, 0.0000, 0.0000, 0.0000,
    119.5160, 119.5160, 0.0000, '2026-01-01T08:00:00Z',
    '2026-01-01T16:00:00Z', '2026-01-01T16:01:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO projection_checkpoints
    (projector_name, source_stream, position, last_event_id,
     last_event_occurred_at_utc, updated_at_utc)
VALUES ('sample-sales-projector', 'orders', 1,
    'b0000000-0000-0000-0000-000000000001', '2026-01-01T12:30:00Z',
    '2026-01-01T12:31:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO processed_messages (message_id, consumer_name, processed_at_utc)
VALUES ('b0000000-0000-0000-0000-000000000001', 'sample-reporting-consumer',
    '2026-01-01T12:31:00Z')
ON CONFLICT DO NOTHING;
