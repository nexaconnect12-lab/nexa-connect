-- Financial changes are journaled in the same transaction as payout custody/completion.
CREATE FUNCTION journal_manual_refund_change() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE sale manual_refund_sales; payout manual_refund_payouts; rev bigint; prior jsonb; next_state jsonb;
BEGIN
 SELECT s.* INTO STRICT sale FROM manual_refund_sales s JOIN manual_refund_approvals a USING(order_id) WHERE a.operation_id=NEW.operation_id;
 SELECT * INTO STRICT payout FROM manual_refund_payouts WHERE operation_id=NEW.operation_id;
 next_state=jsonb_build_object('id',NEW.operation_id,'status','started','version',1,'createdAtUtc',payout.started_at_utc);
 prior=NULL;
 IF TG_TABLE_NAME='manual_refund_completions' THEN
  prior=next_state;
  next_state=next_state||jsonb_build_object('status','completed','version',2,'financialAtUtc',NEW.recorded_at_utc);
 END IF;
 INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(sale.restaurant_id,sale.branch_id,1)
 ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1 RETURNING revision INTO rev;
 INSERT INTO source_financial_changes(restaurant_id,branch_id,epoch,revision,attribution)
 SELECT sale.restaurant_id,sale.branch_id,epoch,rev,jsonb_build_object('version',2,'kind','manual_refunds','recordId',NEW.operation_id,
 'operation','INSERT','ownershipUncertain',false,'before',prior,'after',next_state) FROM source_financial_epoch WHERE singleton;
 RETURN NEW;
END $$;
CREATE TRIGGER manual_refund_financial_change AFTER INSERT ON manual_refund_payouts FOR EACH ROW EXECUTE FUNCTION journal_manual_refund_change();
CREATE TRIGGER manual_refund_financial_change AFTER INSERT ON manual_refund_completions FOR EACH ROW EXECUTE FUNCTION journal_manual_refund_change();

ALTER FUNCTION source_change_blocks_fence(jsonb,timestamptz,timestamptz) RENAME TO source_change_blocks_fence_before_manual;
CREATE FUNCTION source_change_blocks_fence(entry jsonb,start_time timestamptz,end_time timestamptz) RETURNS boolean LANGUAGE plpgsql AS $$
DECLARE item jsonb; status text; created timestamptz; financial timestamptz;
BEGIN
 IF entry->>'kind' IS DISTINCT FROM 'manual_refunds' THEN RETURN source_change_blocks_fence_before_manual(entry,start_time,end_time); END IF;
 IF entry->>'version' IS DISTINCT FROM '2' OR COALESCE((entry->>'ownershipUncertain')::boolean,true)
 OR entry->'before'->>'id' IS NOT NULL AND entry->'before'->>'id'<>entry->'after'->>'id' THEN RETURN true; END IF;
 IF entry->'after'->>'id' IS NULL THEN RETURN true; END IF;
 FOR item IN SELECT value FROM jsonb_array_elements(jsonb_build_array(entry->'before',entry->'after')) WHERE value IS NOT NULL AND value<>'null'::jsonb LOOP
  IF item->>'id' IS NULL OR (item->>'id')::uuid='00000000-0000-0000-0000-000000000000'::uuid
   OR COALESCE((item->>'version')::bigint,0)<=0 OR item->>'createdAtUtc' IS NULL THEN RETURN true; END IF;
  status=item->>'status';created=(item->>'createdAtUtc')::timestamptz;financial=(item->>'financialAtUtc')::timestamptz;
  IF created='0001-01-01T00:00:00Z'::timestamptz OR status IS NULL OR status NOT IN('started','completed') THEN RETURN true; END IF;
  IF status='started' AND created<end_time THEN RETURN true; END IF;
  IF status='completed' THEN
   IF financial IS NULL OR financial<created THEN RETURN true; END IF;
   IF financial>=start_time AND financial<end_time THEN RETURN true; END IF;
  END IF;
 END LOOP;
 RETURN false;
 EXCEPTION WHEN OTHERS THEN RETURN true;
END $$;

ALTER TABLE source_day_cutoffs DROP CONSTRAINT fk_correction_manifest_epoch;
ALTER TABLE source_day_cutoffs DROP COLUMN correction_revision_epoch;
ALTER TABLE source_day_cutoffs ADD COLUMN correction_revision_epoch uuid GENERATED ALWAYS AS
 (CASE WHEN payload->>'evidenceProtocolVersion' IN('3','4') THEN (payload->'sourceRevision'->>'epoch')::uuid ELSE NULL END) STORED;
ALTER TABLE source_day_cutoffs ADD CONSTRAINT fk_correction_manifest_epoch FOREIGN KEY(correction_revision_epoch) REFERENCES source_financial_epoch(epoch);

CREATE OR REPLACE FUNCTION require_correction_day_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE evidence jsonb; expected integer; amount numeric;
BEGIN
 evidence := NEW.payload->'cashCorrections';
 IF NEW.payload->>'evidenceProtocolVersion' IS DISTINCT FROM '4'
    OR jsonb_typeof(NEW.payload->'summary'->'manualRefunds') IS DISTINCT FROM 'object'
    OR jsonb_typeof(evidence) IS DISTINCT FROM 'object'
    OR jsonb_typeof(evidence->'events') IS DISTINCT FROM 'array'
    OR NEW.payload->'sourceRevision'->>'epoch' IS NULL
    OR COALESCE((NEW.payload->'sourceRevision'->>'revision')::bigint,-1)<0 THEN
  RAISE EXCEPTION 'Correction-aware POS manifest required' USING ERRCODE='23514';
 END IF;
 expected := jsonb_array_length(evidence->'events');
 SELECT COALESCE(sum((e->>'cashVarianceAdjustment')::numeric),0) INTO amount FROM jsonb_array_elements(evidence->'events') e;
 IF expected>1000 OR (evidence->>'organizationId')::uuid IS DISTINCT FROM NEW.organization_id
    OR (evidence->>'restaurantId')::uuid IS DISTINCT FROM NEW.restaurant_id OR (evidence->>'branchId')::uuid IS DISTINCT FROM NEW.branch_id
    OR evidence->>'fromUtc' IS NULL OR evidence->>'toUtc' IS NULL
    OR abs(extract(epoch FROM ((evidence->>'fromUtc')::timestamptz-NEW.from_utc)))>0.000001
    OR abs(extract(epoch FROM ((evidence->>'toUtc')::timestamptz-NEW.to_utc)))>0.000001
    OR (NEW.payload->'summary'->>'lateCashCorrections')::integer IS DISTINCT FROM expected
    OR (NEW.payload->'summary'->>'lateCashCorrectionAdjustment')::numeric IS DISTINCT FROM amount THEN
  RAISE EXCEPTION 'Correction manifest scope or summary inconsistent' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
