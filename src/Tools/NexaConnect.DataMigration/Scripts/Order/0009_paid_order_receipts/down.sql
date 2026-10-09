DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM orders WHERE receipt_snapshot IS NOT NULL) THEN
        RAISE EXCEPTION 'Receipt history exists; use forward recovery';
    END IF;
END $$;
DROP TRIGGER order_receipt_guard ON orders;
DROP FUNCTION protect_order_receipt();
ALTER TABLE orders DROP COLUMN receipt_snapshot;
