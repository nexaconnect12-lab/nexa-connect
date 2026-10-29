CREATE TABLE drawer_intents(
 organization_id uuid NOT NULL,operation_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,
 store_id uuid NOT NULL REFERENCES stores(id),terminal_id uuid NOT NULL REFERENCES terminals(id),
 shift_id uuid NOT NULL REFERENCES shifts(id),cash_session_id uuid NOT NULL REFERENCES cash_sessions(id),
 actor_subject_id text NOT NULL CHECK(length(btrim(actor_subject_id)) BETWEEN 1 AND 200),
 cause text NOT NULL CHECK(cause IN('manual','cash_sale')),order_id uuid NULL,reason text NULL,
 authorization_decision_id uuid NOT NULL,reserved_at_utc timestamptz NOT NULL,expires_at_utc timestamptz NOT NULL,
 PRIMARY KEY(organization_id,operation_id),
 CHECK(expires_at_utc=reserved_at_utc+interval '60 seconds'),
 CHECK((cause='manual' AND order_id IS NULL AND reason IN('change_required','cash_check','supervisor_access'))
    OR(cause='cash_sale' AND order_id IS NOT NULL AND reason IS NULL)),
 CHECK(cause<>'manual' OR reason IS NOT NULL));
CREATE UNIQUE INDEX uq_drawer_intents_original_sale ON drawer_intents(organization_id,order_id) WHERE cause='cash_sale';
CREATE TABLE drawer_intent_outcomes(
 organization_id uuid NOT NULL,operation_id uuid NOT NULL,
 outcome text NOT NULL CHECK(outcome IN('submitted','unknown','cancelled')),reported_at_utc timestamptz NOT NULL,authorization_decision_id uuid NOT NULL,
 PRIMARY KEY(organization_id,operation_id),
 FOREIGN KEY(organization_id,operation_id) REFERENCES drawer_intents(organization_id,operation_id));
CREATE TABLE drawer_intent_audit(
 id uuid PRIMARY KEY,organization_id uuid NOT NULL,operation_id uuid NOT NULL,
 stage text NOT NULL CHECK(stage IN('reserved','submitted','unknown','cancelled')),
 actor_subject_id text NOT NULL,authorization_decision_id uuid NOT NULL,occurred_at_utc timestamptz NOT NULL,
 FOREIGN KEY(organization_id,operation_id) REFERENCES drawer_intents(organization_id,operation_id),
 UNIQUE(organization_id,operation_id,stage));
CREATE FUNCTION reject_drawer_intent_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'drawer intent history is append-only' USING ERRCODE='23514'; END $$;
CREATE TRIGGER drawer_intents_immutable BEFORE UPDATE OR DELETE ON drawer_intents FOR EACH ROW EXECUTE FUNCTION reject_drawer_intent_mutation();
CREATE TRIGGER drawer_outcomes_immutable BEFORE UPDATE OR DELETE ON drawer_intent_outcomes FOR EACH ROW EXECUTE FUNCTION reject_drawer_intent_mutation();
CREATE TRIGGER drawer_audit_immutable BEFORE UPDATE OR DELETE ON drawer_intent_audit FOR EACH ROW EXECUTE FUNCTION reject_drawer_intent_mutation();
COMMENT ON TABLE drawer_intents IS 'Consumed reservation, not payment authority or proof of physical drawer opening. Never reset to retry hardware.';
COMMENT ON TABLE drawer_intent_outcomes IS 'Unverified client-reported submission outcome; absent outcome remains consumed/uncertain.';
