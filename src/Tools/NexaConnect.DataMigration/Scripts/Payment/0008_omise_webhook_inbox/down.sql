DO $$ BEGIN IF EXISTS(SELECT 1 FROM omise_webhook_inbox WHERE status IN('pending','processing','exhausted')) THEN RAISE EXCEPTION 'Cannot remove unresolved Omise webhook evidence.'; END IF; END $$;
DROP TABLE omise_webhook_inbox;
