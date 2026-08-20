DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM activity_records WHERE action IN ('payment.authorization.uncertain','payment.authorization.reconciled')) THEN
        RAISE EXCEPTION 'Reporting migration 9 downgrade requires uncertain payment activity to be replayed or retained by a compatible consumer';
    END IF;
END $$;
ALTER TABLE activity_records DROP CONSTRAINT ck_activity_records_action;
ALTER TABLE activity_records ADD CONSTRAINT ck_activity_records_action CHECK(action IN('customer-membership.changed','branch.created','branch.updated','branch.configuration.updated','catalog.menu-item.changed','media.asset.created','media.asset.quarantined','media.asset.deleted','media.asset.upload-expired','notification.queued','notification.delivery.accepted','notification.delivered','notification.delivery.failed','payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','kitchen.ticket.queued','kitchen.ticket.started','kitchen.ticket.ready','kitchen.ticket.completed','kitchen.ticket.cancelled','customer.profile.created'));
