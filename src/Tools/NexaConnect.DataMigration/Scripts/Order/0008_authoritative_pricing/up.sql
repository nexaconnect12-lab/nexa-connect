ALTER TABLE orders ADD COLUMN pricing_snapshot jsonb NULL,
    ADD COLUMN pricing_fingerprint text NULL,
    ADD COLUMN placement_key text NULL;
ALTER TABLE orders ADD CONSTRAINT ck_orders_pricing_snapshot CHECK (
    (pricing_snapshot IS NULL AND pricing_fingerprint IS NULL AND placement_key IS NULL)
    OR (pricing_snapshot IS NOT NULL AND pricing_fingerprint IS NOT NULL
        AND pricing_snapshot ?& ARRAY['PolicyVersion','TaxPercent','TaxInclusive','ServiceChargePercent','MenuAmount','SubtotalAmount','ServiceChargeAmount','TaxAmount','TotalAmount']
        AND jsonb_typeof(pricing_snapshot->'TotalAmount')='number'
        AND jsonb_typeof(pricing_snapshot->'SubtotalAmount')='number'
        AND jsonb_typeof(pricing_snapshot->'ServiceChargeAmount')='number'
        AND jsonb_typeof(pricing_snapshot->'TaxAmount')='number'
        AND pricing_fingerprint ~ '^[0-9A-F]{64}$'
        AND placement_key IS NOT NULL AND length(placement_key) BETWEEN 1 AND 200
        AND currency='THB'
        AND (pricing_snapshot->>'SubtotalAmount')::numeric=subtotal_amount
        AND (pricing_snapshot->>'ServiceChargeAmount')::numeric=service_charge_amount
        AND (pricing_snapshot->>'TaxAmount')::numeric=tax_amount
        AND (pricing_snapshot->>'TotalAmount')::numeric=total_amount));
CREATE UNIQUE INDEX ux_orders_placement_key ON orders(restaurant_id,placement_key) WHERE placement_key IS NOT NULL;
CREATE FUNCTION protect_order_pricing() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.pricing_snapshot IS NOT NULL AND
        (NEW.pricing_snapshot IS DISTINCT FROM OLD.pricing_snapshot
         OR NEW.pricing_fingerprint IS DISTINCT FROM OLD.pricing_fingerprint
         OR NEW.placement_key IS DISTINCT FROM OLD.placement_key
         OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
         OR NEW.restaurant_id IS DISTINCT FROM OLD.restaurant_id
         OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
         OR NEW.currency IS DISTINCT FROM OLD.currency) THEN
        RAISE EXCEPTION 'Accepted order pricing is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER order_pricing_immutable BEFORE UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION protect_order_pricing();
