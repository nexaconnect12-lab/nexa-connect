DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM refund_financial_publications) THEN
        RAISE EXCEPTION 'Refund publication history exists; use forward recovery';
    END IF;
END $$;
DROP TABLE refund_financial_replay_audit;
DROP TABLE refund_financial_publications;
DROP FUNCTION protect_refund_financial_evidence();
DROP INDEX ix_refunds_financial_scope;
