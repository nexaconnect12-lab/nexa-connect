CREATE TABLE refund_financial_publications
(
    refund_id uuid PRIMARY KEY REFERENCES refunds(id),
    event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    refunded_at_utc timestamptz NOT NULL,
    payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ix_refund_financial_publications_scope ON refund_financial_publications(organization_id,branch_id,refunded_at_utc,refund_id);
CREATE INDEX ix_refunds_financial_scope ON refunds(organization_id,branch_id,completed_at_utc,id) WHERE status='completed';
CREATE TABLE refund_financial_replay_audit
(
    run_id uuid NOT NULL,
    refund_id uuid NOT NULL REFERENCES refund_financial_publications(refund_id),
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    actor text NOT NULL CHECK(length(btrim(actor)) BETWEEN 1 AND 128),
    occurred_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(run_id,refund_id)
);
CREATE FUNCTION protect_refund_financial_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Refund financial publication and replay evidence is immutable'; END $$;
CREATE TRIGGER refund_financial_publication_guard BEFORE UPDATE OR DELETE ON refund_financial_publications
FOR EACH ROW EXECUTE FUNCTION protect_refund_financial_evidence();
CREATE TRIGGER refund_financial_replay_audit_guard BEFORE UPDATE OR DELETE ON refund_financial_replay_audit
FOR EACH ROW EXECUTE FUNCTION protect_refund_financial_evidence();
