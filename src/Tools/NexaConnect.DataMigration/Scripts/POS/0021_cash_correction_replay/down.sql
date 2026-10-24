DO $$ BEGIN IF EXISTS(SELECT 1 FROM cash_correction_replay_runs) THEN RAISE EXCEPTION 'Retained correction replay history prevents downgrade'; END IF; END $$;
DROP TABLE cash_correction_replay_attempts;
DROP TABLE cash_correction_replay_runs;
DROP FUNCTION cash_correction_replay_immutable();
