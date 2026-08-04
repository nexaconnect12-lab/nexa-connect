CREATE TABLE warehouses
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    warehouse_type text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_warehouses_restaurant_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_warehouses_restaurant_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_warehouses_code CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_warehouses_name CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_warehouses_type CHECK (warehouse_type IN ('main', 'branch', 'kitchen', 'bar', 'other')),
    CONSTRAINT ck_warehouses_status CHECK (status IN ('active', 'inactive', 'closed')),
    CONSTRAINT ck_warehouses_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_warehouses_branch_status ON warehouses (restaurant_id, branch_id, status, code);

CREATE TABLE stock_items
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    unit_of_measure text NOT NULL,
    on_hand_quantity numeric(19,4) NOT NULL DEFAULT 0,
    reserved_quantity numeric(19,4) NOT NULL DEFAULT 0 CHECK (reserved_quantity >= 0),
    available_quantity numeric(19,4) GENERATED ALWAYS AS (on_hand_quantity - reserved_quantity) STORED,
    reorder_level numeric(19,4) NULL CHECK (reorder_level IS NULL OR reorder_level >= 0),
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT fk_stock_items_warehouses_restaurant_warehouse
        FOREIGN KEY (restaurant_id, warehouse_id) REFERENCES warehouses (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_stock_items_warehouse_id UNIQUE (warehouse_id, id),
    CONSTRAINT ck_stock_items_unit CHECK (char_length(btrim(unit_of_measure)) > 0)
);

CREATE UNIQUE INDEX uq_stock_items_base_product
    ON stock_items (restaurant_id, warehouse_id, product_id) WHERE product_variant_id IS NULL;
CREATE UNIQUE INDEX uq_stock_items_product_variant
    ON stock_items (restaurant_id, warehouse_id, product_id, product_variant_id) WHERE product_variant_id IS NOT NULL;
CREATE INDEX ix_stock_items_warehouse_available ON stock_items (warehouse_id, available_quantity, product_id);

CREATE TABLE stock_movements
(
    id uuid PRIMARY KEY,
    stock_item_id uuid NOT NULL,
    movement_type text NOT NULL,
    quantity_delta numeric(19,4) NOT NULL CHECK (quantity_delta <> 0),
    balance_after numeric(19,4) NOT NULL,
    reference_type text NULL,
    reference_id uuid NULL,
    reason_code text NULL,
    occurred_at_utc timestamptz NOT NULL,
    recorded_by text NOT NULL,
    CONSTRAINT fk_stock_movements_stock_items_stock_item_id
        FOREIGN KEY (stock_item_id) REFERENCES stock_items (id) ON DELETE RESTRICT,
    CONSTRAINT ck_stock_movements_type
        CHECK (movement_type IN ('receipt', 'sale', 'return', 'transfer_in', 'transfer_out', 'adjustment', 'waste'))
);

CREATE INDEX ix_stock_movements_item_occurred ON stock_movements (stock_item_id, occurred_at_utc, id);
CREATE INDEX ix_stock_movements_reference ON stock_movements (reference_type, reference_id) WHERE reference_id IS NOT NULL;

CREATE TABLE stock_reservations
(
    id uuid PRIMARY KEY,
    stock_item_id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    quantity numeric(19,4) NOT NULL CHECK (quantity > 0),
    status text NOT NULL CHECK (status IN ('active', 'committed', 'released', 'expired')),
    reserved_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_stock_reservations_item_order_line UNIQUE (stock_item_id, order_line_id),
    CONSTRAINT fk_stock_reservations_stock_items_stock_item_id
        FOREIGN KEY (stock_item_id) REFERENCES stock_items (id) ON DELETE RESTRICT,
    CONSTRAINT ck_stock_reservations_expiry CHECK (expires_at_utc > reserved_at_utc)
);

CREATE INDEX ix_stock_reservations_active_expiry
    ON stock_reservations (expires_at_utc, stock_item_id) WHERE status = 'active';

CREATE TABLE replenishment_requests
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    stock_item_id uuid NOT NULL,
    requested_quantity numeric(19,4) NOT NULL CHECK (requested_quantity > 0),
    fulfilled_quantity numeric(19,4) NOT NULL DEFAULT 0 CHECK (fulfilled_quantity >= 0),
    status text NOT NULL CHECK (status IN ('requested', 'approved', 'in_progress', 'fulfilled', 'cancelled')),
    requested_at_utc timestamptz NOT NULL,
    requested_by text NOT NULL,
    completed_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT fk_replenishment_requests_warehouses_restaurant_warehouse
        FOREIGN KEY (restaurant_id, warehouse_id) REFERENCES warehouses (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_replenishment_requests_stock_items_warehouse_stock_item
        FOREIGN KEY (warehouse_id, stock_item_id) REFERENCES stock_items (warehouse_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_replenishment_fulfilled CHECK (fulfilled_quantity <= requested_quantity)
);

CREATE INDEX ix_replenishment_requests_warehouse_status
    ON replenishment_requests (warehouse_id, status, requested_at_utc, id);

CREATE TABLE processed_messages
(
    message_id uuid NOT NULL, consumer_name text NOT NULL, processed_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_processed_messages PRIMARY KEY (message_id, consumer_name)
);

CREATE TABLE outbox_messages
(
    id uuid PRIMARY KEY, event_type text NOT NULL, contract_version integer NOT NULL CHECK (contract_version > 0),
    aggregate_type text NOT NULL, aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'), correlation_id text NULL, causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL, published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0 CHECK (retry_count >= 0), next_attempt_at_utc timestamptz NULL, last_error_category text NULL,
    CONSTRAINT ck_outbox_messages_published CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);
CREATE INDEX ix_outbox_messages_unpublished ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id) WHERE published_at_utc IS NULL;

COMMENT ON COLUMN warehouses.branch_id IS 'External Restaurant Management branch identifier; no cross-database foreign key.';
COMMENT ON COLUMN stock_items.product_id IS 'External Catalog product identifier; no cross-database foreign key.';
COMMENT ON TABLE stock_movements IS 'Append-only inventory ledger; stock_items is the optimistic-concurrency balance projection.';
