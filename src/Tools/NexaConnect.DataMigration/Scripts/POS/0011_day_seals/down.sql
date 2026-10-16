DO $$ BEGIN IF EXISTS(SELECT 1 FROM branch_day_seals) OR EXISTS(SELECT 1 FROM branch_day_seal_operations) OR EXISTS(SELECT 1 FROM branch_day_seal_audit) THEN
 RAISE EXCEPTION 'Retained day-seal coordination requires forward recovery'; END IF; END; $$;
DROP TABLE branch_day_seal_audit,branch_day_seal_operations,branch_day_seals;
DROP FUNCTION protect_branch_day_seal_audit();
DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_seals) OR EXISTS(SELECT 1 FROM source_financial_changes) THEN
 RAISE EXCEPTION 'Retained seals and change journals require forward recovery'; END IF; END; $$;
DROP TRIGGER source_financial_revision_journal ON source_financial_revisions;
DROP FUNCTION journal_source_financial_revision();
DROP TABLE source_financial_changes;
DROP FUNCTION protect_source_financial_changes();
DROP TABLE source_day_seals;
DROP FUNCTION protect_source_day_seals();
CREATE OR REPLACE FUNCTION protect_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='source_financial_epoch' OR TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth()<2 THEN
  RAISE EXCEPTION 'Financial revision evidence is source-managed';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NEW.revision<>OLD.revision+1) THEN
  RAISE EXCEPTION 'Financial revisions must advance monotonically';
 END IF;
 IF TG_OP='INSERT' AND NEW.revision<>1 THEN RAISE EXCEPTION 'Invalid initial financial revision'; END IF;
 RETURN NEW;
END; $$;
