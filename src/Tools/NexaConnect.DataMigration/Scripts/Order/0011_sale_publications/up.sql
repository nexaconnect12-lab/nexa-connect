CREATE TABLE order_sale_publications
(
    order_id uuid PRIMARY KEY REFERENCES orders(id),
    event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    paid_at_utc timestamptz NOT NULL,
    payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ix_order_sale_publications_scope ON order_sale_publications(organization_id,branch_id,paid_at_utc,order_id);
CREATE FUNCTION protect_sale_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Sale publication evidence is immutable'; END $$;
CREATE TRIGGER order_sale_publication_guard BEFORE UPDATE OR DELETE ON order_sale_publications
FOR EACH ROW EXECUTE FUNCTION protect_sale_publication();
CREATE TABLE order_sale_replay_audit
(
    run_id uuid NOT NULL,
    order_id uuid NOT NULL REFERENCES order_sale_publications(order_id),
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    actor text NOT NULL CHECK(length(btrim(actor)) BETWEEN 1 AND 128),
    occurred_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(run_id,order_id)
);
CREATE TRIGGER order_sale_replay_audit_guard BEFORE UPDATE OR DELETE ON order_sale_replay_audit
FOR EACH ROW EXECUTE FUNCTION protect_sale_publication();
