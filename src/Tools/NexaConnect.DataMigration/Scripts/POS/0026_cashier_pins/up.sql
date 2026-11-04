CREATE TABLE cashier_pin_credentials (
    organization_id uuid NOT NULL, issuer text NOT NULL, subject text NOT NULL,
    state text NOT NULL CHECK(state IN ('unset','active','revoked')),
    version bigint NOT NULL CHECK(version >= 0), protected_verifier text,
    failed_attempts integer NOT NULL CHECK(failed_attempts BETWEEN 0 AND 5), locked_until_utc timestamptz,
    PRIMARY KEY(organization_id,issuer,subject),
    CHECK ((state='active') = (protected_verifier IS NOT NULL))
);
CREATE TABLE cashier_pin_audit (
    organization_id uuid NOT NULL, issuer text NOT NULL, subject text NOT NULL, operation_id uuid NOT NULL,
    actor text NOT NULL, action text NOT NULL CHECK(action IN ('change','verify','reset','revoke')),
    decision_id uuid NOT NULL, branch_id uuid NOT NULL, store_id uuid NOT NULL, terminal_id uuid NOT NULL,
    expected_version bigint NOT NULL, result_version bigint NOT NULL, accepted boolean NOT NULL,
    observed_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(organization_id,issuer,subject,operation_id)
);
CREATE FUNCTION cashier_pin_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Cashier PIN audit is immutable'; END $$;
CREATE TRIGGER cashier_pin_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cashier_pin_audit
FOR EACH STATEMENT EXECUTE FUNCTION cashier_pin_audit_immutable();
