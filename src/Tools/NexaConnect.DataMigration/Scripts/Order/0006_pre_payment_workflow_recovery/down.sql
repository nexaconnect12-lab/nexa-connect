DO $$ BEGIN IF EXISTS(SELECT 1 FROM orders WHERE status IN('inventory_reserved','kitchen_accepted') OR workflow_recovery_claim_id IS NOT NULL OR (status='submitted' AND workflow_payment_method IN('cash_manual','promptpay_manual'))) THEN RAISE EXCEPTION 'Cannot downgrade Order migration 6 after recoverable workflow state exists.'; END IF; END $$;
DROP INDEX ix_orders_workflow_recovery;
ALTER TABLE orders DROP CONSTRAINT ck_orders_workflow_recovery_attempts;
ALTER TABLE orders DROP CONSTRAINT ck_orders_workflow_payment_method;
ALTER TABLE orders DROP COLUMN workflow_recovery_last_error_category;
ALTER TABLE orders DROP COLUMN workflow_recovery_attempt_count;
ALTER TABLE orders DROP COLUMN workflow_recovery_next_attempt_at_utc;
ALTER TABLE orders DROP COLUMN workflow_recovery_locked_until_utc;
ALTER TABLE orders DROP COLUMN workflow_recovery_claim_id;
ALTER TABLE orders DROP COLUMN workflow_correlation_id;
ALTER TABLE orders DROP COLUMN workflow_payment_method;
ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN
('draft','submitted','accepted','payment_pending','payment_review','preparing','ready','completed','cancelled'));
