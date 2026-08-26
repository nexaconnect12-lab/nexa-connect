DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM payment_intents WHERE capture_lease_owner IS NOT NULL OR capture_attempt_count <> 0 OR capture_last_reconciled_at_utc IS NOT NULL) THEN
        RAISE EXCEPTION 'Payment migration 6 downgrade requires capture recovery state to be reconciled and archived first';
    END IF;
END $$;
DROP INDEX ix_payment_intents_expired_capture_leases;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled','payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain'));
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_capture_attempts;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_capture_lease_owner;
ALTER TABLE payment_intents DROP COLUMN capture_last_reconciled_at_utc;
ALTER TABLE payment_intents DROP COLUMN capture_attempt_count;
ALTER TABLE payment_intents DROP COLUMN capture_lease_expires_at_utc;
ALTER TABLE payment_intents DROP COLUMN capture_lease_owner;
