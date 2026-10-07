DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_day_cutoffs WHERE payload->>'evidenceProtocolVersion'='2') THEN
 RAISE EXCEPTION 'Revision-bound cutoff history requires forward recovery'; END IF; END; $$;
DROP TRIGGER financial_revision_change ON stores;
DROP TRIGGER financial_revision_no_truncate ON stores;
DROP TRIGGER financial_revision_change ON cash_movements;
DROP TRIGGER financial_revision_no_truncate ON cash_movements;
DROP TRIGGER financial_revision_change ON cash_session_review_states;
DROP TRIGGER financial_revision_no_truncate ON cash_session_review_states;
DROP TRIGGER financial_revision_change ON cash_sessions;
DROP TRIGGER financial_revision_no_truncate ON cash_sessions;
DROP TRIGGER financial_revision_change ON shifts;
DROP TRIGGER financial_revision_no_truncate ON shifts;
DROP FUNCTION advance_source_financial_revision();
DROP FUNCTION reject_financial_source_truncate();
DROP TABLE source_financial_revisions;
DROP TABLE source_financial_epoch;
DROP FUNCTION protect_financial_revision();
