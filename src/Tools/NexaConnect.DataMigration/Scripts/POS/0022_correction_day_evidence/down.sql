DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM source_day_cutoffs WHERE payload->>'evidenceProtocolVersion'='3') THEN
  RAISE EXCEPTION 'Retained correction-aware manifests prevent downgrade';
 END IF;
END $$;
DROP TRIGGER source_day_cutoff_correction_guard ON source_day_cutoffs;
DROP FUNCTION require_correction_day_evidence();
ALTER TABLE source_day_cutoffs DROP CONSTRAINT fk_correction_manifest_epoch;
ALTER TABLE source_day_cutoffs DROP COLUMN correction_revision_epoch;
DROP INDEX ux_correction_source_epoch;
