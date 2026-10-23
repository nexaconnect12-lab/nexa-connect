CREATE TABLE order_qr_quotes (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
 visitor_id uuid NOT NULL, entry_id uuid NOT NULL, table_code varchar(32) NOT NULL,
 fingerprint char(64) NOT NULL, snapshot jsonb NOT NULL CHECK (octet_length(snapshot::text) <= 131072),
 expires_at_utc timestamptz NOT NULL, created_at_utc timestamptz NOT NULL DEFAULT now(),
 UNIQUE (organization_id, branch_id, visitor_id, id)
);
CREATE INDEX ix_order_qr_quotes_scope_expiry ON order_qr_quotes(organization_id,branch_id,expires_at_utc);
CREATE TABLE order_qr_requests (
 id uuid PRIMARY KEY REFERENCES order_qr_quotes(id), organization_id uuid NOT NULL, branch_id uuid NOT NULL,
 visitor_id uuid NOT NULL, entry_id uuid NOT NULL,
 status varchar(16) NOT NULL CHECK (status IN ('pending','rejected','expired')),
 version bigint NOT NULL CHECK (version > 0), submitted_at_utc timestamptz NOT NULL, deadline_utc timestamptz NOT NULL,
 reason varchar(32) NULL CHECK (reason IN ('unavailable','closing','staff_review')),
 FOREIGN KEY (organization_id,branch_id,visitor_id,id) REFERENCES order_qr_quotes(organization_id,branch_id,visitor_id,id),
 CHECK (deadline_utc > submitted_at_utc), CHECK ((status='rejected')=(reason IS NOT NULL))
);
CREATE INDEX ix_order_qr_requests_queue ON order_qr_requests(organization_id,branch_id,status,submitted_at_utc);
CREATE INDEX ix_order_qr_requests_visitor ON order_qr_requests(organization_id,branch_id,visitor_id);
CREATE UNIQUE INDEX ux_order_qr_pending_visitor ON order_qr_requests(organization_id,branch_id,visitor_id) WHERE status='pending';
CREATE TABLE order_qr_request_audit (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, request_id uuid NOT NULL,
 action varchar(32) NOT NULL CHECK (action IN ('submitted','rejected','expired')), version bigint NOT NULL,
 actor varchar(200) NOT NULL, decision_id uuid NULL, correlation_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
 UNIQUE(request_id,version)
);
CREATE FUNCTION prevent_order_qr_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'order_qr_request_audit is append-only'; END; $$;
CREATE TRIGGER tr_order_qr_audit_append_only BEFORE UPDATE OR DELETE ON order_qr_request_audit
 FOR EACH ROW EXECUTE FUNCTION prevent_order_qr_audit_mutation();
