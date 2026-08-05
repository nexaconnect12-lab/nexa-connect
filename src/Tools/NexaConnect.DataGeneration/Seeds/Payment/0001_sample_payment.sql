-- requires-schema-version: 1

INSERT INTO payment_intents
    (id, restaurant_id, branch_id, order_id, idempotency_key, amount, currency,
     payment_method, status, expires_at_utc, authorized_at_utc, captured_at_utc,
     failed_at_utc, created_at_utc, updated_at_utc, concurrency_version)
VALUES ('a2000000-0000-0000-0000-000000000001', 'ca000000-0000-0000-0000-000000000001',
    'ca000000-0000-0000-0000-000000000002', '0d000000-0000-0000-0000-000000000001',
    'sample-payment-0001', 19.5160, 'SGD', 'card', 'captured', NULL,
    '2026-01-01T12:20:00Z', '2026-01-01T12:21:00Z', NULL,
    '2026-01-01T12:19:00Z', '2026-01-01T12:21:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO provider_transactions
    (id, payment_intent_id, provider_code, provider_transaction_id, transaction_type,
     amount, currency, status, sanitized_response, processed_at_utc, created_at_utc)
VALUES ('a2000000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000001',
    'sample_provider', 'sample-provider-txn-0001', 'capture', 19.5160, 'SGD',
    'succeeded', '{"testMode":true}'::jsonb, '2026-01-01T12:21:00Z',
    '2026-01-01T12:19:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO refunds
    (id, payment_intent_id, order_return_id, idempotency_key, amount, currency,
     reason_code, status, requested_at_utc, completed_at_utc, failed_at_utc,
     requested_by, updated_at_utc, concurrency_version)
VALUES ('a2000000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000001',
    '0d000000-0000-0000-0000-000000000006', 'sample-refund-0001', 19.5160, 'SGD',
    'sample_return', 'completed', '2026-01-01T12:35:00Z',
    '2026-01-01T12:40:00Z', NULL, 'sample-user-001', '2026-01-01T12:40:00Z', 1)
ON CONFLICT DO NOTHING;

INSERT INTO reconciliation_records
    (id, provider_code, settlement_reference, payment_intent_id,
     provider_transaction_id, settlement_date, gross_amount, fee_amount, net_amount,
     currency, status, details, created_at_utc, updated_at_utc)
VALUES ('a2000000-0000-0000-0000-000000000004', 'sample_provider',
    'sample-settlement-0001', 'a2000000-0000-0000-0000-000000000001',
    'a2000000-0000-0000-0000-000000000002', '2026-01-02', 19.5160, 0.5000,
    19.0160, 'SGD', 'matched', '{"testMode":true}'::jsonb,
    '2026-01-02T00:00:00Z', '2026-01-02T00:00:00Z')
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('a2000000-0000-0000-0000-000000000005', 'payment.sample-data.loaded', 1,
    'payment_intent', 'a2000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
