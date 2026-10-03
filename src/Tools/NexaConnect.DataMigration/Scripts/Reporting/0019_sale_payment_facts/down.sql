DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM sale_fact_event_receipts) THEN
        RAISE EXCEPTION 'Sale financial projection history exists; use forward recovery';
    END IF;
END $$;
ALTER TABLE payment_facts DROP COLUMN payment_origin;
DROP TABLE sale_fact_event_receipts;
