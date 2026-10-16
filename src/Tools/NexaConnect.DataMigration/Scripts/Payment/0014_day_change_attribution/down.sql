DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_financial_changes WHERE attribution IS NOT NULL) THEN RAISE EXCEPTION 'Retained attribution requires forward recovery'; END IF; END; $$;
CREATE OR REPLACE FUNCTION advance_source_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item jsonb; r uuid; b uuid; scopes jsonb='[]'::jsonb; scope record;
BEGIN
 IF TG_OP='UPDATE' AND NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 -- Retain both scopes on reassignment; sort keys before locking to reduce deadlocks.
 FOR item IN SELECT value FROM jsonb_array_elements(
   CASE TG_OP WHEN 'INSERT' THEN jsonb_build_array(to_jsonb(NEW))
     WHEN 'DELETE' THEN jsonb_build_array(to_jsonb(OLD))
     ELSE jsonb_build_array(to_jsonb(OLD),to_jsonb(NEW)) END)
 LOOP
  r=NULL; b=NULL;
  IF TG_NARGS=0 THEN r=(item->>'restaurant_id')::uuid; b=(item->>'branch_id')::uuid;
  ELSE
   SELECT restaurant_id,branch_id INTO r,b FROM refunds WHERE id=(item->>TG_ARGV[0])::uuid;
  END IF;
  IF r IS NOT NULL AND b IS NOT NULL THEN scopes=scopes||jsonb_build_array(jsonb_build_object('r',r,'b',b)); END IF;
 END LOOP;
 FOR scope IN SELECT DISTINCT (value->>'r')::uuid AS r,(value->>'b')::uuid AS b
   FROM jsonb_array_elements(scopes) ORDER BY r,b
 LOOP
  INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(scope.r,scope.b,1)
   ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END; $$;

CREATE TRIGGER source_financial_revision_journal AFTER INSERT OR UPDATE ON source_financial_revisions FOR EACH ROW EXECUTE FUNCTION journal_source_financial_revision();
ALTER TABLE source_financial_changes DROP COLUMN attribution;
