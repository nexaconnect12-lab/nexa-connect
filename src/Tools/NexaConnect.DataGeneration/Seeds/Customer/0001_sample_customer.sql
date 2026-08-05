-- requires-schema-version: 1

INSERT INTO customers
    (id, organization_id, customer_number, identity_subject_id, display_name, status,
     contact_preferences, attributes, created_at_utc, created_by, updated_at_utc,
     updated_by, concurrency_version)
VALUES ('c5000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-000000000001',
    'DEMO-0001', 'sample-customer-001', 'Alex Example', 'active',
    '{"email":true}'::jsonb, '{"fictional":true}'::jsonb,
    '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO customer_contacts
    (id, customer_id, contact_type, contact_value, normalized_value, is_primary,
     is_verified, verified_at_utc, status, created_at_utc, updated_at_utc)
VALUES ('c5000000-0000-0000-0000-000000000002', 'c5000000-0000-0000-0000-000000000001',
    'email', 'alex@example.test', 'alex@example.test', true, true,
    '2026-01-01T00:00:00Z', 'active', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO customer_addresses
    (id, customer_id, address_type, recipient_name, line_1, city, postal_code,
     country_code, delivery_instructions, is_primary, status, created_at_utc,
     updated_at_utc)
VALUES ('c5000000-0000-0000-0000-000000000003', 'c5000000-0000-0000-0000-000000000001',
    'delivery', 'Alex Example', '10 Fictional Street', 'Singapore', '000010', 'SG',
    'Sample address only', true, 'active', '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO loyalty_accounts
    (id, customer_id, program_code, loyalty_number, points_balance, tier_code,
     status, joined_at_utc, updated_at_utc, concurrency_version)
VALUES ('c5000000-0000-0000-0000-000000000004', 'c5000000-0000-0000-0000-000000000001',
    'DEMO_REWARDS', 'DEMO-L-0001', 125.0000, 'silver', 'active',
    '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('c5000000-0000-0000-0000-000000000005', 'customer.sample-data.loaded', 1,
    'customer', 'c5000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
