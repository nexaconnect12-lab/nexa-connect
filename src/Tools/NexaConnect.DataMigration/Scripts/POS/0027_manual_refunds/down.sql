LOCK TABLE manual_refund_sales IN ACCESS EXCLUSIVE MODE;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM manual_refund_sales) THEN
        RAISE EXCEPTION 'Manual refund history exists. POS 27 is forward-only after use.';
    END IF;
END;
$$;

DROP TRIGGER manual_refund_drawer_close ON cash_sessions;
DROP TRIGGER manual_refund_shift_close ON shifts;
DROP FUNCTION prevent_close_during_manual_refund();
DROP TABLE manual_refund_completions, manual_refund_payouts, manual_refund_approvals, manual_refund_sales;
DROP FUNCTION prevent_manual_refund_history_mutation();
