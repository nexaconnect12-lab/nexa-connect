ALTER TABLE notifications ADD COLUMN organization_id uuid NULL;
ALTER TABLE notifications ADD COLUMN source_event_id uuid NULL;
CREATE UNIQUE INDEX uq_notifications_source_event ON notifications (source_event_id) WHERE source_event_id IS NOT NULL;
CREATE INDEX ix_notifications_organization_created ON notifications (organization_id, created_at_utc DESC, id);

CREATE TABLE notification_audit_records
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    notification_id uuid NOT NULL REFERENCES notifications(id),
    action text NOT NULL,
    actor_subject_id text NOT NULL,
    occurred_at_utc timestamptz NOT NULL
);

CREATE FUNCTION prevent_notification_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'notification_audit_records is append-only';
END;
$$;
CREATE TRIGGER tr_notification_audit_records_append_only
BEFORE UPDATE OR DELETE ON notification_audit_records
FOR EACH ROW EXECUTE FUNCTION prevent_notification_audit_mutation();

CREATE TABLE inbox_messages
(
    message_id uuid NOT NULL,
    consumer_name text NOT NULL,
    status text NOT NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    locked_until_utc timestamptz NULL,
    processed_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id, consumer_name),
    CONSTRAINT ck_inbox_messages_status CHECK (status IN ('queued','processing','completed'))
);

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
    CONSTRAINT ck_outbox_messages_published CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);
CREATE INDEX ix_outbox_messages_unpublished ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id) WHERE published_at_utc IS NULL;

COMMENT ON COLUMN notifications.organization_id IS 'External Platform Directory identifier; no cross-database foreign key.';
COMMENT ON COLUMN notifications.source_event_id IS 'Optional immutable integration-event identifier used for idempotent creation.';
