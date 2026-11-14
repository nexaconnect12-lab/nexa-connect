CREATE TABLE manual_refund_reference_keys (
    organization_id uuid PRIMARY KEY,
    protected_key text NOT NULL
);

CREATE TABLE manual_refund_reference_claims (
    organization_id uuid NOT NULL REFERENCES manual_refund_reference_keys,
    digest bytea NOT NULL CHECK (octet_length(digest)=32),
    operation_id uuid NOT NULL UNIQUE REFERENCES manual_refund_completions DEFERRABLE INITIALLY DEFERRED,
    PRIMARY KEY (organization_id,digest)
);

CREATE INDEX ix_manual_refund_sales_promptpay_organization
    ON manual_refund_sales(organization_id,order_id) WHERE tender='promptpay_manual';

CREATE TRIGGER manual_refund_reference_keys_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_reference_keys
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();
CREATE TRIGGER manual_refund_reference_claims_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_reference_claims
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();

CREATE FUNCTION require_manual_refund_reference_claim() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.protected_transfer_reference IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM manual_refund_reference_claims claim
        JOIN manual_refund_approvals approval ON approval.operation_id=claim.operation_id
        JOIN manual_refund_sales sale ON sale.order_id=approval.order_id AND sale.organization_id=claim.organization_id
        WHERE claim.operation_id=NEW.operation_id AND sale.tender='promptpay_manual'
    ) THEN
        RAISE EXCEPTION 'manual_refund_transfer_claim_required';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER manual_refund_transfer_claim_guard BEFORE INSERT ON manual_refund_completions
    FOR EACH ROW EXECUTE FUNCTION require_manual_refund_reference_claim();
