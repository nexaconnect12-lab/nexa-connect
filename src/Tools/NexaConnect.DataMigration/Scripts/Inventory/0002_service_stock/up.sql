CREATE TABLE inventory_stock
(
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    available_quantity numeric(19,4) NOT NULL CHECK (available_quantity >= 0),
    CONSTRAINT pk_inventory_stock PRIMARY KEY (branch_id, product_id)
);

CREATE TABLE inventory_reservation_lines
(
    order_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity numeric(19,4) NOT NULL CHECK (quantity > 0),
    released_at_utc timestamptz NULL,
    CONSTRAINT pk_inventory_reservation_lines PRIMARY KEY (order_id, product_id)
);
