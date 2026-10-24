CREATE FUNCTION source_financial_change_state(kind text,item jsonb,other jsonb,op text,is_after boolean) RETURNS jsonb LANGUAGE plpgsql AS $$
DECLARE p jsonb;
BEGIN
 IF kind='orders' THEN p=item; ELSE SELECT to_jsonb(o) INTO p FROM orders o WHERE id=(item->>'order_id')::uuid FOR UPDATE; END IF;
 IF p IS NULL THEN RETURN NULL; END IF;
 RETURN jsonb_build_object('id',p->'id','status',p->'status','version',p->'concurrency_version',
  'createdAtUtc',p->'created_at_utc','financialAtUtc',p->'receipt_snapshot'->'PaidAtUtc','hasReceipt',p->'receipt_snapshot' IS NOT NULL AND p->'receipt_snapshot'<>'null'::jsonb);
END; $$;
CREATE OR REPLACE FUNCTION advance_source_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item jsonb; r uuid; b uuid; scopes jsonb='[]'::jsonb; scope record; parent jsonb; anchors jsonb; old_times jsonb; new_times jsonb; uncertain boolean=false; next_revision bigint; before_state jsonb; after_state jsonb; record_id uuid;
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
   SELECT restaurant_id,branch_id INTO r,b FROM orders WHERE id=(item->>TG_ARGV[0])::uuid FOR UPDATE;
  END IF;
  IF r IS NOT NULL AND b IS NOT NULL THEN scopes=scopes||jsonb_build_array(jsonb_build_object('r',r,'b',b)); END IF;
 END LOOP;
 IF TG_OP='UPDATE' AND (to_jsonb(OLD)->'id' IS DISTINCT FROM to_jsonb(NEW)->'id' OR to_jsonb(OLD)->'restaurant_id' IS DISTINCT FROM to_jsonb(NEW)->'restaurant_id' OR to_jsonb(OLD)->'branch_id' IS DISTINCT FROM to_jsonb(NEW)->'branch_id' OR to_jsonb(OLD)->'organization_id' IS DISTINCT FROM to_jsonb(NEW)->'organization_id' OR to_jsonb(OLD)->'store_id' IS DISTINCT FROM to_jsonb(NEW)->'store_id' OR to_jsonb(OLD)->'order_id' IS DISTINCT FROM to_jsonb(NEW)->'order_id' OR to_jsonb(OLD)->'refund_id' IS DISTINCT FROM to_jsonb(NEW)->'refund_id' OR to_jsonb(OLD)->'cash_session_id' IS DISTINCT FROM to_jsonb(NEW)->'cash_session_id') THEN uncertain=true; END IF;
 record_id=CASE WHEN TG_OP='DELETE' THEN COALESCE(to_jsonb(OLD)->>'id',to_jsonb(OLD)->>'event_id',to_jsonb(OLD)->>'order_id',to_jsonb(OLD)->>'refund_id',to_jsonb(OLD)->>'cash_session_id')::uuid ELSE COALESCE(to_jsonb(NEW)->>'id',to_jsonb(NEW)->>'event_id',to_jsonb(NEW)->>'order_id',to_jsonb(NEW)->>'refund_id',to_jsonb(NEW)->>'cash_session_id')::uuid END;
 IF TG_OP<>'INSERT' THEN before_state=source_financial_change_state(TG_TABLE_NAME,to_jsonb(OLD),CASE WHEN TG_OP='UPDATE' THEN to_jsonb(NEW) ELSE NULL END,TG_OP,false); END IF;
 IF TG_OP<>'DELETE' THEN after_state=source_financial_change_state(TG_TABLE_NAME,to_jsonb(NEW),CASE WHEN TG_OP='UPDATE' THEN to_jsonb(OLD) ELSE NULL END,TG_OP,true); END IF;
 FOR scope IN SELECT DISTINCT (value->>'r')::uuid AS r,(value->>'b')::uuid AS b
   FROM jsonb_array_elements(scopes) ORDER BY r,b
 LOOP
  INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(scope.r,scope.b,1)
   ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1 RETURNING revision INTO next_revision;
  INSERT INTO source_financial_changes(restaurant_id,branch_id,epoch,revision,attribution)
   SELECT scope.r,scope.b,epoch,next_revision,jsonb_build_object('version',2,'kind',TG_TABLE_NAME,'recordId',record_id,'operation',TG_OP,
     'ownershipUncertain',uncertain,'before',before_state,'after',after_state)
   FROM source_financial_epoch WHERE singleton;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END; $$;

COMMENT ON COLUMN source_financial_changes.attribution IS 'Version two immutable before/after source financial selection metadata, identities and versions; legacy descriptors remain unknown. No monetary payload, personal data or provider identifiers.';
