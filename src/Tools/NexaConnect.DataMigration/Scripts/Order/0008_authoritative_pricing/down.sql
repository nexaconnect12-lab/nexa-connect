DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM orders WHERE pricing_snapshot IS NOT NULL) THEN
        RAISE EXCEPTION 'Cannot remove accepted pricing history; use forward recovery';
    END IF;
END $$;
DROP TRIGGER order_pricing_immutable ON orders;
DROP FUNCTION protect_order_pricing();
DROP INDEX ux_orders_placement_key;
ALTER TABLE orders DROP CONSTRAINT ck_orders_pricing_snapshot,
    DROP COLUMN pricing_snapshot, DROP COLUMN pricing_fingerprint, DROP COLUMN placement_key;
