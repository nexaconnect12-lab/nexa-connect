CREATE TABLE source_day_seals (
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,
 from_utc timestamptz NOT NULL,to_utc timestamptz NOT NULL,id uuid NOT NULL,operation_id uuid NOT NULL,
 manifest_id uuid NOT NULL,fingerprint varchar(64) NOT NULL,actor varchar(128) NOT NULL,payload jsonb NOT NULL,
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,operation_id),
 FOREIGN KEY(organization_id,manifest_id) REFERENCES source_day_cutoffs(organization_id,id),
 CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '27 hours'),
 CHECK((payload->>'sealId')::uuid=id AND (payload->>'manifestId')::uuid=manifest_id)
);
CREATE FUNCTION protect_source_day_seals() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Source day-seal evidence is immutable'; END; $$;
CREATE TRIGGER source_day_seals_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_day_seals
 FOR EACH STATEMENT EXECUTE FUNCTION protect_source_day_seals();
CREATE TABLE source_financial_changes (
 restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,epoch uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
 recorded_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(restaurant_id,branch_id,epoch,revision)
);
CREATE FUNCTION protect_source_financial_changes() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP<>'INSERT' OR pg_trigger_depth()<2 THEN RAISE EXCEPTION 'Source financial changes are append-only and source-managed'; END IF;
 RETURN NEW;
END; $$;
CREATE TRIGGER source_financial_changes_guard BEFORE INSERT OR UPDATE OR DELETE ON source_financial_changes
 FOR EACH ROW EXECUTE FUNCTION protect_source_financial_changes();
CREATE TRIGGER source_financial_changes_no_truncate BEFORE TRUNCATE ON source_financial_changes
 FOR EACH STATEMENT EXECUTE FUNCTION protect_source_financial_changes();
CREATE FUNCTION journal_source_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO source_financial_changes(restaurant_id,branch_id,epoch,revision)
 SELECT NEW.restaurant_id,NEW.branch_id,epoch,NEW.revision FROM source_financial_epoch WHERE singleton;
 RETURN NEW;
END; $$;
CREATE TRIGGER source_financial_revision_journal AFTER INSERT OR UPDATE ON source_financial_revisions
 FOR EACH ROW EXECUTE FUNCTION journal_source_financial_revision();
COMMENT ON TABLE source_financial_changes IS 'Each committed branch revision affects all sealed windows conservatively. No financial values, entity IDs or personal data are copied.';
CREATE OR REPLACE FUNCTION protect_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='source_financial_epoch' OR TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth()<2 THEN
  RAISE EXCEPTION 'Financial revision evidence is source-managed';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NEW.revision<>OLD.revision+1) THEN
  RAISE EXCEPTION 'Financial revisions must advance monotonically';
 END IF;
 IF TG_OP='INSERT' AND NEW.revision<>1 THEN RAISE EXCEPTION 'Invalid initial financial revision'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('financial-revision:'||NEW.restaurant_id::text||':'||NEW.branch_id::text,0));
 RETURN NEW;
END; $$;
CREATE TABLE branch_day_seals (
    organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
    business_date date NOT NULL, version bigint NOT NULL CHECK(version >= 0), state jsonb NOT NULL,
    PRIMARY KEY(organization_id, branch_id, business_date),
    CHECK((state->>'version')::bigint=version),
    CHECK(state->>'status' IN ('preparing','blocked','ready_for_review'))
);
CREATE TABLE branch_day_seal_operations (
    organization_id uuid NOT NULL, operation_id uuid NOT NULL, branch_id uuid NOT NULL, business_date date NOT NULL,
    fingerprint varchar(64) NOT NULL, status text NOT NULL CHECK(status IN('pending','completed','abandoned')),
    result_version bigint, PRIMARY KEY(organization_id,operation_id),
    FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_seals(organization_id,branch_id,business_date)
);
CREATE TABLE branch_day_seal_audit (
    organization_id uuid NOT NULL, branch_id uuid NOT NULL, business_date date NOT NULL, version bigint NOT NULL,
    action text NOT NULL CHECK(action IN('prepare','complete','invalidate')), operation_id uuid,
    subject_id text NOT NULL, authorization_decision_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
    state jsonb NOT NULL, PRIMARY KEY(organization_id,branch_id,business_date,version),
    FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_seals(organization_id,branch_id,business_date)
);
CREATE FUNCTION protect_branch_day_seal_audit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Branch day-close audit is immutable'; END; $$;
CREATE TRIGGER branch_day_seal_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_seal_audit
FOR EACH STATEMENT EXECUTE FUNCTION protect_branch_day_seal_audit();
