CREATE TABLE catalog_menu_items
(
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    name text NOT NULL,
    unit_price numeric(19,4) NOT NULL CHECK (unit_price >= 0),
    currency char(3) NOT NULL,
    preparation_station text NOT NULL,
    available boolean NOT NULL,
    CONSTRAINT pk_catalog_menu_items PRIMARY KEY (branch_id, product_id)
);
