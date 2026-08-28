DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM payment_intents WHERE status IN ('voiding','void_unknown','voided','void_failed') OR provider_void_id IS NOT NULL OR void_lease_owner IS NOT NULL OR void_attempt_count <> 0 OR void_last_reconciled_at_utc IS NOT NULL) THEN
  RAISE EXCEPTION 'Payment migration 7 downgrade requires void recovery state to be reconciled and archived first';
 END IF;
END $$;
DROP INDEX ix_payment_intents_expired_void_leases;
DROP INDEX uq_payment_intents_provider_void;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_status;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending','authorizing','unknown','requires_action','authorized','capturing','capture_unknown','captured','failed','cancelled','expired'));
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled','payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain','payment.capture.reconciled'));
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_void_attempts;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_void_lease_owner;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_void_provider_ref;
ALTER TABLE payment_intents DROP COLUMN voided_at_utc;
ALTER TABLE payment_intents DROP COLUMN void_last_reconciled_at_utc;
ALTER TABLE payment_intents DROP COLUMN void_attempt_count;
ALTER TABLE payment_intents DROP COLUMN void_lease_expires_at_utc;
ALTER TABLE payment_intents DROP COLUMN void_lease_owner;
ALTER TABLE payment_intents DROP COLUMN provider_void_id;
