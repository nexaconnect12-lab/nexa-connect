DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM settlement_facts) OR EXISTS(SELECT 1 FROM settlement_event_receipts)
    OR EXISTS(SELECT 1 FROM projection_checkpoints WHERE projector_name='pos-day-settlement') THEN
  RAISE EXCEPTION 'Settlement projection history prevents downgrade';
 END IF;
END $$;
DROP TABLE settlement_facts;
DROP TABLE settlement_event_receipts;
DROP FUNCTION settlement_projection_immutable();
