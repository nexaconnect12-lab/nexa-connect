DO $$ BEGIN IF EXISTS(SELECT 1 FROM drawer_intents) THEN RAISE EXCEPTION 'Retained drawer intents prevent downgrade'; END IF; END $$;
DROP TABLE drawer_intent_audit,drawer_intent_outcomes,drawer_intents;
DROP FUNCTION reject_drawer_intent_mutation();
