DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM orders WHERE status = 'payment_pending')
       OR EXISTS (SELECT 1 FROM order_status_history WHERE to_status = 'payment_pending') THEN
        RAISE EXCEPTION 'Cannot downgrade Order migration 2 while payment-pending orders or history exist.';
    END IF;
END $$;

DROP TABLE IF EXISTS inbox_messages;

ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status
    CHECK (status IN ('draft', 'submitted', 'accepted', 'preparing', 'ready', 'completed', 'cancelled'));

ALTER TABLE order_status_history DROP CONSTRAINT ck_order_status_history_to_status;
ALTER TABLE order_status_history ADD CONSTRAINT ck_order_status_history_to_status
    CHECK (to_status IN ('draft', 'submitted', 'accepted', 'preparing', 'ready', 'completed', 'cancelled'));
