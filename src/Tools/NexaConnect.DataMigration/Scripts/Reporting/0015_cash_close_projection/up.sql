CREATE TABLE cash_close_facts (
    session_id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    store_id uuid NOT NULL,
    closed_at_utc timestamptz NOT NULL,
    snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
    projected_at_utc timestamptz NOT NULL
);
CREATE INDEX ix_cash_close_facts_scope_time ON cash_close_facts(organization_id,branch_id,store_id,closed_at_utc DESC,session_id DESC);
CREATE TABLE cash_close_event_receipts (
    event_id uuid PRIMARY KEY,
    payload_hash text NOT NULL CHECK(length(payload_hash)=64)
);
