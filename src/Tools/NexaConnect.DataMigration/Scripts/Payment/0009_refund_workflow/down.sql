DO $$ BEGIN IF EXISTS(SELECT 1 FROM refunds) THEN RAISE EXCEPTION 'Cannot downgrade Payment refund workflow after refund history exists.'; END IF; END $$;
DROP TRIGGER tr_refunds_completed_immutable ON refunds;
DROP FUNCTION prevent_completed_refund_mutation();
DROP INDEX ix_refunds_recovery;
DROP INDEX ix_refunds_tenant_intent;
DROP INDEX uq_refunds_provider_ref;
DROP INDEX uq_refunds_operation;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_receipt;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_attempts;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_lease_owner;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_provider_ref;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_reason_code;
ALTER TABLE refunds DROP CONSTRAINT ck_refunds_status;
ALTER TABLE refunds ADD CONSTRAINT ck_refunds_status CHECK(status IN('requested','processing','completed','failed','cancelled'));
ALTER TABLE refunds DROP COLUMN last_reconciled_at_utc,DROP COLUMN recovery_attempt_count,DROP COLUMN lease_expires_at_utc,
 DROP COLUMN lease_owner,DROP COLUMN receipt_snapshot,DROP COLUMN failure_code,DROP COLUMN provider_refund_id,
 DROP COLUMN authorization_decision_id,DROP COLUMN operation_id,DROP COLUMN order_id,DROP COLUMN branch_id,
 DROP COLUMN restaurant_id,DROP COLUMN organization_id;
ALTER TABLE payment_audit_records DROP CONSTRAINT ck_payment_audit_records_action;
ALTER TABLE payment_audit_records ADD CONSTRAINT ck_payment_audit_records_action CHECK(action IN('payment.intent.created','payment.authorization.started','payment.authorization.succeeded','payment.authorization.failed','payment.authorization.uncertain','payment.authorization.reconciled','payment.capture.started','payment.capture.succeeded','payment.capture.failed','payment.capture.uncertain','payment.capture.reconciled','payment.void.started','payment.void.succeeded','payment.void.failed','payment.void.uncertain','payment.void.reconciled'));
