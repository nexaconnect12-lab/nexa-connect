DO $$ BEGIN IF EXISTS(SELECT 1 FROM refunds) THEN RAISE EXCEPTION 'Payment 9 requires explicit migration of legacy refund evidence.'; END IF; END $$;
ALTER TABLE refunds ADD COLUMN organization_id uuid NULL;
ALTER TABLE refunds ADD COLUMN restaurant_id uuid NULL;
ALTER TABLE refunds ADD COLUMN branch_id uuid NULL;
ALTER TABLE refunds ADD COLUMN order_id uuid NULL;
ALTER TABLE refunds ADD COLUMN operation_id uuid NULL;
ALTER TABLE refunds ADD COLUMN authorization_decision_id uuid NULL;
ALTER TABLE refunds ADD COLUMN provider_refund_id text NULL;
ALTER TABLE refunds ADD COLUMN failure_code text NULL;
ALTER TABLE refunds ADD COLUMN receipt_snapshot jsonb NULL;
ALTER TABLE refunds ADD COLUMN lease_owner text NULL;
ALTER TABLE refunds ADD COLUMN lease_expires_at_utc timestamptz NULL;
ALTER TABLE refunds ADD COLUMN recovery_attempt_count integer NOT NULL DEFAULT 0;
ALTER TABLE refunds ADD COLUMN last_reconciled_at_utc timestamptz NULL;

UPDATE refunds r SET organization_id=p.organization_id,restaurant_id=p.restaurant_id,branch_id=p.branch_id,
    order_id=p.order_id,operation_id=r.id,authorization_decision_id=r.id
FROM payment_intents p WHERE p.id=r.payment_intent_id;

ALTER TABLE refunds ALTER COLUMN organization_id SET NOT NULL;
ALTER TABLE refunds ALTER COLUMN restaurant_id SET NOT NULL;
ALTER TABLE refunds ALTER COLUMN branch_id SET NOT NULL;
ALTER TABLE refunds ALTER COLUMN order_id SET NOT NULL;
ALTER TABLE refunds ALTER COLUMN operation_id SET NOT NULL;
ALTER TABLE refunds ALTER COLUMN authorization_decision_id SET NOT NULL;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_status;
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_status CHECK(status IN('processing','refund_unknown','review_required','completed','failed'));
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_reason_code CHECK(reason_code IN('customer_request','duplicate_charge','item_unavailable','service_issue','other'));
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_provider_ref CHECK(provider_refund_id IS NULL OR char_length(btrim(provider_refund_id)) BETWEEN 1 AND 200);
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_lease_owner CHECK(lease_owner IS NULL OR char_length(btrim(lease_owner)) BETWEEN 1 AND 200);
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_attempts CHECK(recovery_attempt_count BETWEEN 0 AND 100);
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_receipt CHECK((status='completed')=(receipt_snapshot IS NOT NULL) AND (receipt_snapshot IS NULL OR jsonb_typeof(receipt_snapshot)='object'));
CREATE UNIQUE INDEX uq_refunds_operation ON refunds(payment_intent_id,operation_id);
CREATE UNIQUE INDEX uq_refunds_provider_ref ON refunds(provider_refund_id) WHERE provider_refund_id IS NOT NULL;
CREATE INDEX ix_refunds_tenant_intent ON refunds(organization_id,payment_intent_id,requested_at_utc DESC);
CREATE INDEX ix_refunds_recovery ON refunds(lease_expires_at_utc) WHERE status='processing';

CREATE FUNCTION prevent_completed_refund_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.receipt_snapshot IS NOT NULL THEN RAISE EXCEPTION 'completed refunds and receipt snapshots are immutable'; END IF;
  RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
END; $$;
CREATE TRIGGER tr_refunds_completed_immutable BEFORE UPDATE OR DELETE ON refunds
FOR EACH ROW EXECUTE FUNCTION prevent_completed_refund_mutation();

ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK(action IN(
 'payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled',
 'payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain','payment.capture.reconciled',
 'payment.void.started','payment.void.succeeded','payment.void.failed','payment.void.uncertain','payment.void.reconciled',
 'payment.refund.requested','payment.refund.succeeded','payment.refund.failed','payment.refund.uncertain','payment.refund.reconciled','payment.refund.review-required'));

COMMENT ON COLUMN refunds.authorization_decision_id IS 'Live product authorization decision retained for the manager refund command.';
COMMENT ON COLUMN refunds.receipt_snapshot IS 'Immutable customer refund receipt created only after a definitive provider refund.';
