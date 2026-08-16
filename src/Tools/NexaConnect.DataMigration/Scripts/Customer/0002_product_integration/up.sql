CREATE TABLE customer_audit_records
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    customer_id uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    action text NOT NULL,
    actor_subject_id text NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_customer_audit_action CHECK (action IN ('customer.profile.created')),
    CONSTRAINT ck_customer_audit_actor CHECK
    (
        char_length(actor_subject_id) BETWEEN 1 AND 200
        AND actor_subject_id !~ '[[:cntrl:]]'
    )
);

CREATE INDEX ix_customer_audit_organization_occurred
    ON customer_audit_records (organization_id, occurred_at_utc DESC, id DESC);

CREATE FUNCTION prevent_customer_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'customer audit records are append-only';
END;
$$;

CREATE TRIGGER tr_customer_audit_append_only
BEFORE UPDATE OR DELETE ON customer_audit_records
FOR EACH ROW EXECUTE FUNCTION prevent_customer_audit_mutation();

COMMENT ON TABLE customer_audit_records IS
    'Append-only Customer audit excluding profile fields; actor subject is restricted accountability data. Operational logs are not an audit substitute.';
