CREATE TABLE catalog_audit_records
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    action text NOT NULL,
    actor_subject_id text NOT NULL,
    occurred_at_utc timestamptz NOT NULL
);

CREATE FUNCTION prevent_catalog_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'catalog_audit_records is append-only';
END;
$$;
CREATE TRIGGER tr_catalog_audit_records_append_only
BEFORE UPDATE OR DELETE ON catalog_audit_records
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_audit_mutation();

CREATE TABLE outbox_messages
(
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    contract_version integer NOT NULL CHECK (contract_version > 0),
    aggregate_type text NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    correlation_id text NULL,
    causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL,
    published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0 CHECK (retry_count >= 0),
    next_attempt_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT ck_catalog_outbox_published CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);
CREATE INDEX ix_catalog_outbox_unpublished ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id) WHERE published_at_utc IS NULL;
