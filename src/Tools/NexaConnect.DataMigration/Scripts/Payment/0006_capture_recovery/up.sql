ALTER TABLE payment_intents ADD COLUMN capture_lease_owner text NULL;
ALTER TABLE payment_intents ADD COLUMN capture_lease_expires_at_utc timestamptz NULL;
ALTER TABLE payment_intents ADD COLUMN capture_attempt_count integer NOT NULL DEFAULT 0;
ALTER TABLE payment_intents ADD COLUMN capture_last_reconciled_at_utc timestamptz NULL;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_capture_lease_owner CHECK (capture_lease_owner IS NULL OR char_length(btrim(capture_lease_owner)) BETWEEN 1 AND 200);
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_capture_attempts CHECK (capture_attempt_count >= 0 AND capture_attempt_count <= 100);
CREATE INDEX ix_payment_intents_expired_capture_leases ON payment_intents (capture_lease_expires_at_utc)
    WHERE status = 'capturing' AND capture_lease_expires_at_utc IS NOT NULL;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled','payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain','payment.capture.reconciled'));
COMMENT ON COLUMN payment_intents.capture_lease_owner IS 'Opaque capture recovery worker identity; never a user identity or provider secret.';
COMMENT ON COLUMN payment_intents.capture_attempt_count IS 'Bounded number of provider capture status recovery attempts.';
COMMENT ON COLUMN payment_intents.capture_last_reconciled_at_utc IS 'Last durable provider capture status reconciliation timestamp.';
