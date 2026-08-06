DROP INDEX IF EXISTS uq_shifts_authorization_decision_id;
ALTER TABLE shifts DROP COLUMN authorization_decision_id;
