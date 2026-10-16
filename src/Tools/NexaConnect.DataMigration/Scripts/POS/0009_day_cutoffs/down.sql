DO $$ BEGIN
IF EXISTS(SELECT 1 FROM branch_day_cutoffs) OR EXISTS(SELECT 1 FROM branch_day_cutoff_audit) OR EXISTS(SELECT 1 FROM branch_day_cutoff_operations)
 OR EXISTS(SELECT 1 FROM source_day_cutoffs)
THEN RAISE EXCEPTION 'Retained day-cutoff evidence requires forward recovery'; END IF;
END $$;
DROP TABLE branch_day_cutoff_audit, branch_day_cutoff_operations, branch_day_cutoffs;
DROP FUNCTION protect_branch_day_cutoff_audit();
DROP TABLE source_day_cutoffs;
DROP FUNCTION protect_source_day_cutoffs();
