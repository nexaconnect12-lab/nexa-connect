DROP INDEX ix_orders_workflow_recovery;
CREATE INDEX ix_orders_workflow_recovery ON orders(workflow_recovery_next_attempt_at_utc,updated_at_utc,id)
WHERE status IN('submitted','inventory_reserved') AND workflow_payment_method IN('cash_manual','promptpay_manual');
COMMENT ON COLUMN orders.workflow_payment_method IS 'Original checkout method. Recovery is enabled only for manual tenders and stops before payment.';
COMMENT ON COLUMN orders.workflow_recovery_claim_id IS 'Opaque fencing token for one pre-payment recovery attempt.';
