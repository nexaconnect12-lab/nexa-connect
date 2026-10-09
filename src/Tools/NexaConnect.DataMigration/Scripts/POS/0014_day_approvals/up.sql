CREATE TABLE branch_day_approvals (
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,business_date date NOT NULL,
 version bigint NOT NULL CHECK(version>=0),state jsonb NOT NULL,
 PRIMARY KEY(organization_id,branch_id,business_date),
 FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_seals(organization_id,branch_id,business_date),
 CHECK((state->>'version')::bigint=version),CHECK(state->>'status' IN('not_approved','approved','unverified','superseded'))
);
CREATE TABLE branch_day_approval_decisions (
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,business_date date NOT NULL,
 id uuid NOT NULL,operation_id uuid NOT NULL,approval_version bigint NOT NULL CHECK(approval_version>0),seal_version bigint NOT NULL CHECK(seal_version>0),
 fingerprint varchar(64) NOT NULL,payload jsonb NOT NULL,
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,operation_id),UNIQUE(organization_id,branch_id,business_date,approval_version),
 FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_approvals(organization_id,branch_id,business_date),
 CHECK((payload->>'approvalVersion')::bigint=approval_version),CHECK((payload->>'sealVersion')::bigint=seal_version)
);
CREATE TABLE branch_day_approval_audit (
 organization_id uuid NOT NULL,branch_id uuid NOT NULL,business_date date NOT NULL,version bigint NOT NULL,
 action text NOT NULL CHECK(action IN('approve','supersede','validate')),subject_id varchar(128) NOT NULL,
 authorization_decision_id uuid NOT NULL,occurred_at_utc timestamptz NOT NULL,state jsonb NOT NULL,
 PRIMARY KEY(organization_id,branch_id,business_date,version),
 FOREIGN KEY(organization_id,branch_id,business_date) REFERENCES branch_day_approvals(organization_id,branch_id,business_date)
);
CREATE FUNCTION protect_day_approval_history() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Day-close approval history is immutable'; END; $$;
CREATE TRIGGER day_approval_decisions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_approval_decisions
 FOR EACH STATEMENT EXECUTE FUNCTION protect_day_approval_history();
CREATE TRIGGER day_approval_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON branch_day_approval_audit
 FOR EACH STATEMENT EXECUTE FUNCTION protect_day_approval_history();
