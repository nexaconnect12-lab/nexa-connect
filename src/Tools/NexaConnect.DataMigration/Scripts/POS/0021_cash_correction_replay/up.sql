CREATE TABLE cash_correction_replay_runs (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, restaurant_id uuid NOT NULL,
    from_utc timestamptz NOT NULL, to_utc timestamptz NOT NULL CHECK(to_utc>from_utc),
    operator_id uuid NOT NULL, database_actor text NOT NULL DEFAULT session_user,
    reason text NOT NULL CHECK(reason IN ('rebuild','retry')),
    manifest text NOT NULL CHECK(length(manifest)=64), selection_limit integer NOT NULL CHECK(selection_limit BETWEEN 1 AND 1000),
    event_count integer NOT NULL CHECK(event_count BETWEEN 0 AND selection_limit),
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE cash_correction_replay_attempts (
    run_id uuid NOT NULL REFERENCES cash_correction_replay_runs(id), event_id uuid NOT NULL,
    outcome text NOT NULL CHECK(outcome IN ('started','confirmed')),
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(run_id,event_id,outcome)
);
CREATE FUNCTION cash_correction_replay_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Cash correction replay audit is append-only'; END; $$;
CREATE TRIGGER cash_correction_replay_runs_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cash_correction_replay_runs
    FOR EACH STATEMENT EXECUTE FUNCTION cash_correction_replay_immutable();
CREATE TRIGGER cash_correction_replay_attempts_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cash_correction_replay_attempts
    FOR EACH STATEMENT EXECUTE FUNCTION cash_correction_replay_immutable();
