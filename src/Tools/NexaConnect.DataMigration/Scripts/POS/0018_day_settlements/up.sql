CREATE TABLE branch_day_settlements (
 organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL, business_date date NOT NULL,
 id uuid NOT NULL, state jsonb NOT NULL,last_attempt_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id),
 FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_finalization_preparations(organization_id,branch_id,business_date),
 CHECK(state->>'status' IN('arming','committing','finalized','aborting','aborted')));
CREATE UNIQUE INDEX branch_day_active_settlement ON branch_day_settlements(organization_id,branch_id,business_date) WHERE state->>'status'<>'aborted';
CREATE UNIQUE INDEX branch_day_settlement_operation_lookup ON branch_day_settlements(organization_id,(state->'command'->>'operationId'));
CREATE INDEX branch_day_settlement_recovery_queue ON branch_day_settlements(last_attempt_at_utc,id) WHERE state->>'status' NOT IN('finalized','aborted');
CREATE TABLE branch_day_settlement_operations (
 organization_id uuid NOT NULL,operation_id uuid NOT NULL,fingerprint varchar(64) NOT NULL,command jsonb NOT NULL,
 PRIMARY KEY(organization_id,operation_id));
CREATE TABLE branch_day_settlement_decisions (
 organization_id uuid NOT NULL,settlement_id uuid NOT NULL,decision_id uuid NOT NULL,decision text NOT NULL CHECK(decision IN('commit','abort')),
 PRIMARY KEY(organization_id,settlement_id), UNIQUE(organization_id,decision_id),
 FOREIGN KEY(organization_id,settlement_id) REFERENCES branch_day_settlements(organization_id,id));
CREATE TABLE branch_day_settlement_receipts (
 organization_id uuid NOT NULL,settlement_id uuid NOT NULL,receipt jsonb NOT NULL,
 PRIMARY KEY(organization_id,settlement_id),
 FOREIGN KEY(organization_id,settlement_id) REFERENCES branch_day_settlement_decisions(organization_id,settlement_id));
CREATE TABLE branch_day_settlement_audit (
 organization_id uuid NOT NULL,settlement_id uuid NOT NULL,action text NOT NULL CHECK(action IN('intent','commit','abort','acknowledged')),
 state jsonb NOT NULL,occurred_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,settlement_id,action), FOREIGN KEY(organization_id,settlement_id) REFERENCES branch_day_settlements(organization_id,id));
CREATE FUNCTION protect_settlement_history() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Settlement history is immutable'; END; $$;
CREATE TRIGGER settlement_operations_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_settlement_operations FOR EACH STATEMENT EXECUTE FUNCTION protect_settlement_history();
CREATE TRIGGER settlement_decisions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_settlement_decisions FOR EACH STATEMENT EXECUTE FUNCTION protect_settlement_history();
CREATE TRIGGER settlement_receipts_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_settlement_receipts FOR EACH STATEMENT EXECUTE FUNCTION protect_settlement_history();
CREATE TRIGGER settlement_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_settlement_audit FOR EACH STATEMENT EXECUTE FUNCTION protect_settlement_history();
CREATE FUNCTION protect_settlement_projection() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Settlement intent is retained'; END IF;
 IF NEW.organization_id<>OLD.organization_id OR NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NEW.business_date<>OLD.business_date OR NEW.id<>OLD.id
 OR NEW.state-ARRAY['status','decisionId','receipt','sources'] IS DISTINCT FROM OLD.state-ARRAY['status','decisionId','receipt','sources']
 OR OLD.state->>'decisionId' IS NOT NULL AND (NEW.state->'decisionId' IS DISTINCT FROM OLD.state->'decisionId' OR NEW.state->'receipt' IS DISTINCT FROM OLD.state->'receipt')
 OR OLD.state->>'status' IN('finalized','aborted') AND NEW.state->>'status'<>OLD.state->>'status'
 THEN RAISE EXCEPTION 'Settlement binding is immutable'; END IF; RETURN NEW; END; $$;
CREATE TRIGGER settlement_projection_guard BEFORE UPDATE OR DELETE ON branch_day_settlements FOR EACH ROW EXECUTE FUNCTION protect_settlement_projection();
CREATE TRIGGER settlement_projection_no_truncate BEFORE TRUNCATE ON branch_day_settlements FOR EACH STATEMENT EXECUTE FUNCTION protect_settlement_projection();
CREATE FUNCTION protect_settlement_preparation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM branch_day_settlements s WHERE s.organization_id=OLD.organization_id AND s.branch_id=OLD.branch_id AND s.business_date=OLD.business_date AND s.state->>'status'<>'aborted')
 THEN RAISE EXCEPTION 'Settlement owns this preparation' USING ERRCODE='PDS02'; END IF; RETURN NEW; END; $$;
CREATE TRIGGER preparation_settlement_pinned BEFORE UPDATE OR DELETE ON branch_day_finalization_preparations FOR EACH ROW EXECUTE FUNCTION protect_settlement_preparation();
