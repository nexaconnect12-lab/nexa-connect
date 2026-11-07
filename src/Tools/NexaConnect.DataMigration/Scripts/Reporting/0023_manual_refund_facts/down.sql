LOCK TABLE manual_refund_facts IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM manual_refund_facts) THEN
        RAISE EXCEPTION 'Manual refund facts exist. Reporting 23 is forward-only after projection.';
    END IF;
END;
$$;
DROP TABLE manual_refund_facts;
