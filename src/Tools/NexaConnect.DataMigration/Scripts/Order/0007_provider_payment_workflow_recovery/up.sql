DROP INDEX ix_orders_workflow_recovery;
CREATE INDEX ix_orders_workflow_recovery ON orders(workflow_recovery_next_attempt_at_utc,updated_at_utc,id)
WHERE
    (status IN('submitted','inventory_reserved') AND workflow_payment_method IS NOT NULL)
    OR (status='kitchen_accepted' AND workflow_payment_method NOT IN('cash_manual','promptpay_manual'));
COMMENT ON COLUMN orders.workflow_payment_method IS 'Original checkout method. Recovery resumes provider payments after kitchen acceptance and stops manual tenders before payment.';
COMMENT ON COLUMN orders.workflow_recovery_claim_id IS 'Opaque fencing token for one Order workflow recovery attempt.';
