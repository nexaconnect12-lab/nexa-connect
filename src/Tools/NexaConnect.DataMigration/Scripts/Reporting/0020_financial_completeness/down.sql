DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM financial_completeness_checks) THEN
        RAISE EXCEPTION 'Financial completeness audit history exists; use forward recovery';
    END IF;
END $$;
DROP TABLE financial_completeness_checks;
DROP FUNCTION protect_financial_completeness_check();
