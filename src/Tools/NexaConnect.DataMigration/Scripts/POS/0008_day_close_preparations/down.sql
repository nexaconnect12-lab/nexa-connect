DO $$ BEGIN
IF EXISTS(SELECT 1 FROM branch_day_closes) OR EXISTS(SELECT 1 FROM branch_day_close_audit) OR EXISTS(SELECT 1 FROM branch_day_close_operations)
THEN RAISE EXCEPTION 'Retained day-close preparation evidence prevents downgrade'; END IF;
END $$;
DROP TABLE branch_day_close_audit, branch_day_close_operations, branch_day_closes;
DROP FUNCTION protect_branch_day_close_audit();
