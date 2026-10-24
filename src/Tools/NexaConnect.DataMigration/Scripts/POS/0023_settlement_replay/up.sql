CREATE TABLE settlement_replay_runs(
 id uuid PRIMARY KEY,organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,
 from_date date NOT NULL,to_date date NOT NULL CHECK(to_date>from_date AND to_date-from_date<=31),
 operator_id uuid NOT NULL,database_actor text NOT NULL DEFAULT session_user,reason text NOT NULL CHECK(reason IN('retry','rebuild')),
 manifest text NOT NULL CHECK(manifest ~ '^[A-F0-9]{64}$'),selection_limit integer NOT NULL CHECK(selection_limit BETWEEN 1 AND 31),
 event_count integer NOT NULL CHECK(event_count BETWEEN 0 AND selection_limit),
 selected_events jsonb NOT NULL CHECK(jsonb_typeof(selected_events)='array' AND jsonb_array_length(selected_events)=event_count),
 created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE TABLE settlement_replay_attempts(
 run_id uuid NOT NULL REFERENCES settlement_replay_runs(id),event_id uuid NOT NULL,outcome text NOT NULL CHECK(outcome IN('started','confirmed')),
 database_actor text NOT NULL DEFAULT session_user,created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(run_id,event_id,outcome));
CREATE FUNCTION settlement_replay_append_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 NEW.database_actor:=session_user;
 IF TG_TABLE_NAME='settlement_replay_attempts' THEN
  IF NOT EXISTS(SELECT 1 FROM settlement_replay_runs r,jsonb_array_elements(r.selected_events) e
     WHERE r.id=NEW.run_id AND (e->>'id')::uuid=NEW.event_id) THEN
   RAISE EXCEPTION 'Event absent from reviewed settlement replay selection' USING ERRCODE='23514';
  END IF;
  IF NEW.outcome='confirmed' AND NOT EXISTS(SELECT 1 FROM settlement_replay_attempts a
     WHERE a.run_id=NEW.run_id AND a.event_id=NEW.event_id AND a.outcome='started') THEN
   RAISE EXCEPTION 'Settlement replay confirmation requires started attempt' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER settlement_replay_run_attribution BEFORE INSERT ON settlement_replay_runs FOR EACH ROW EXECUTE FUNCTION settlement_replay_append_guard();
CREATE TRIGGER settlement_replay_attempt_attribution BEFORE INSERT ON settlement_replay_attempts FOR EACH ROW EXECUTE FUNCTION settlement_replay_append_guard();
CREATE FUNCTION settlement_replay_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Settlement replay audit is append-only'; END $$;
CREATE TRIGGER settlement_replay_runs_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON settlement_replay_runs FOR EACH STATEMENT EXECUTE FUNCTION settlement_replay_immutable();
CREATE TRIGGER settlement_replay_attempts_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON settlement_replay_attempts FOR EACH STATEMENT EXECUTE FUNCTION settlement_replay_immutable();
