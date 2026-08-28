DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM order_payment_reviews) OR EXISTS(SELECT 1 FROM order_payment_review_history) THEN
  RAISE EXCEPTION 'Cannot downgrade Order migration 4 after payment-review cases or history exist.';
 END IF;
END $$;
DROP TRIGGER trg_order_payment_review_history_append_only ON order_payment_review_history;
DROP FUNCTION reject_order_payment_review_history_mutation();
DROP TABLE order_payment_review_history;
DROP TABLE order_payment_reviews;
