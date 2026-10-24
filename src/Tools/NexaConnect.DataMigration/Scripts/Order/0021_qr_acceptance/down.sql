DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM order_qr_acceptances) OR EXISTS(SELECT 1 FROM order_qr_requests WHERE status IN('price_review','accepted') OR consented_snapshot IS NOT NULL) OR EXISTS(SELECT 1 FROM order_qr_request_audit WHERE action IN('price_review','price_confirmed','accepted')) THEN
 RAISE EXCEPTION 'QR acceptance/review history exists; forward recovery required'; END IF;
END $$;
DROP TABLE order_qr_acceptances;
DROP FUNCTION prevent_order_qr_acceptance_mutation();
ALTER TABLE order_qr_requests DROP COLUMN review_snapshot,DROP COLUMN consented_snapshot;
ALTER TABLE order_qr_requests DROP CONSTRAINT order_qr_requests_status_check;
ALTER TABLE order_qr_requests ADD CONSTRAINT order_qr_requests_status_check CHECK(status IN('pending','rejected','expired'));
DROP INDEX ux_order_qr_pending_visitor;
CREATE UNIQUE INDEX ux_order_qr_pending_visitor ON order_qr_requests(organization_id,branch_id,visitor_id) WHERE status='pending';
ALTER TABLE order_qr_request_audit DROP CONSTRAINT order_qr_request_audit_action_check;
ALTER TABLE order_qr_request_audit ADD CONSTRAINT order_qr_request_audit_action_check CHECK(action IN('submitted','rejected','expired'));
