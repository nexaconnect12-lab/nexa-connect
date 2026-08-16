ALTER TABLE payment_intents ADD COLUMN organization_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE payment_intents ALTER COLUMN organization_id DROP DEFAULT;
ALTER TABLE payment_intents DROP CONSTRAINT uq_payment_intents_restaurant_idempotency;
ALTER TABLE payment_intents ADD CONSTRAINT uq_payment_intents_organization_restaurant_idempotency UNIQUE (organization_id, restaurant_id, idempotency_key);
CREATE INDEX ix_payment_intents_organization_id ON payment_intents (organization_id, id);

CREATE TABLE payment_audit_records
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    payment_intent_id uuid NOT NULL,
    action text NOT NULL,
    actor_subject_id text NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_payment_audit_records_payment_intent FOREIGN KEY (payment_intent_id) REFERENCES payment_intents (id) ON DELETE RESTRICT,
    CONSTRAINT ck_payment_audit_records_action CHECK (action IN ('payment.intent.created')),
    CONSTRAINT ck_payment_audit_records_actor CHECK (char_length(btrim(actor_subject_id)) BETWEEN 1 AND 200 AND actor_subject_id !~ '[[:cntrl:]]')
);

CREATE INDEX ix_payment_audit_records_organization_time ON payment_audit_records (organization_id, occurred_at_utc DESC, id DESC);
CREATE FUNCTION prevent_payment_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'payment_audit_records is append-only'; END; $$;
CREATE TRIGGER tr_payment_audit_records_append_only BEFORE UPDATE OR DELETE ON payment_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_payment_audit_mutation();

COMMENT ON COLUMN payment_intents.organization_id IS 'External Platform Directory identifier required in tenant-scoped queries.';
COMMENT ON TABLE payment_audit_records IS 'Append-only Payment business audit; operational logs are not a substitute.';
