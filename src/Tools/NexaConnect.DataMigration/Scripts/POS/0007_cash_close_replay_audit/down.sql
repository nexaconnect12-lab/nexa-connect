DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM cash_close_replay_runs) THEN
        RAISE EXCEPTION 'Cannot remove cash-close replay audit after use';
    END IF;
END; $$;
DROP TABLE cash_close_replay_attempts;
DROP TABLE cash_close_replay_runs;
DROP FUNCTION cash_close_replay_immutable();
DROP INDEX ix_cash_close_replay_scope;
