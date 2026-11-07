CREATE TABLE manual_refund_facts (
    operation_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    settlement_id uuid NOT NULL,
    method text NOT NULL CHECK (method IN ('cash','promptpay_manual')),
    amount numeric(19,4) NOT NULL CHECK (amount>0),
    currency char(3) NOT NULL CHECK (currency='THB'),
    cash_movement_id uuid UNIQUE,
    refunded_at_utc timestamptz NOT NULL,
    payload_hash char(64) NOT NULL,
    CHECK ((method='cash') = (cash_movement_id IS NOT NULL))
);
CREATE INDEX ix_manual_refund_facts_window ON manual_refund_facts (organization_id,branch_id,refunded_at_utc);
