ALTER TABLE manual_refund_approvals
    ADD COLUMN cashier_limit numeric(19,2),
    DROP CONSTRAINT manual_refund_approvals_check,
    ADD CONSTRAINT manual_refund_approvals_authority_check CHECK (
        (cashier_limit IS NULL AND manager_subject <> cashier_subject)
        OR (cashier_limit IS NOT NULL AND cashier_limit > 0 AND amount <= cashier_limit
            AND manager_subject = cashier_subject)
    );
