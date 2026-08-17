ALTER TABLE notifications ADD COLUMN correlation_id text NULL;
ALTER TABLE notifications ADD COLUMN updated_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN provider_code text NULL;
ALTER TABLE notifications ADD COLUMN provider_message_id text NULL;
ALTER TABLE notifications ADD COLUMN delivery_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE notifications ADD COLUMN receipt_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE notifications ADD COLUMN next_delivery_attempt_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN next_receipt_attempt_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN delivery_lease_id uuid NULL;
ALTER TABLE notifications ADD COLUMN delivery_locked_until_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN provider_accepted_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN delivered_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN delivery_failed_at_utc timestamptz NULL;
ALTER TABLE notifications ADD COLUMN last_error_category text NULL;
ALTER TABLE notifications ADD COLUMN concurrency_version bigint NOT NULL DEFAULT 0;
UPDATE notifications SET correlation_id=id::text,updated_at_utc=created_at_utc;
UPDATE notifications SET status='queued' WHERE status IN('submitting','retry_scheduled');
UPDATE notifications SET status='provider_accepted' WHERE status='reconciling';
UPDATE notifications SET next_delivery_attempt_at_utc=created_at_utc WHERE organization_id IS NOT NULL AND status='queued';
UPDATE notifications SET next_receipt_attempt_at_utc=created_at_utc WHERE organization_id IS NOT NULL AND status='provider_accepted';
UPDATE notifications SET delivered_at_utc=created_at_utc WHERE status='delivered';
UPDATE notifications SET delivery_failed_at_utc=created_at_utc WHERE status='delivery_failed';
ALTER TABLE notifications ALTER COLUMN correlation_id SET NOT NULL;
ALTER TABLE notifications ALTER COLUMN updated_at_utc SET NOT NULL;
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_status CHECK(status IN('queued','submitting','provider_accepted','reconciling','retry_scheduled','delivered','delivery_failed'));
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_attempts CHECK(delivery_attempts>=0 AND receipt_attempts>=0);
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_text CHECK(char_length(correlation_id) BETWEEN 1 AND 128 AND (provider_code IS NULL OR char_length(provider_code) BETWEEN 1 AND 64) AND (provider_message_id IS NULL OR char_length(provider_message_id) BETWEEN 1 AND 200) AND (last_error_category IS NULL OR char_length(last_error_category) BETWEEN 1 AND 100));
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_lease CHECK((delivery_lease_id IS NULL)=(delivery_locked_until_utc IS NULL));
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_terminal CHECK((status<>'delivered' OR delivered_at_utc IS NOT NULL) AND (status<>'delivery_failed' OR delivery_failed_at_utc IS NOT NULL));
ALTER TABLE notifications ADD CONSTRAINT ck_notifications_delivery_schedule CHECK(organization_id IS NULL OR (status IN('queued','submitting','retry_scheduled') AND next_delivery_attempt_at_utc IS NOT NULL) OR (status IN('provider_accepted','reconciling') AND next_receipt_attempt_at_utc IS NOT NULL) OR (status IN('delivered','delivery_failed') AND next_delivery_attempt_at_utc IS NULL AND next_receipt_attempt_at_utc IS NULL));
ALTER TABLE notifications ADD CONSTRAINT uq_notifications_id_organization UNIQUE(id,organization_id);
CREATE INDEX ix_notifications_delivery_due ON notifications(COALESCE(next_delivery_attempt_at_utc,next_receipt_attempt_at_utc),created_at_utc,id) WHERE status IN('queued','submitting','provider_accepted','reconciling','retry_scheduled');
CREATE UNIQUE INDEX uq_notifications_provider_message ON notifications(provider_code,provider_message_id) WHERE provider_message_id IS NOT NULL;

CREATE TABLE notification_delivery_attempts
(
    id uuid PRIMARY KEY,
    notification_id uuid NOT NULL,
    organization_id uuid NOT NULL,
    operation text NOT NULL CHECK(operation IN('submit','reconcile')),
    attempt_number integer NOT NULL CHECK(attempt_number>0),
    provider_code text NOT NULL,
    outcome text NOT NULL CHECK(outcome IN('accepted','delivered','pending','transientfailure','permanentfailure')),
    error_category text NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_notification_delivery_attempts_notification FOREIGN KEY(notification_id,organization_id) REFERENCES notifications(id,organization_id),
    CONSTRAINT ck_notification_delivery_attempts_text CHECK(char_length(provider_code) BETWEEN 1 AND 64 AND (error_category IS NULL OR char_length(error_category) BETWEEN 1 AND 100))
);
CREATE INDEX ix_notification_delivery_attempts_notification ON notification_delivery_attempts(notification_id,occurred_at_utc,id);
CREATE FUNCTION prevent_notification_delivery_attempt_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'notification_delivery_attempts is append-only';
END;
$$;
CREATE TRIGGER tr_notification_delivery_attempts_append_only BEFORE UPDATE OR DELETE ON notification_delivery_attempts
FOR EACH ROW EXECUTE FUNCTION prevent_notification_delivery_attempt_mutation();

COMMENT ON COLUMN notifications.recipient IS 'Delivery address; restricted personal data that must not be logged or emitted.';
COMMENT ON COLUMN notifications.body IS 'Notification content; restricted data that must not be logged or emitted.';
COMMENT ON COLUMN notifications.correlation_id IS 'Validated request correlation identifier propagated to safe lifecycle events.';
COMMENT ON COLUMN notifications.provider_message_id IS 'Opaque provider receipt identifier; not notification content.';
