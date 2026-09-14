CREATE TABLE cash_session_review_states
(
    cash_session_id uuid PRIMARY KEY REFERENCES cash_sessions(id) ON DELETE RESTRICT,
    reviewed_session_version bigint NOT NULL CHECK (reviewed_session_version > 0),
    status text NOT NULL CHECK (status IN ('investigating', 'approved')),
    reviewed_by text NOT NULL CHECK (char_length(btrim(reviewed_by)) BETWEEN 1 AND 200),
    authorization_decision_id uuid NOT NULL,
    reviewed_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0)
);

CREATE TABLE cash_session_review_history
(
    id uuid PRIMARY KEY,
    cash_session_id uuid NOT NULL REFERENCES cash_sessions(id) ON DELETE RESTRICT,
    session_version bigint NOT NULL CHECK (session_version > 0),
    decision text NOT NULL CHECK (decision IN ('approve', 'investigate')),
    reason text NOT NULL CHECK (char_length(btrim(reason)) BETWEEN 1 AND 200),
    reviewer_subject_id text NOT NULL CHECK (char_length(btrim(reviewer_subject_id)) BETWEEN 1 AND 200),
    authorization_decision_id uuid NOT NULL,
    payload_hash char(64) NOT NULL CHECK (payload_hash ~ '^[0-9a-f]{64}$'),
    review_version bigint NOT NULL CHECK (review_version > 0),
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_cash_session_review_history_version UNIQUE (cash_session_id, review_version)
);

CREATE INDEX ix_cash_session_review_history_session_occurred
    ON cash_session_review_history(cash_session_id, occurred_at_utc, id);
CREATE INDEX ix_cash_sessions_store_closed_review
    ON cash_sessions(store_id, closed_at_utc DESC, id DESC)
    WHERE status = 'closed';

CREATE FUNCTION deny_cash_session_review_history_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'cash_session_review_history is append-only';
END;
$$;

CREATE TRIGGER cash_session_review_history_append_only
BEFORE UPDATE OR DELETE ON cash_session_review_history
FOR EACH ROW EXECUTE FUNCTION deny_cash_session_review_history_mutation();

COMMENT ON TABLE cash_session_review_history IS
    'Append-only supervisor review decisions. Financial values remain owned by cash_sessions and cash_movements.';
