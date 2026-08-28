DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM orders WHERE status='payment_review')
    OR EXISTS (SELECT 1 FROM order_status_history WHERE to_status='payment_review') THEN
  RAISE EXCEPTION 'Cannot downgrade Order migration 3 while payment-review orders or history exist.';
 END IF;
 IF EXISTS (SELECT 1 FROM orders WHERE organization_id IS NOT NULL OR payment_intent_id IS NOT NULL) THEN
  RAISE EXCEPTION 'Cannot downgrade Order migration 3 after reconciliation ownership has been persisted.';
 END IF;
END $$;

ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status
    CHECK (status IN ('draft','submitted','accepted','payment_pending','preparing','ready','completed','cancelled'));

ALTER TABLE order_status_history DROP CONSTRAINT ck_order_status_history_to_status;
ALTER TABLE order_status_history ADD CONSTRAINT ck_order_status_history_to_status
    CHECK (to_status IN ('draft','submitted','accepted','payment_pending','preparing','ready','completed','cancelled'));

DROP INDEX ux_orders_organization_payment_intent;
ALTER TABLE orders DROP COLUMN payment_intent_id;
ALTER TABLE orders DROP COLUMN organization_id;
