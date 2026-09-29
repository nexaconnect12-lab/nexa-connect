ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN
('draft','submitted','accepted','inventory_reserved','kitchen_accepted','payment_pending','payment_review','preparing','ready','completed','cancelled','cancellation_pending','cancellation_review'));

CREATE TABLE order_cancellations (
    order_id uuid PRIMARY KEY REFERENCES orders(id),
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    operation_id uuid NOT NULL UNIQUE,
    reason varchar(200) NOT NULL,
    actor_subject_id varchar(200) NOT NULL,
    authorization_decision_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    from_status varchar(32) NOT NULL,
    release_inventory boolean NOT NULL,
    cancel_kitchen boolean NOT NULL,
    status varchar(16) NOT NULL,
    claim_id uuid NULL,
    locked_until_utc timestamptz NULL,
    next_attempt_at_utc timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    last_error_category varchar(64) NULL,
    requested_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_order_cancellations_reason CHECK (char_length(btrim(reason)) BETWEEN 1 AND 200),
    CONSTRAINT ck_order_cancellations_from_status CHECK (from_status IN ('submitted','inventory_reserved','kitchen_accepted')),
    CONSTRAINT ck_order_cancellations_status CHECK (status IN ('pending','completed','blocked')),
    CONSTRAINT ck_order_cancellations_attempts CHECK (attempt_count >= 0),
    CONSTRAINT ck_order_cancellations_completion CHECK ((status='completed') = (completed_at_utc IS NOT NULL)),
    CONSTRAINT ck_order_cancellations_dependencies CHECK
      ((from_status='submitted' AND NOT release_inventory AND NOT cancel_kitchen)
       OR (from_status='inventory_reserved' AND release_inventory AND NOT cancel_kitchen)
       OR (from_status='kitchen_accepted' AND release_inventory AND cancel_kitchen))
);
CREATE INDEX ix_order_cancellations_recovery ON order_cancellations(next_attempt_at_utc,order_id)
WHERE status='pending';
COMMENT ON TABLE order_cancellations IS 'One immutable operator cancellation identity plus fenced compensation state per pre-payment Order.';
