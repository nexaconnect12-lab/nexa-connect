ALTER TABLE shifts ADD COLUMN close_authorization_decision_id uuid NULL;
CREATE UNIQUE INDEX uq_shifts_close_authorization_decision_id
    ON shifts (close_authorization_decision_id)
    WHERE close_authorization_decision_id IS NOT NULL;
