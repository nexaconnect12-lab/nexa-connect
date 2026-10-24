DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_cutoffs) THEN
 RAISE EXCEPTION 'Retained source cutoffs require forward recovery'; END IF; END; $$;
DROP TABLE source_day_cutoffs;
DROP FUNCTION protect_source_day_cutoffs();
