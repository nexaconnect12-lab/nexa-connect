DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM source_day_cutoffs WHERE payload->>'evidenceProtocolVersion'='4')
 OR EXISTS(SELECT 1 FROM source_financial_changes WHERE attribution->>'kind'='manual_refunds') THEN
 RAISE EXCEPTION 'Manual refund settlement evidence is retained; POS29 requires forward recovery'; END IF;
END $$;
DROP TRIGGER manual_refund_financial_change ON manual_refund_completions;
DROP TRIGGER manual_refund_financial_change ON manual_refund_payouts;
DROP FUNCTION journal_manual_refund_change();
DROP FUNCTION source_change_blocks_fence(jsonb,timestamptz,timestamptz);
ALTER FUNCTION source_change_blocks_fence_before_manual(jsonb,timestamptz,timestamptz) RENAME TO source_change_blocks_fence;
-- Keep the widened epoch FK: it is compatible with protocol 3 and preserves all historical bindings.

CREATE OR REPLACE FUNCTION require_correction_day_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE evidence jsonb; expected integer; amount numeric;
BEGIN
 evidence := NEW.payload->'cashCorrections';
 IF NEW.payload->>'evidenceProtocolVersion' IS DISTINCT FROM '3'
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
