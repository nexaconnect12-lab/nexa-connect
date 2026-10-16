DO $$ BEGIN IF EXISTS(SELECT 1 FROM branch_day_approval_decisions) OR EXISTS(SELECT 1 FROM branch_day_approval_audit)
 OR EXISTS(SELECT 1 FROM branch_day_approvals WHERE version>0) THEN RAISE EXCEPTION 'Approval history requires forward recovery'; END IF; END; $$;
DROP TABLE branch_day_approval_audit;
DROP TABLE branch_day_approval_decisions;
DROP TABLE branch_day_approvals;
DROP FUNCTION protect_day_approval_history();
