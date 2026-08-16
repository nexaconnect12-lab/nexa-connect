DELETE FROM inbox_messages WHERE consumer_name='reporting.activity.v1' AND message_id IN(SELECT event_id FROM activity_records WHERE action LIKE 'customer.profile.%' OR resource_type='customer-profile');
DELETE FROM activity_records WHERE action LIKE 'customer.profile.%' OR resource_type='customer-profile';
ALTER TABLE activity_records DROP CONSTRAINT ck_activity_records_action;
ALTER TABLE activity_records ADD CONSTRAINT ck_activity_records_action CHECK(action IN('customer-membership.changed','branch.created','branch.updated','branch.configuration.updated','catalog.menu-item.changed','media.asset.created','media.asset.quarantined','media.asset.deleted','media.asset.upload-expired','notification.queued','payment.intent.created','kitchen.ticket.queued','kitchen.ticket.started','kitchen.ticket.ready','kitchen.ticket.completed','kitchen.ticket.cancelled'));
ALTER TABLE activity_records DROP CONSTRAINT ck_activity_records_resource;
ALTER TABLE activity_records ADD CONSTRAINT ck_activity_records_resource CHECK(resource_type IN('organization-membership','branch','branch-configuration','catalog-menu-item','media-asset','notification','payment-intent','kitchen-ticket'));
