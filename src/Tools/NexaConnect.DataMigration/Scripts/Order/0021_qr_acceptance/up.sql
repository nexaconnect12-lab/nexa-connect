ALTER TABLE order_qr_requests DROP CONSTRAINT order_qr_requests_status_check;
ALTER TABLE order_qr_requests ADD CONSTRAINT order_qr_requests_status_check CHECK(status IN('pending','price_review','accepted','rejected','expired'));
ALTER TABLE order_qr_requests ADD COLUMN review_snapshot jsonb NULL CHECK(octet_length(review_snapshot::text)<=131072);
ALTER TABLE order_qr_requests ADD COLUMN consented_snapshot jsonb NULL CHECK(octet_length(consented_snapshot::text)<=131072);
DROP INDEX ux_order_qr_pending_visitor;
CREATE UNIQUE INDEX ux_order_qr_pending_visitor ON order_qr_requests(organization_id,branch_id,visitor_id) WHERE status IN('pending','price_review');
ALTER TABLE order_qr_request_audit DROP CONSTRAINT order_qr_request_audit_action_check;
ALTER TABLE order_qr_request_audit ADD CONSTRAINT order_qr_request_audit_action_check CHECK(action IN('submitted','rejected','expired','price_review','price_confirmed','accepted'));
CREATE TABLE order_qr_acceptances (
 request_id uuid PRIMARY KEY REFERENCES order_qr_requests(id),order_id uuid NOT NULL UNIQUE REFERENCES orders(id),
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,
 entry_id uuid NOT NULL,visitor_id uuid NOT NULL,table_code varchar(32) NOT NULL,
 actor varchar(200) NOT NULL,decision_id uuid NOT NULL,request_version bigint NOT NULL,
 fingerprint char(64) NOT NULL,correlation_id uuid NOT NULL,accepted_at_utc timestamptz NOT NULL,
 CHECK(request_id=order_id)
);
CREATE FUNCTION prevent_order_qr_acceptance_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'order_qr_acceptances is append-only'; END; $$;
CREATE TRIGGER tr_order_qr_acceptance_immutable BEFORE UPDATE OR DELETE ON order_qr_acceptances FOR EACH ROW EXECUTE FUNCTION prevent_order_qr_acceptance_mutation();
