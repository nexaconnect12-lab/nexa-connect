DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_barriers) OR EXISTS(SELECT 1 FROM source_late_work) THEN RAISE EXCEPTION 'Settlement history requires forward recovery'; END IF; END; $$;
DROP TRIGGER source_fence_pinned ON source_day_fences; DROP FUNCTION protect_pinned_source_fence();
DROP TABLE source_late_work_links,source_late_work,source_day_barrier_audit,source_day_barriers;
DROP FUNCTION protect_day_barrier_history(); DROP FUNCTION protect_day_barrier_state();
 CREATE OR REPLACE FUNCTION enforce_source_day_fence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM source_day_fences f WHERE f.restaurant_id=NEW.restaurant_id AND f.branch_id=NEW.branch_id AND NOT f.cancelled
 AND f.expires_at_utc>clock_timestamp() AND source_change_blocks_fence(to_jsonb(NEW)->'attribution',f.from_utc,f.to_utc)) THEN RAISE EXCEPTION 'financial_day_fenced'; END IF;RETURN NEW; END; $$;

