LOCK TABLE manual_refund_sales IN ACCESS EXCLUSIVE MODE;
LOCK TABLE manual_refund_releases IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM manual_refund_releases) THEN
        RAISE EXCEPTION 'Manual refund releases exist. POS 28 is forward-only after release.';
    END IF;
END;
$$;
DROP TABLE manual_refund_cashier_readiness, manual_refund_releases;
