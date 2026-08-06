ALTER TABLE shifts ADD COLUMN authorization_decision_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE shifts ALTER COLUMN authorization_decision_id DROP DEFAULT;
CREATE UNIQUE INDEX uq_shifts_authorization_decision_id ON shifts (authorization_decision_id);
