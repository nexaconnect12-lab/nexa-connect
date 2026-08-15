DROP TABLE outbox_messages;
DROP TABLE inbox_messages;
DROP TABLE notification_audit_records;
DROP FUNCTION prevent_notification_audit_mutation();
DROP INDEX ix_notifications_organization_created;
DROP INDEX uq_notifications_source_event;
ALTER TABLE notifications DROP COLUMN source_event_id;
ALTER TABLE notifications DROP COLUMN organization_id;
