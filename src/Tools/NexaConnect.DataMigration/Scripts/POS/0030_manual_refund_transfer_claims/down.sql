LOCK TABLE manual_refund_completions, manual_refund_reference_keys, manual_refund_reference_claims IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM manual_refund_reference_keys) THEN
        RAISE EXCEPTION 'Manual refund reference history requires forward recovery';
    END IF;
END $$;
DROP TRIGGER manual_refund_transfer_claim_guard ON manual_refund_completions;
DROP FUNCTION require_manual_refund_reference_claim();
DROP TABLE manual_refund_reference_claims;
DROP TABLE manual_refund_reference_keys;
DROP INDEX ix_manual_refund_sales_promptpay_organization;
