DROP TRIGGER tr_payment_audit_records_append_only ON payment_audit_records;
DROP FUNCTION prevent_payment_audit_mutation();
DROP TABLE payment_audit_records;
DROP INDEX ix_payment_intents_organization_id;
DO $$ BEGIN IF EXISTS (SELECT 1 FROM payment_intents GROUP BY restaurant_id,idempotency_key HAVING count(*)>1) THEN RAISE EXCEPTION 'Payment migration 2 downgrade requires organization/idempotency collisions to be reconciled first'; END IF; END $$;
ALTER TABLE payment_intents DROP CONSTRAINT uq_payment_intents_organization_restaurant_idempotency;
ALTER TABLE payment_intents DROP COLUMN organization_id;
ALTER TABLE payment_intents ADD CONSTRAINT uq_payment_intents_restaurant_idempotency UNIQUE (restaurant_id, idempotency_key);
