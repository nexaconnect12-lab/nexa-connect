-- The restored separation check refuses downgrade if cashier authorization evidence exists.
ALTER TABLE manual_refund_approvals
    DROP CONSTRAINT manual_refund_approvals_authority_check,
    ADD CONSTRAINT manual_refund_approvals_check CHECK (manager_subject <> cashier_subject),
    DROP COLUMN cashier_limit;
