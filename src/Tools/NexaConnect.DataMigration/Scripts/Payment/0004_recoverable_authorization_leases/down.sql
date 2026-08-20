DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM payment_intents WHERE status IN ('authorizing', 'unknown', 'requires_action', 'authorized') OR lease_owner IS NOT NULL OR provider_authorization_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Payment migration 4 downgrade requires authorization records to be reconciled first';
    END IF;
END $$;
DROP INDEX ix_payment_intents_expired_authorization_leases;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
DELETE FROM payment_audit_records WHERE action IN ('payment.authorization.uncertain', 'payment.authorization.reconciled');
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created', 'payment.authorization.started', 'payment.authorization.succeeded', 'payment.authorization.failed'));
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_lease_owner;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_authorization_attempts;
ALTER TABLE payment_intents DROP COLUMN last_reconciled_at_utc;
ALTER TABLE payment_intents DROP COLUMN authorization_attempt_count;
ALTER TABLE payment_intents DROP COLUMN lease_expires_at_utc;
ALTER TABLE payment_intents DROP COLUMN lease_owner;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_status;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending', 'authorizing', 'requires_action', 'authorized', 'captured', 'failed', 'cancelled', 'expired'));
