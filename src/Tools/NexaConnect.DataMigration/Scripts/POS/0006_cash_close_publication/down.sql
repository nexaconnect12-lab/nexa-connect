DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM cash_close_publications) OR EXISTS(SELECT 1 FROM outbox_messages WHERE event_type='pos.cash-close.snapshot.v1') THEN
        RAISE EXCEPTION 'Cash-close publication history exists; preserve snapshot versions and use forward recovery.';
    END IF;
END $$;
DROP INDEX ix_cash_sessions_closed_publication;
DROP TABLE cash_close_publications;
