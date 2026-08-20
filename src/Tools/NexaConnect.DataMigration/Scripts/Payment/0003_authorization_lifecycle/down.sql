DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM payment_intents WHERE status IN ('authorizing', 'authorized') OR provider_authorization_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Payment migration 3 downgrade requires authorization lifecycle records to be reconciled first';
    END IF;
END $$;
DROP INDEX uq_payment_intents_provider_authorization;
DROP TRIGGER tr_payment_audit_records_append_only ON payment_audit_records;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
DELETE FROM payment_audit_records WHERE action IN ('payment.authorization.started', 'payment.authorization.succeeded', 'payment.authorization.failed');
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created'));
CREATE TRIGGER tr_payment_audit_records_append_only BEFORE UPDATE OR DELETE ON payment_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_payment_audit_mutation();
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_failure_code;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_provider_authorization;
ALTER TABLE payment_intents DROP COLUMN failure_code;
ALTER TABLE payment_intents DROP COLUMN provider_authorization_id;
ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_status;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending', 'requires_action', 'authorized', 'captured', 'failed', 'cancelled', 'expired'));
