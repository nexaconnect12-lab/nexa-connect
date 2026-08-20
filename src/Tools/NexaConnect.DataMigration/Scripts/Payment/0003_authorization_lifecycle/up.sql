ALTER TABLE payment_intents DROP CONSTRAINT ck_payment_intents_status;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending', 'authorizing', 'requires_action', 'authorized', 'captured', 'failed', 'cancelled', 'expired'));
ALTER TABLE payment_intents ADD COLUMN provider_authorization_id text NULL;
ALTER TABLE payment_intents ADD COLUMN failure_code text NULL;
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_provider_authorization CHECK (provider_authorization_id IS NULL OR char_length(btrim(provider_authorization_id)) BETWEEN 1 AND 200);
ALTER TABLE payment_intents ADD CONSTRAINT ck_payment_intents_failure_code CHECK (failure_code IS NULL OR failure_code ~ '^[a-z0-9_-]{1,100}$');
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created', 'payment.authorization.started', 'payment.authorization.succeeded', 'payment.authorization.failed'));
CREATE UNIQUE INDEX uq_payment_intents_provider_authorization ON payment_intents (provider_authorization_id) WHERE provider_authorization_id IS NOT NULL;

COMMENT ON COLUMN payment_intents.provider_authorization_id IS 'Sanitized provider reference only; never store PAN, CVV, provider tokens, or response bodies.';
COMMENT ON COLUMN payment_intents.failure_code IS 'Bounded machine-readable failure category safe for API, audit, and operational use.';
