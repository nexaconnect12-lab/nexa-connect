DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM order_qr_requests) OR EXISTS(SELECT 1 FROM order_qr_request_audit) THEN
  RAISE EXCEPTION 'QR request history exists; forward recovery required';
 END IF;
END $$;
DROP TABLE order_qr_request_audit;
DROP FUNCTION prevent_order_qr_audit_mutation();
DROP TABLE order_qr_requests;
DROP TABLE order_qr_quotes;
