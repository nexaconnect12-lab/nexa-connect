DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM order_cancellations) THEN
        RAISE EXCEPTION 'Order cancellation history exists; use forward recovery';
    END IF;
END $$;
DROP TABLE order_cancellations;
ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN
('draft','submitted','accepted','inventory_reserved','kitchen_accepted','payment_pending','payment_review','preparing','ready','completed','cancelled'));
