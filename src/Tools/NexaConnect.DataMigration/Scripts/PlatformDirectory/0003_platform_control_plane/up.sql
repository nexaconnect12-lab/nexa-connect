CREATE TABLE platform_audit_records
(
    id uuid NOT NULL,
    action text NOT NULL,
    resource_type text NOT NULL,
    resource_id text NOT NULL,
    actor_subject_id text NOT NULL,
    outcome text NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_platform_audit_records PRIMARY KEY (id),
    CONSTRAINT ck_platform_audit_records_action CHECK (char_length(btrim(action)) > 0),
    CONSTRAINT ck_platform_audit_records_resource_type CHECK (char_length(btrim(resource_type)) > 0),
    CONSTRAINT ck_platform_audit_records_resource_id CHECK (char_length(btrim(resource_id)) > 0),
    CONSTRAINT ck_platform_audit_records_actor CHECK (char_length(btrim(actor_subject_id)) > 0),
    CONSTRAINT ck_platform_audit_records_outcome CHECK (outcome IN ('succeeded', 'failed'))
);

CREATE INDEX ix_platform_audit_records_occurred ON platform_audit_records (occurred_at_utc DESC, id DESC);
CREATE INDEX ix_platform_audit_records_actor ON platform_audit_records (actor_subject_id, occurred_at_utc DESC, id DESC);

CREATE FUNCTION prevent_platform_audit_record_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'platform_audit_records is append-only';
END;
$$;

CREATE TRIGGER tr_platform_audit_records_append_only BEFORE UPDATE OR DELETE ON platform_audit_records
FOR EACH ROW EXECUTE FUNCTION prevent_platform_audit_record_mutation();

COMMENT ON TABLE platform_audit_records IS 'Append-only audit history for platform control-plane administration.';
