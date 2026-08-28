ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status
    CHECK (status IN ('draft','submitted','accepted','payment_pending','payment_review','preparing','ready','completed','cancelled'));

ALTER TABLE order_status_history DROP CONSTRAINT ck_order_status_history_to_status;
ALTER TABLE order_status_history ADD CONSTRAINT ck_order_status_history_to_status
    CHECK (to_status IN ('draft','submitted','accepted','payment_pending','payment_review','preparing','ready','completed','cancelled'));

COMMENT ON COLUMN orders.status IS 'payment_review retains Inventory/Kitchen work until an operator resolves a definitive void failure or exhausted uncertainty.';

ALTER TABLE orders ADD COLUMN organization_id uuid NULL;
ALTER TABLE orders ADD COLUMN payment_intent_id uuid NULL;
CREATE UNIQUE INDEX ux_orders_organization_payment_intent
    ON orders (organization_id, payment_intent_id)
    WHERE payment_intent_id IS NOT NULL;
COMMENT ON COLUMN orders.organization_id IS 'Owning Platform Directory organization. Legacy rows must be backfilled before application access.';
COMMENT ON COLUMN orders.payment_intent_id IS 'Payment intent bound to this order once authorization starts; reconciliation events must match it.';
