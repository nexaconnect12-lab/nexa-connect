DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_financial_changes WHERE (attribution->>'version')::int>=2) THEN RAISE EXCEPTION 'Exact attribution history requires forward recovery'; END IF; END; $$;
CREATE OR REPLACE FUNCTION advance_source_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item jsonb; r uuid; b uuid; scopes jsonb='[]'::jsonb; scope record; parent jsonb; anchors jsonb; old_times jsonb; new_times jsonb; uncertain boolean=false; next_revision bigint;
BEGIN
 IF TG_OP='UPDATE' AND NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 -- Retain both scopes on reassignment; sort keys before locking to reduce deadlocks.
 FOR item IN SELECT value FROM jsonb_array_elements(
   CASE TG_OP WHEN 'INSERT' THEN jsonb_build_array(to_jsonb(NEW))
     WHEN 'DELETE' THEN jsonb_build_array(to_jsonb(OLD))
     ELSE jsonb_build_array(to_jsonb(OLD),to_jsonb(NEW)) END)
 LOOP
  IF TG_NARGS=0 THEN parent=item; ELSE SELECT to_jsonb(o) INTO parent FROM orders o WHERE id=(item->>TG_ARGV[0])::uuid; END IF;
  IF parent IS NULL OR parent->>'created_at_utc' IS NULL THEN uncertain=true; END IF;
  SELECT COALESCE(jsonb_agg(value),'[]'::jsonb) INTO anchors FROM jsonb_array_elements(jsonb_build_array(parent->'created_at_utc',parent->'completed_at_utc',parent->'updated_at_utc',parent->'receipt_snapshot'->'PaidAtUtc')) WHERE value<>'null'::jsonb;
  IF TG_OP='DELETE' OR TG_OP='UPDATE' AND old_times IS NULL THEN old_times=anchors; ELSE new_times=anchors; END IF;
  r=NULL; b=NULL;
  IF TG_NARGS=0 THEN r=(item->>'restaurant_id')::uuid; b=(item->>'branch_id')::uuid;
  ELSE
   SELECT restaurant_id,branch_id INTO r,b FROM orders WHERE id=(item->>TG_ARGV[0])::uuid;
  END IF;
  IF r IS NOT NULL AND b IS NOT NULL THEN scopes=scopes||jsonb_build_array(jsonb_build_object('r',r,'b',b)); END IF;
 END LOOP;
 IF TG_OP='UPDATE' AND (to_jsonb(OLD)->'restaurant_id' IS DISTINCT FROM to_jsonb(NEW)->'restaurant_id' OR to_jsonb(OLD)->'branch_id' IS DISTINCT FROM to_jsonb(NEW)->'branch_id' OR to_jsonb(OLD)->'organization_id' IS DISTINCT FROM to_jsonb(NEW)->'organization_id' OR to_jsonb(OLD)->'store_id' IS DISTINCT FROM to_jsonb(NEW)->'store_id' OR to_jsonb(OLD)->'order_id' IS DISTINCT FROM to_jsonb(NEW)->'order_id' OR to_jsonb(OLD)->'refund_id' IS DISTINCT FROM to_jsonb(NEW)->'refund_id' OR to_jsonb(OLD)->'cash_session_id' IS DISTINCT FROM to_jsonb(NEW)->'cash_session_id') THEN uncertain=true; END IF;
 FOR scope IN SELECT DISTINCT (value->>'r')::uuid AS r,(value->>'b')::uuid AS b
   FROM jsonb_array_elements(scopes) ORDER BY r,b
 LOOP
  INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(scope.r,scope.b,1)
   ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1 RETURNING revision INTO next_revision;
  INSERT INTO source_financial_changes(restaurant_id,branch_id,epoch,revision,attribution)
   SELECT scope.r,scope.b,epoch,next_revision,CASE WHEN uncertain THEN NULL ELSE
    jsonb_build_object('version',1,'kind',TG_TABLE_NAME,'before',old_times,'after',new_times) END
   FROM source_financial_epoch WHERE singleton;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END; $$;


DROP FUNCTION source_financial_change_state(text,jsonb,jsonb,text,boolean);

