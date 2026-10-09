CREATE TABLE source_day_cutoffs (
 organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
 from_utc timestamptz NOT NULL, to_utc timestamptz NOT NULL,
 id uuid NOT NULL, operation_id uuid NOT NULL, generation bigint NOT NULL CHECK(generation>0),
 fingerprint varchar(64) NOT NULL, actor varchar(128) NOT NULL, payload jsonb NOT NULL,
 PRIMARY KEY(organization_id,id), UNIQUE(organization_id,operation_id),
 UNIQUE(organization_id,restaurant_id,branch_id,from_utc,to_utc,generation),
 CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '27 hours'),
 CHECK((payload->>'manifestId')::uuid=id AND (payload->>'operationId')::uuid=operation_id
   AND (payload->>'generation')::bigint=generation)
);
CREATE FUNCTION protect_source_day_cutoffs() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Source day-cutoff history is immutable'; END; $$;
CREATE TRIGGER source_day_cutoffs_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_day_cutoffs
FOR EACH STATEMENT EXECUTE FUNCTION protect_source_day_cutoffs();

CREATE TABLE branch_day_cutoffs (
    organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
    business_date date NOT NULL, version bigint NOT NULL CHECK(version >= 0), state jsonb NOT NULL,
    PRIMARY KEY(organization_id, branch_id, business_date),
    CHECK((state->>'version')::bigint=version),
    CHECK(state->>'status' IN ('preparing','blocked','ready_for_review'))
);
CREATE TABLE branch_day_cutoff_operations (
    organization_id uuid NOT NULL, operation_id uuid NOT NULL, branch_id uuid NOT NULL, business_date date NOT NULL,
    fingerprint varchar(64) NOT NULL, status text NOT NULL CHECK(status IN('pending','completed','abandoned')),
    result_version bigint, PRIMARY KEY(organization_id,operation_id),
    FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_cutoffs(organization_id,branch_id,business_date)
);
CREATE TABLE branch_day_cutoff_audit (
    organization_id uuid NOT NULL, branch_id uuid NOT NULL, business_date date NOT NULL, version bigint NOT NULL,
    action text NOT NULL CHECK(action IN('prepare','complete','invalidate')), operation_id uuid,
    subject_id text NOT NULL, authorization_decision_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
    state jsonb NOT NULL, PRIMARY KEY(organization_id,branch_id,business_date,version),
    FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_cutoffs(organization_id,branch_id,business_date)
);
CREATE FUNCTION protect_branch_day_cutoff_audit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Branch day-close audit is immutable'; END; $$;
CREATE TRIGGER branch_day_cutoff_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_cutoff_audit
FOR EACH STATEMENT EXECUTE FUNCTION protect_branch_day_cutoff_audit();
