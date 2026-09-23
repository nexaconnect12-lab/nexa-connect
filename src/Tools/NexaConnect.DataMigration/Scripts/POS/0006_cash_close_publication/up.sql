CREATE TABLE cash_close_publications (
    cash_session_id uuid PRIMARY KEY REFERENCES cash_sessions(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    financial_version bigint NOT NULL CHECK(financial_version>0),
    review_version bigint NOT NULL CHECK(review_version>=0),
    snapshot_version bigint NOT NULL CHECK(snapshot_version>0)
);
CREATE INDEX ix_cash_sessions_closed_publication ON cash_sessions(id) WHERE status='closed';
