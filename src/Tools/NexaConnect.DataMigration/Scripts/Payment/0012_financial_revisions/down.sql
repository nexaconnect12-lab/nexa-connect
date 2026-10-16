DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_cutoffs WHERE payload->>'evidenceProtocolVersion'='2') THEN
 RAISE EXCEPTION 'Revision-bound cutoff history requires forward recovery'; END IF; END; $$;
DROP TRIGGER financial_revision_change ON payment_intents;
DROP TRIGGER financial_revision_no_truncate ON payment_intents;
DROP TRIGGER financial_revision_change ON refunds;
DROP TRIGGER financial_revision_no_truncate ON refunds;
DROP TRIGGER financial_revision_change ON refund_financial_publications;
DROP TRIGGER financial_revision_no_truncate ON refund_financial_publications;
DROP FUNCTION advance_source_financial_revision();
DROP FUNCTION reject_financial_source_truncate();
DROP TABLE source_financial_revisions;
DROP TABLE source_financial_epoch;
DROP FUNCTION protect_financial_revision();
