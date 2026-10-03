CREATE TABLE refund_facts (
    refund_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    payment_intent_id uuid NOT NULL,
    amount numeric(19,4) NOT NULL CHECK(amount > 0),
    currency char(3) NOT NULL CHECK(currency ~ '^[A-Z]{3}$'),
    reason_code text NOT NULL CHECK(reason_code IN('customer_request','duplicate_charge','item_unavailable','service_issue','other')),
    refunded_at_utc timestamptz NOT NULL,
    cumulative_refunded_amount numeric(19,4) NOT NULL,
    captured_amount numeric(19,4) NOT NULL,
    receipt_number text NOT NULL UNIQUE CHECK(char_length(receipt_number) BETWEEN 4 AND 80),
    projected_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_refund_facts_totals CHECK(cumulative_refunded_amount >= amount AND cumulative_refunded_amount <= captured_amount)
);
CREATE INDEX ix_refund_facts_scope_time ON refund_facts(organization_id,branch_id,refunded_at_utc DESC,refund_id DESC);
CREATE INDEX ix_refund_facts_payment_intent ON refund_facts(payment_intent_id,refunded_at_utc,refund_id);

CREATE TABLE refund_fact_event_receipts (
    event_id uuid PRIMARY KEY,
    payload_hash text NOT NULL CHECK(length(payload_hash)=64)
);

COMMENT ON TABLE refund_facts IS 'Rebuildable Payment refund financial projection; ranges use refund occurrence time.';
