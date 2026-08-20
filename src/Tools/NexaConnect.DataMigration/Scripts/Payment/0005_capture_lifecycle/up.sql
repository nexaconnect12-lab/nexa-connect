ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_status;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending','authorizing','unknown','requires_action','authorized','capturing','capture_unknown','captured','failed','cancelled','expired'));
ALTER TABLE payment_intents ADD COLUMN provider_capture_id text NULL;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_provider_capture CHECK (provider_capture_id IS NULL OR char_length(btrim(provider_capture_id)) BETWEEN 1 AND 200);
CREATE UNIQUE INDEX uq_payment_intents_provider_capture ON payment_intents(provider_capture_id) WHERE provider_capture_id IS NOT NULL;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled','payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain'));
COMMENT ON COLUMN payment_intents.provider_capture_id IS 'Sanitized provider capture reference only; never PAN, CVV, token, or provider response content.';
