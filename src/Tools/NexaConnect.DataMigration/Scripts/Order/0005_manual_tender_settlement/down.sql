DO $$ BEGIN IF EXISTS(SELECT 1 FROM order_manual_tender_settlements) THEN RAISE EXCEPTION 'Cannot downgrade Order migration 5 after manual tender settlements exist.'; END IF; END $$;
DROP TRIGGER trg_order_manual_tender_append_only ON order_manual_tender_settlements;
DROP FUNCTION reject_order_manual_tender_mutation();
DROP TABLE order_manual_tender_settlements;
