ALTER TABLE inventory_stock ADD COLUMN organization_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE inventory_stock ALTER COLUMN organization_id DROP DEFAULT;
ALTER TABLE inventory_stock DROP CONSTRAINT pk_inventory_stock;
ALTER TABLE inventory_stock ADD CONSTRAINT pk_inventory_stock PRIMARY KEY (organization_id, branch_id, product_id);

ALTER TABLE inventory_reservation_lines ADD COLUMN organization_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE inventory_reservation_lines ALTER COLUMN organization_id DROP DEFAULT;
ALTER TABLE inventory_reservation_lines DROP CONSTRAINT pk_inventory_reservation_lines;
ALTER TABLE inventory_reservation_lines ADD CONSTRAINT pk_inventory_reservation_lines PRIMARY KEY (organization_id, order_id, product_id);

CREATE INDEX ix_inventory_reservations_organization_order_active
    ON inventory_reservation_lines (organization_id, order_id, branch_id) WHERE released_at_utc IS NULL;
COMMENT ON COLUMN inventory_stock.organization_id IS 'External Platform Directory identifier required in customer-tenant queries.';
COMMENT ON COLUMN inventory_reservation_lines.organization_id IS 'External Platform Directory identifier required in customer-tenant queries.';
