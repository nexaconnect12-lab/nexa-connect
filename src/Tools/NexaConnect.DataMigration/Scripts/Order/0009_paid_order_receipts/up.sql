ALTER TABLE orders ADD COLUMN receipt_snapshot jsonb NULL;
CREATE FUNCTION protect_order_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.receipt_snapshot IS NOT NULL THEN RAISE EXCEPTION 'Paid receipt history cannot be deleted'; END IF;
        RETURN OLD;
    END IF;
    IF TG_OP = 'UPDATE' AND OLD.receipt_snapshot IS NOT NULL AND
        (NEW.receipt_snapshot IS DISTINCT FROM OLD.receipt_snapshot OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
         OR NEW.restaurant_id IS DISTINCT FROM OLD.restaurant_id OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
         OR NEW.total_amount IS DISTINCT FROM OLD.total_amount OR NEW.currency IS DISTINCT FROM OLD.currency OR NEW.status <> 'completed') THEN
        RAISE EXCEPTION 'Paid receipt history is immutable';
    END IF;
    IF NEW.receipt_snapshot IS NOT NULL AND
        (NEW.status <> 'completed' OR (NEW.receipt_snapshot->>'OrderId')::uuid IS DISTINCT FROM NEW.id
         OR (NEW.receipt_snapshot->>'OrganizationId')::uuid IS DISTINCT FROM NEW.organization_id
         OR (NEW.receipt_snapshot->>'RestaurantId')::uuid IS DISTINCT FROM NEW.restaurant_id
         OR (NEW.receipt_snapshot->>'BranchId')::uuid IS DISTINCT FROM NEW.branch_id
         OR (NEW.receipt_snapshot->>'Version')::integer IS DISTINCT FROM 1
         OR NEW.receipt_snapshot->>'ReceiptNumber' IS DISTINCT FROM 'R-' || upper(replace(NEW.id::text, '-', ''))
         OR NEW.receipt_snapshot->>'OrderNumber' IS DISTINCT FROM NEW.order_number
         OR COALESCE(length(btrim(NEW.receipt_snapshot->>'Tender')), 0) = 0
         OR jsonb_typeof(NEW.receipt_snapshot->'Lines') IS DISTINCT FROM 'array'
         OR jsonb_array_length(NEW.receipt_snapshot->'Lines') = 0
         OR (NEW.receipt_snapshot->>'SubtotalAmount')::numeric IS DISTINCT FROM NEW.subtotal_amount
         OR (NEW.receipt_snapshot->>'ServiceChargeAmount')::numeric IS DISTINCT FROM NEW.service_charge_amount
         OR (NEW.receipt_snapshot->>'TaxAmount')::numeric IS DISTINCT FROM NEW.tax_amount
         OR (NEW.receipt_snapshot->>'TotalAmount')::numeric IS DISTINCT FROM NEW.total_amount
         OR NEW.receipt_snapshot->>'Currency' IS DISTINCT FROM btrim(NEW.currency)) THEN
        RAISE EXCEPTION 'Receipt must match the paid order';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER order_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON orders
FOR EACH ROW EXECUTE FUNCTION protect_order_receipt();
