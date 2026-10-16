CREATE UNIQUE INDEX ux_correction_source_epoch ON source_financial_epoch(epoch);
ALTER TABLE source_day_cutoffs ADD COLUMN correction_revision_epoch uuid GENERATED ALWAYS AS
 (CASE WHEN payload->>'evidenceProtocolVersion'='3' THEN (payload->'sourceRevision'->>'epoch')::uuid ELSE NULL END) STORED;
ALTER TABLE source_day_cutoffs ADD CONSTRAINT fk_correction_manifest_epoch FOREIGN KEY(correction_revision_epoch) REFERENCES source_financial_epoch(epoch);
CREATE FUNCTION require_correction_day_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
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
CREATE TRIGGER source_day_cutoff_correction_guard BEFORE INSERT ON source_day_cutoffs
 FOR EACH ROW EXECUTE FUNCTION require_correction_day_evidence();
