DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM settlement_replay_runs) OR EXISTS(SELECT 1 FROM settlement_replay_attempts) THEN
  RAISE EXCEPTION 'Settlement replay audit history prevents downgrade';
 END IF;
END $$;
DROP TABLE settlement_replay_attempts;
DROP TABLE settlement_replay_runs;
DROP FUNCTION settlement_replay_append_guard();
DROP FUNCTION settlement_replay_immutable();
