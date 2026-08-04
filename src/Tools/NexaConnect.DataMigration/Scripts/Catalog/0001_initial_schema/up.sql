CREATE TABLE tax_classifications
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_tax_classifications PRIMARY KEY (id),
    CONSTRAINT uq_tax_classifications_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_tax_classifications_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_tax_classifications_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_tax_classifications_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_tax_classifications_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_tax_classifications_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_tax_classifications_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE TABLE categories
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    parent_category_id uuid NULL,
    code text NOT NULL,
    name text NOT NULL,
    description text NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_categories PRIMARY KEY (id),
    CONSTRAINT uq_categories_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_categories_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT fk_categories_categories_restaurant_id_parent_category_id
        FOREIGN KEY (restaurant_id, parent_category_id)
        REFERENCES categories (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_categories_not_own_parent
        CHECK (parent_category_id IS NULL OR parent_category_id <> id),
    CONSTRAINT ck_categories_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_categories_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_categories_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_categories_status
        CHECK (status IN ('active', 'inactive', 'archived')),
    CONSTRAINT ck_categories_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_categories_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_categories_restaurant_id_parent_category_id_status
    ON categories (restaurant_id, parent_category_id, status, display_order);

CREATE TABLE products
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    tax_classification_id uuid NULL,
    sku text NOT NULL,
    name text NOT NULL,
    description text NULL,
    base_unit text NOT NULL DEFAULT 'each',
    attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_products PRIMARY KEY (id),
    CONSTRAINT uq_products_restaurant_id_sku UNIQUE (restaurant_id, sku),
    CONSTRAINT uq_products_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT fk_products_tax_classifications_tax_id
        FOREIGN KEY (restaurant_id, tax_classification_id)
        REFERENCES tax_classifications (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_products_sku
        CHECK (char_length(btrim(sku)) > 0),
    CONSTRAINT ck_products_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_products_base_unit
        CHECK (char_length(btrim(base_unit)) > 0),
    CONSTRAINT ck_products_attributes
        CHECK (jsonb_typeof(attributes) = 'object'),
    CONSTRAINT ck_products_status
        CHECK (status IN ('draft', 'active', 'inactive', 'archived')),
    CONSTRAINT ck_products_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_products_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_products_restaurant_id_status_name
    ON products (restaurant_id, status, name, id);

CREATE TABLE product_variants
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_product_variants PRIMARY KEY (id),
    CONSTRAINT uq_product_variants_restaurant_id_product_id_code
        UNIQUE (restaurant_id, product_id, code),
    CONSTRAINT uq_product_variants_restaurant_id_product_id_id
        UNIQUE (restaurant_id, product_id, id),
    CONSTRAINT fk_product_variants_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_variants_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_product_variants_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_product_variants_attributes
        CHECK (jsonb_typeof(attributes) = 'object'),
    CONSTRAINT ck_product_variants_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_product_variants_status
        CHECK (status IN ('active', 'inactive', 'archived')),
    CONSTRAINT ck_product_variants_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_product_variants_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_product_variants_restaurant_id_product_id_status
    ON product_variants (restaurant_id, product_id, status, display_order);

CREATE TABLE product_categories
(
    restaurant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    category_id uuid NOT NULL,
    display_order integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_product_categories PRIMARY KEY (restaurant_id, product_id, category_id),
    CONSTRAINT fk_product_categories_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_categories_categories_restaurant_id_category_id
        FOREIGN KEY (restaurant_id, category_id)
        REFERENCES categories (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_categories_display_order
        CHECK (display_order >= 0)
);

CREATE INDEX ix_product_categories_restaurant_id_category_id_display_order
    ON product_categories (restaurant_id, category_id, display_order, product_id);

CREATE TABLE product_barcodes
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    barcode_value text NOT NULL,
    barcode_type text NOT NULL,
    is_primary boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_product_barcodes PRIMARY KEY (id),
    CONSTRAINT uq_product_barcodes_restaurant_id_barcode_value
        UNIQUE (restaurant_id, barcode_value),
    CONSTRAINT fk_product_barcodes_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_barcodes_product_variants_restaurant_product_variant
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_barcodes_barcode_value
        CHECK (char_length(btrim(barcode_value)) > 0),
    CONSTRAINT ck_product_barcodes_barcode_type
        CHECK (barcode_type IN ('ean13', 'ean8', 'upca', 'upce', 'code128', 'qr', 'other'))
);

CREATE UNIQUE INDEX uq_product_barcodes_primary_product
    ON product_barcodes (restaurant_id, product_id)
    WHERE is_primary AND product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_product_barcodes_primary_variant
    ON product_barcodes (restaurant_id, product_id, product_variant_id)
    WHERE is_primary AND product_variant_id IS NOT NULL;

CREATE TABLE menus
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NULL,
    code text NOT NULL,
    name text NOT NULL,
    scope_type text NOT NULL,
    valid_from_utc timestamptz NULL,
    valid_to_utc timestamptz NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_menus PRIMARY KEY (id),
    CONSTRAINT uq_menus_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_menus_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_menus_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_menus_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_menus_scope
        CHECK
        (
            (scope_type = 'restaurant' AND branch_id IS NULL)
            OR
            (scope_type = 'branch' AND branch_id IS NOT NULL)
        ),
    CONSTRAINT ck_menus_validity
        CHECK (valid_to_utc IS NULL OR valid_from_utc IS NULL OR valid_to_utc > valid_from_utc),
    CONSTRAINT ck_menus_status
        CHECK (status IN ('draft', 'active', 'inactive', 'archived')),
    CONSTRAINT ck_menus_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_menus_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_menus_restaurant_id_branch_id_status
    ON menus (restaurant_id, branch_id, status, id);

CREATE TABLE menu_channels
(
    restaurant_id uuid NOT NULL,
    menu_id uuid NOT NULL,
    channel text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_menu_channels PRIMARY KEY (restaurant_id, menu_id, channel),
    CONSTRAINT fk_menu_channels_menus_restaurant_id_menu_id
        FOREIGN KEY (restaurant_id, menu_id)
        REFERENCES menus (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_menu_channels_channel
        CHECK (channel IN ('pos', 'waiter', 'kiosk', 'qr', 'web', 'mobile'))
);

CREATE TABLE menu_categories
(
    restaurant_id uuid NOT NULL,
    menu_id uuid NOT NULL,
    category_id uuid NOT NULL,
    display_order integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_menu_categories PRIMARY KEY (restaurant_id, menu_id, category_id),
    CONSTRAINT fk_menu_categories_menus_restaurant_id_menu_id
        FOREIGN KEY (restaurant_id, menu_id)
        REFERENCES menus (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_menu_categories_categories_restaurant_id_category_id
        FOREIGN KEY (restaurant_id, category_id)
        REFERENCES categories (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_menu_categories_display_order
        CHECK (display_order >= 0)
);

CREATE INDEX ix_menu_categories_restaurant_id_menu_id_display_order
    ON menu_categories (restaurant_id, menu_id, display_order, category_id);

CREATE TABLE menu_items
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    menu_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_menu_items PRIMARY KEY (id),
    CONSTRAINT fk_menu_items_menus_restaurant_id_menu_id
        FOREIGN KEY (restaurant_id, menu_id)
        REFERENCES menus (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_menu_items_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_menu_items_product_variants_restaurant_product_variant
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_menu_items_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_menu_items_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_menu_items_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_menu_items_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE UNIQUE INDEX uq_menu_items_base_product
    ON menu_items (restaurant_id, menu_id, product_id)
    WHERE product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_menu_items_product_variant
    ON menu_items (restaurant_id, menu_id, product_id, product_variant_id)
    WHERE product_variant_id IS NOT NULL;

CREATE INDEX ix_menu_items_restaurant_id_menu_id_status_display_order
    ON menu_items (restaurant_id, menu_id, status, display_order, id);

CREATE TABLE modifier_groups
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    minimum_selections smallint NOT NULL DEFAULT 0,
    maximum_selections smallint NOT NULL DEFAULT 1,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_modifier_groups PRIMARY KEY (id),
    CONSTRAINT uq_modifier_groups_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_modifier_groups_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_modifier_groups_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_modifier_groups_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_modifier_groups_selection_limits
        CHECK
        (
            minimum_selections >= 0
            AND maximum_selections > 0
            AND maximum_selections >= minimum_selections
        ),
    CONSTRAINT ck_modifier_groups_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_modifier_groups_status
        CHECK (status IN ('active', 'inactive', 'archived')),
    CONSTRAINT ck_modifier_groups_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_modifier_groups_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE TABLE modifier_options
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    modifier_group_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    is_default boolean NOT NULL DEFAULT false,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_modifier_options PRIMARY KEY (id),
    CONSTRAINT uq_modifier_options_restaurant_id_modifier_group_id_code
        UNIQUE (restaurant_id, modifier_group_id, code),
    CONSTRAINT uq_modifier_options_restaurant_id_modifier_group_id_id
        UNIQUE (restaurant_id, modifier_group_id, id),
    CONSTRAINT fk_modifier_options_modifier_groups_group_id
        FOREIGN KEY (restaurant_id, modifier_group_id)
        REFERENCES modifier_groups (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_modifier_options_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_modifier_options_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_modifier_options_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_modifier_options_status
        CHECK (status IN ('active', 'inactive', 'archived')),
    CONSTRAINT ck_modifier_options_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_modifier_options_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_modifier_options_restaurant_id_modifier_group_id_status
    ON modifier_options (restaurant_id, modifier_group_id, status, display_order);

CREATE TABLE product_modifier_groups
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    modifier_group_id uuid NOT NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_product_modifier_groups PRIMARY KEY (id),
    CONSTRAINT fk_product_modifier_groups_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_modifier_groups_variants_variant_id
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_modifier_groups_groups_group_id
        FOREIGN KEY (restaurant_id, modifier_group_id)
        REFERENCES modifier_groups (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_modifier_groups_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_product_modifier_groups_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_product_modifier_groups_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_product_modifier_groups_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE UNIQUE INDEX uq_product_modifier_groups_base_product
    ON product_modifier_groups (restaurant_id, product_id, modifier_group_id)
    WHERE product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_product_modifier_groups_product_variant
    ON product_modifier_groups
        (restaurant_id, product_id, product_variant_id, modifier_group_id)
    WHERE product_variant_id IS NOT NULL;

CREATE TABLE price_lists
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NULL,
    code text NOT NULL,
    name text NOT NULL,
    scope_type text NOT NULL,
    currency char(3) NOT NULL,
    valid_from_utc timestamptz NULL,
    valid_to_utc timestamptz NULL,
    priority integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_price_lists PRIMARY KEY (id),
    CONSTRAINT uq_price_lists_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_price_lists_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_price_lists_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_price_lists_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_price_lists_scope
        CHECK
        (
            (scope_type = 'restaurant' AND branch_id IS NULL)
            OR
            (scope_type = 'branch' AND branch_id IS NOT NULL)
        ),
    CONSTRAINT ck_price_lists_currency
        CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_price_lists_validity
        CHECK (valid_to_utc IS NULL OR valid_from_utc IS NULL OR valid_to_utc > valid_from_utc),
    CONSTRAINT ck_price_lists_priority
        CHECK (priority >= 0),
    CONSTRAINT ck_price_lists_status
        CHECK (status IN ('draft', 'active', 'inactive', 'archived')),
    CONSTRAINT ck_price_lists_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_price_lists_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_price_lists_restaurant_id_branch_id_status_priority
    ON price_lists (restaurant_id, branch_id, status, priority DESC, id);

CREATE TABLE product_prices
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    amount numeric(19,4) NOT NULL,
    effective_from_utc timestamptz NOT NULL,
    effective_to_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_product_prices PRIMARY KEY (id),
    CONSTRAINT fk_product_prices_price_lists_restaurant_id_price_list_id
        FOREIGN KEY (restaurant_id, price_list_id)
        REFERENCES price_lists (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_prices_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_prices_product_variants_restaurant_product_variant
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_prices_amount
        CHECK (amount >= 0),
    CONSTRAINT ck_product_prices_effective_period
        CHECK (effective_to_utc IS NULL OR effective_to_utc > effective_from_utc)
);

CREATE UNIQUE INDEX uq_product_prices_base_product_effective_from
    ON product_prices (restaurant_id, price_list_id, product_id, effective_from_utc)
    WHERE product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_product_prices_product_variant_effective_from
    ON product_prices
        (restaurant_id, price_list_id, product_id, product_variant_id, effective_from_utc)
    WHERE product_variant_id IS NOT NULL;

CREATE INDEX ix_product_prices_effective_lookup
    ON product_prices
        (restaurant_id, price_list_id, product_id, product_variant_id, effective_from_utc DESC);

CREATE TABLE modifier_option_prices
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    modifier_group_id uuid NOT NULL,
    modifier_option_id uuid NOT NULL,
    amount numeric(19,4) NOT NULL,
    effective_from_utc timestamptz NOT NULL,
    effective_to_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_modifier_option_prices PRIMARY KEY (id),
    CONSTRAINT fk_modifier_option_prices_price_lists_list_id
        FOREIGN KEY (restaurant_id, price_list_id)
        REFERENCES price_lists (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_modifier_option_prices_options_option_id
        FOREIGN KEY (restaurant_id, modifier_group_id, modifier_option_id)
        REFERENCES modifier_options (restaurant_id, modifier_group_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_modifier_option_prices_effective_from
        UNIQUE
        (
            restaurant_id,
            price_list_id,
            modifier_group_id,
            modifier_option_id,
            effective_from_utc
        ),
    CONSTRAINT ck_modifier_option_prices_amount
        CHECK (amount >= 0),
    CONSTRAINT ck_modifier_option_prices_effective_period
        CHECK (effective_to_utc IS NULL OR effective_to_utc > effective_from_utc)
);

CREATE INDEX ix_modifier_option_prices_effective_lookup
    ON modifier_option_prices
        (restaurant_id, price_list_id, modifier_option_id, effective_from_utc DESC);

CREATE TABLE product_availability
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    availability_status text NOT NULL,
    reason text NULL,
    available_from_utc timestamptz NULL,
    available_to_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_product_availability PRIMARY KEY (id),
    CONSTRAINT fk_product_availability_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_product_availability_variants_variant_id
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_product_availability_status
        CHECK (availability_status IN ('available', 'unavailable', 'sold_out')),
    CONSTRAINT ck_product_availability_period
        CHECK (available_to_utc IS NULL OR available_from_utc IS NULL OR available_to_utc > available_from_utc),
    CONSTRAINT ck_product_availability_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_product_availability_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE UNIQUE INDEX uq_product_availability_base_product
    ON product_availability (restaurant_id, branch_id, product_id)
    WHERE product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_product_availability_product_variant
    ON product_availability (restaurant_id, branch_id, product_id, product_variant_id)
    WHERE product_variant_id IS NOT NULL;

CREATE INDEX ix_product_availability_branch_status
    ON product_availability (restaurant_id, branch_id, availability_status, product_id);

CREATE TABLE preparation_routes
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    preparation_station_id uuid NOT NULL,
    priority integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_preparation_routes PRIMARY KEY (id),
    CONSTRAINT fk_preparation_routes_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_preparation_routes_variants_variant_id
        FOREIGN KEY (restaurant_id, product_id, product_variant_id)
        REFERENCES product_variants (restaurant_id, product_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_preparation_routes_priority
        CHECK (priority >= 0),
    CONSTRAINT ck_preparation_routes_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_preparation_routes_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_preparation_routes_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE UNIQUE INDEX uq_preparation_routes_base_product_station
    ON preparation_routes
        (restaurant_id, branch_id, product_id, preparation_station_id)
    WHERE product_variant_id IS NULL;

CREATE UNIQUE INDEX uq_preparation_routes_product_variant_station
    ON preparation_routes
        (restaurant_id, branch_id, product_id, product_variant_id, preparation_station_id)
    WHERE product_variant_id IS NOT NULL;

CREATE INDEX ix_preparation_routes_branch_product_status_priority
    ON preparation_routes (restaurant_id, branch_id, product_id, status, priority, id);

CREATE TABLE product_images
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    media_asset_id uuid NOT NULL,
    alt_text text NULL,
    display_order integer NOT NULL DEFAULT 0,
    is_primary boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_product_images PRIMARY KEY (id),
    CONSTRAINT fk_product_images_products_restaurant_id_product_id
        FOREIGN KEY (restaurant_id, product_id)
        REFERENCES products (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_product_images_restaurant_id_product_id_media_asset_id
        UNIQUE (restaurant_id, product_id, media_asset_id),
    CONSTRAINT ck_product_images_display_order
        CHECK (display_order >= 0)
);

CREATE UNIQUE INDEX uq_product_images_primary_product
    ON product_images (restaurant_id, product_id)
    WHERE is_primary;

CREATE INDEX ix_product_images_restaurant_id_product_id_display_order
    ON product_images (restaurant_id, product_id, display_order, id);

CREATE TABLE outbox_messages
(
    id uuid NOT NULL,
    event_type text NOT NULL,
    contract_version integer NOT NULL,
    aggregate_type text NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    correlation_id text NULL,
    causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL,
    published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0,
    next_attempt_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id),
    CONSTRAINT ck_outbox_messages_event_type
        CHECK (char_length(btrim(event_type)) > 0),
    CONSTRAINT ck_outbox_messages_contract_version
        CHECK (contract_version > 0),
    CONSTRAINT ck_outbox_messages_aggregate_type
        CHECK (char_length(btrim(aggregate_type)) > 0),
    CONSTRAINT ck_outbox_messages_payload
        CHECK (jsonb_typeof(payload) = 'object'),
    CONSTRAINT ck_outbox_messages_retry_count
        CHECK (retry_count >= 0),
    CONSTRAINT ck_outbox_messages_publish_timestamp
        CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);

CREATE INDEX ix_outbox_messages_unpublished
    ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id)
    WHERE published_at_utc IS NULL;

COMMENT ON COLUMN products.restaurant_id IS
    'Restaurant Management identifier obtained through an API or versioned event; no cross-database foreign key.';

COMMENT ON COLUMN menus.branch_id IS
    'Restaurant Management branch identifier; no cross-database foreign key.';

COMMENT ON TABLE tax_classifications IS
    'Catalog classification labels only. Tax rates and service-charge policies remain Restaurant-owned.';

COMMENT ON TABLE product_availability IS
    'Menu availability and sold-out state only. Inventory quantities remain Inventory-owned.';

COMMENT ON COLUMN preparation_routes.preparation_station_id IS
    'Restaurant Management preparation-station identifier; no cross-database foreign key.';

COMMENT ON COLUMN product_images.media_asset_id IS
    'Media service asset identifier; image bytes and processing metadata are not stored here.';
