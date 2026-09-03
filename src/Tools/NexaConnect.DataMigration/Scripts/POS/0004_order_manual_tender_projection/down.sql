DO $$ BEGIN IF EXISTS(SELECT 1 FROM pos_order_settlements) THEN RAISE EXCEPTION 'Cannot downgrade POS migration 4 after Order settlements exist.'; END IF; END $$;
DROP TRIGGER trg_pos_order_settlement_append_only ON pos_order_settlements;
DROP FUNCTION reject_pos_order_settlement_mutation();
DROP INDEX uq_cash_movements_manual_order;
DROP TABLE pos_order_settlements;
