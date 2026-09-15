ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN
('draft','submitted','accepted','inventory_reserved','kitchen_accepted','payment_pending','payment_review','preparing','ready','completed','cancelled'));
ALTER TABLE orders ADD COLUMN workflow_payment_method text NULL;
ALTER TABLE orders ADD COLUMN workflow_correlation_id uuid NULL;
ALTER TABLE orders ADD COLUMN workflow_recovery_claim_id uuid NULL;
ALTER TABLE orders ADD COLUMN workflow_recovery_locked_until_utc timestamptz NULL;
ALTER TABLE orders ADD COLUMN workflow_recovery_next_attempt_at_utc timestamptz NULL;
ALTER TABLE orders ADD COLUMN workflow_recovery_attempt_count integer NOT NULL DEFAULT 0;
ALTER TABLE orders ADD COLUMN workflow_recovery_last_error_category text NULL;
ALTER TABLE orders ADD CONSTRAINT ck_orders_workflow_payment_method CHECK
(workflow_payment_method IS NULL OR char_length(btrim(workflow_payment_method)) BETWEEN 1 AND 64);
ALTER TABLE orders ADD CONSTRAINT ck_orders_workflow_recovery_attempts CHECK(workflow_recovery_attempt_count >= 0);
CREATE INDEX ix_orders_workflow_recovery ON orders(workflow_recovery_next_attempt_at_utc,updated_at_utc,id)
WHERE status IN('submitted','inventory_reserved') AND workflow_payment_method IN('cash_manual','promptpay_manual');
COMMENT ON COLUMN orders.workflow_payment_method IS 'Original checkout method. Recovery is enabled only for manual tenders and stops before payment.';
COMMENT ON COLUMN orders.workflow_recovery_claim_id IS 'Opaque fencing token for one pre-payment recovery attempt.';
