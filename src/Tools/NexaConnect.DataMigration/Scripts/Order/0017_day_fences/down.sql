DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_fences) OR EXISTS(SELECT 1 FROM source_day_fence_audit) THEN RAISE EXCEPTION 'Fence history requires forward recovery'; END IF; END; $$;
 DROP TRIGGER source_day_fence_write_guard ON source_financial_changes;DROP FUNCTION enforce_source_day_fence();DROP FUNCTION source_change_blocks_fence(jsonb,timestamptz,timestamptz);
 DROP TRIGGER source_fence_generation ON source_day_fences;DROP FUNCTION advance_source_fence_generation();DROP TABLE source_day_fence_audit;DROP FUNCTION protect_source_day_fence_audit();DROP TABLE source_day_fences;DROP FUNCTION protect_source_day_fence_state();
 CREATE OR REPLACE FUNCTION protect_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='source_financial_epoch' OR TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth()<2 THEN
  RAISE EXCEPTION 'Financial revision evidence is source-managed';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NEW.revision<>OLD.revision+1) THEN
  RAISE EXCEPTION 'Financial revisions must advance monotonically';
 END IF;
 IF TG_OP='INSERT' AND NEW.revision<>1 THEN RAISE EXCEPTION 'Invalid initial financial revision'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('financial-revision:'||NEW.restaurant_id::text||':'||NEW.branch_id::text,0));
 RETURN NEW;
END; $$;

 ALTER TABLE source_financial_revisions DROP CONSTRAINT source_financial_revisions_revision_check;
 ALTER TABLE source_financial_revisions ADD CONSTRAINT source_financial_revisions_revision_check CHECK(revision>0);
 ALTER TABLE source_financial_revisions DROP COLUMN fence_generation;
 