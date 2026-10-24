DO $$ BEGIN IF EXISTS(SELECT 1 FROM branch_day_settlements) THEN RAISE EXCEPTION 'Settlement history requires forward recovery'; END IF; END; $$;
DROP TRIGGER preparation_settlement_pinned ON branch_day_finalization_preparations; DROP FUNCTION protect_settlement_preparation();
DROP TABLE branch_day_settlement_audit,branch_day_settlement_receipts,branch_day_settlement_decisions,branch_day_settlement_operations,branch_day_settlements;
DROP FUNCTION protect_settlement_history(); DROP FUNCTION protect_settlement_projection();
