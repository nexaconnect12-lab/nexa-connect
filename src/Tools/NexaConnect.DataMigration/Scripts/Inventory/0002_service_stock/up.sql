CREATE TABLE inventory_stock
(
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    available_quantity numeric(19,4) NOT NULL CHECK (available_quantity >= 0),
    CONSTRAINT pk_inventory_stock PRIMARY KEY (branch_id, product_id)
);
