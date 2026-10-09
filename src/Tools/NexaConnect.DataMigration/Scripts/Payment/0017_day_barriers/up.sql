CREATE TABLE source_day_barriers (
 organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
 id uuid NOT NULL, operation_id uuid NOT NULL, preparation_operation_id uuid NOT NULL,
 from_utc timestamptz NOT NULL, to_utc timestamptz NOT NULL, fingerprint varchar(64) NOT NULL,
 command jsonb NOT NULL, phase text NOT NULL CHECK(phase IN ('armed','committed','aborted')),
 decision_id uuid, changed_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,id), UNIQUE(organization_id,operation_id), UNIQUE(organization_id,preparation_operation_id),
 FOREIGN KEY(organization_id,preparation_operation_id) REFERENCES source_day_fences(organization_id,operation_id),
 CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '27 hours'),
 CHECK((phase='armed')=(decision_id IS NULL)));
CREATE INDEX source_day_barriers_window ON source_day_barriers(restaurant_id,branch_id,from_utc,to_utc) WHERE phase IN('armed','committed');
CREATE TABLE source_day_barrier_audit (
 organization_id uuid NOT NULL, barrier_id uuid NOT NULL, phase text NOT NULL, decision_id uuid,
 command jsonb NOT NULL, occurred_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,barrier_id,phase),
 FOREIGN KEY(organization_id,barrier_id) REFERENCES source_day_barriers(organization_id,id));
CREATE TABLE source_late_work (
 organization_id uuid NOT NULL, event_type varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
 fingerprint varchar(64) NOT NULL, payload jsonb NOT NULL,
 received_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,event_type,event_id));
CREATE TABLE source_late_work_links (
 organization_id uuid NOT NULL,event_type varchar(128) NOT NULL,event_id varchar(128) NOT NULL,barrier_id uuid NOT NULL,
 PRIMARY KEY(organization_id,event_type,event_id,barrier_id),
 FOREIGN KEY(organization_id,event_type,event_id) REFERENCES source_late_work(organization_id,event_type,event_id),
 FOREIGN KEY(organization_id,barrier_id) REFERENCES source_day_barriers(organization_id,id));
CREATE INDEX source_late_work_by_barrier ON source_late_work_links(organization_id,barrier_id);
CREATE FUNCTION protect_day_barrier_history() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 RAISE EXCEPTION 'Settlement evidence is immutable'; END; $$;
CREATE TRIGGER source_barrier_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_day_barrier_audit FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();
CREATE TRIGGER source_late_work_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_late_work FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();
CREATE TRIGGER source_late_links_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_late_work_links FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();
CREATE FUNCTION protect_day_barrier_state() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Settlement barriers are retained'; END IF;
 IF TG_OP='UPDATE' AND (OLD.phase<>'armed' OR NEW.phase NOT IN ('committed','aborted') OR NEW.decision_id IS NULL
 OR to_jsonb(NEW)-ARRAY['phase','decision_id','changed_at_utc'] IS DISTINCT FROM to_jsonb(OLD)-ARRAY['phase','decision_id','changed_at_utc'])
 THEN RAISE EXCEPTION 'Settlement decision is irreversible'; END IF; RETURN NEW; END; $$;
CREATE TRIGGER source_barrier_state_guard BEFORE UPDATE OR DELETE ON source_day_barriers FOR EACH ROW EXECUTE FUNCTION protect_day_barrier_state();
CREATE TRIGGER source_barrier_no_truncate BEFORE TRUNCATE ON source_day_barriers FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_state();
CREATE TRIGGER source_barrier_generation AFTER INSERT OR UPDATE ON source_day_barriers FOR EACH ROW EXECUTE FUNCTION advance_source_fence_generation();
CREATE FUNCTION protect_pinned_source_fence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP='INSERT' THEN
 IF NOT NEW.cancelled AND EXISTS(SELECT 1 FROM source_day_barriers b WHERE b.restaurant_id=NEW.restaurant_id AND b.branch_id=NEW.branch_id
 AND b.phase IN('armed','committed') AND b.from_utc<NEW.to_utc AND b.to_utc>NEW.from_utc)
 THEN RAISE EXCEPTION 'financial_day_barrier' USING ERRCODE='PDS01'; END IF; RETURN NEW; END IF;
 IF EXISTS(SELECT 1 FROM source_day_barriers b WHERE b.organization_id=OLD.organization_id AND b.preparation_operation_id=OLD.operation_id AND b.phase IN('armed','committed'))
 THEN RAISE EXCEPTION 'financial_day_barrier' USING ERRCODE='PDS01'; END IF; RETURN NEW; END; $$;
CREATE TRIGGER source_fence_pinned BEFORE INSERT OR UPDATE ON source_day_fences FOR EACH ROW EXECUTE FUNCTION protect_pinned_source_fence();
CREATE OR REPLACE FUNCTION enforce_source_day_fence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM source_day_barriers b WHERE b.restaurant_id=NEW.restaurant_id AND b.branch_id=NEW.branch_id
 AND b.phase IN('armed','committed') AND source_change_blocks_fence(to_jsonb(NEW)->'attribution',b.from_utc,b.to_utc))
 THEN RAISE EXCEPTION 'financial_day_barrier' USING ERRCODE='PDS01'; END IF;
 IF EXISTS(SELECT 1 FROM source_day_fences f WHERE f.restaurant_id=NEW.restaurant_id AND f.branch_id=NEW.branch_id AND NOT f.cancelled
 AND f.expires_at_utc>clock_timestamp() AND source_change_blocks_fence(to_jsonb(NEW)->'attribution',f.from_utc,f.to_utc))
 THEN RAISE EXCEPTION 'financial_day_fenced'; END IF; RETURN NEW; END; $$;
