DROP INDEX ix_inventory_reservations_organization_order_active;
ALTER TABLE inventory_reservation_lines DROP CONSTRAINT pk_inventory_reservation_lines;
ALTER TABLE inventory_reservation_lines ADD CONSTRAINT pk_inventory_reservation_lines PRIMARY KEY (order_id, product_id);
ALTER TABLE inventory_reservation_lines DROP COLUMN organization_id;
ALTER TABLE inventory_stock DROP CONSTRAINT pk_inventory_stock;
ALTER TABLE inventory_stock ADD CONSTRAINT pk_inventory_stock PRIMARY KEY (branch_id, product_id);
ALTER TABLE inventory_stock DROP COLUMN organization_id;
