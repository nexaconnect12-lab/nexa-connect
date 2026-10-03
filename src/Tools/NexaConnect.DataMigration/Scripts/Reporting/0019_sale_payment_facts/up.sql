CREATE TABLE sale_fact_event_receipts
(
    event_id uuid PRIMARY KEY,
    payload_hash text NOT NULL CHECK(payload_hash ~ '^[0-9A-F]{64}$'),
    projected_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);
-- Existing rows are retained as explicitly unknown evidence, never silently reclassified.
ALTER TABLE payment_facts ADD COLUMN payment_origin text NOT NULL DEFAULT 'legacy_unknown'
    CHECK(payment_origin IN('legacy_unknown','payment_intent','manual_settlement'));
COMMENT ON COLUMN payment_facts.payment_intent_id IS 'Payment identity: provider intent or manual settlement UUID, distinguished by payment_origin.';
