CREATE TABLE orders
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    customer_id uuid NULL,
    order_number text NOT NULL,
    currency char(3) NOT NULL,
    channel text NOT NULL,
    service_type text NOT NULL,
    guest_count smallint NULL,
    subtotal_amount numeric(19,4) NOT NULL,
    discount_amount numeric(19,4) NOT NULL DEFAULT 0,
    service_charge_amount numeric(19,4) NOT NULL DEFAULT 0,
    tax_amount numeric(19,4) NOT NULL DEFAULT 0,
    total_amount numeric(19,4) NOT NULL,
    status text NOT NULL,
    submitted_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    cancelled_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_orders PRIMARY KEY (id),
    CONSTRAINT uq_orders_restaurant_id_branch_id_order_number
        UNIQUE (restaurant_id, branch_id, order_number),
    CONSTRAINT uq_orders_restaurant_id_branch_id_id
        UNIQUE (restaurant_id, branch_id, id),
    CONSTRAINT ck_orders_order_number CHECK (char_length(btrim(order_number)) > 0),
    CONSTRAINT ck_orders_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_orders_channel
        CHECK (channel IN ('pos', 'waiter', 'kiosk', 'qr', 'web', 'mobile')),
    CONSTRAINT ck_orders_service_type
        CHECK (service_type IN ('dine_in', 'takeaway', 'delivery')),
    CONSTRAINT ck_orders_guest_count CHECK (guest_count IS NULL OR guest_count > 0),
    CONSTRAINT ck_orders_amounts
        CHECK
        (
            subtotal_amount >= 0 AND discount_amount >= 0
            AND service_charge_amount >= 0 AND tax_amount >= 0 AND total_amount >= 0
            AND total_amount = subtotal_amount - discount_amount + service_charge_amount + tax_amount
        ),
    CONSTRAINT ck_orders_status
        CHECK (status IN ('draft', 'submitted', 'accepted', 'preparing', 'ready', 'completed', 'cancelled')),
    CONSTRAINT ck_orders_audit_timestamps CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_orders_concurrency_version CHECK (concurrency_version > 0)
);

CREATE INDEX ix_orders_branch_status_created
    ON orders (restaurant_id, branch_id, status, created_at_utc DESC, id);
CREATE INDEX ix_orders_customer_created
    ON orders (restaurant_id, customer_id, created_at_utc DESC, id)
    WHERE customer_id IS NOT NULL;

CREATE TABLE order_lines
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    line_number integer NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    sku_snapshot text NOT NULL,
    name_snapshot text NOT NULL,
    variant_name_snapshot text NULL,
    quantity numeric(12,3) NOT NULL,
    unit_price numeric(19,4) NOT NULL,
    discount_amount numeric(19,4) NOT NULL DEFAULT 0,
    tax_amount numeric(19,4) NOT NULL DEFAULT 0,
    line_total numeric(19,4) NOT NULL,
    notes text NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_order_lines PRIMARY KEY (id),
    CONSTRAINT uq_order_lines_order_id_line_number UNIQUE (order_id, line_number),
    CONSTRAINT uq_order_lines_order_id_id UNIQUE (order_id, id),
    CONSTRAINT fk_order_lines_orders_restaurant_branch_order
        FOREIGN KEY (restaurant_id, branch_id, order_id)
        REFERENCES orders (restaurant_id, branch_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_order_lines_line_number CHECK (line_number > 0),
    CONSTRAINT ck_order_lines_snapshots
        CHECK (char_length(btrim(sku_snapshot)) > 0 AND char_length(btrim(name_snapshot)) > 0),
    CONSTRAINT ck_order_lines_amounts
        CHECK (quantity > 0 AND unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND line_total >= 0),
    CONSTRAINT ck_order_lines_status
        CHECK (status IN ('active', 'voided', 'cancelled', 'returned')),
    CONSTRAINT ck_order_lines_audit_timestamps CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_order_lines_concurrency_version CHECK (concurrency_version > 0)
);

CREATE INDEX ix_order_lines_order_id_status ON order_lines (order_id, status, line_number);

CREATE TABLE order_line_modifiers
(
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    modifier_group_id uuid NOT NULL,
    modifier_option_id uuid NOT NULL,
    group_name_snapshot text NOT NULL,
    option_name_snapshot text NOT NULL,
    quantity numeric(12,3) NOT NULL,
    unit_price numeric(19,4) NOT NULL,
    total_amount numeric(19,4) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_order_line_modifiers PRIMARY KEY (id),
    CONSTRAINT fk_order_line_modifiers_order_lines_order_id_order_line_id
        FOREIGN KEY (order_id, order_line_id)
        REFERENCES order_lines (order_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_order_line_modifiers_names
        CHECK (char_length(btrim(group_name_snapshot)) > 0 AND char_length(btrim(option_name_snapshot)) > 0),
    CONSTRAINT ck_order_line_modifiers_amounts
        CHECK (quantity > 0 AND unit_price >= 0 AND total_amount >= 0)
);

CREATE INDEX ix_order_line_modifiers_order_line_id ON order_line_modifiers (order_line_id, id);

CREATE TABLE order_status_history
(
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    from_status text NULL,
    to_status text NOT NULL,
    reason_code text NULL,
    notes text NULL,
    changed_at_utc timestamptz NOT NULL,
    changed_by text NOT NULL,
    CONSTRAINT pk_order_status_history PRIMARY KEY (id),
    CONSTRAINT fk_order_status_history_orders_order_id
        FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE RESTRICT,
    CONSTRAINT ck_order_status_history_to_status
        CHECK (to_status IN ('draft', 'submitted', 'accepted', 'preparing', 'ready', 'completed', 'cancelled'))
);

CREATE INDEX ix_order_status_history_order_id_changed
    ON order_status_history (order_id, changed_at_utc, id);

CREATE TABLE order_channel_contexts
(
    order_id uuid NOT NULL,
    terminal_id uuid NULL,
    device_id uuid NULL,
    dining_table_id uuid NULL,
    employee_identity_subject_id text NULL,
    client_operation_id uuid NOT NULL,
    collection_number text NULL,
    context jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT pk_order_channel_contexts PRIMARY KEY (order_id),
    CONSTRAINT fk_order_channel_contexts_orders_order_id
        FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE RESTRICT,
    CONSTRAINT uq_order_channel_contexts_client_operation_id UNIQUE (client_operation_id),
    CONSTRAINT ck_order_channel_contexts_context CHECK (jsonb_typeof(context) = 'object')
);

CREATE TABLE returns
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    return_number text NOT NULL,
    reason_code text NOT NULL,
    total_amount numeric(19,4) NOT NULL,
    status text NOT NULL,
    authorized_by text NULL,
    authorized_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_returns PRIMARY KEY (id),
    CONSTRAINT uq_returns_restaurant_id_branch_id_return_number
        UNIQUE (restaurant_id, branch_id, return_number),
    CONSTRAINT uq_returns_order_id_id UNIQUE (order_id, id),
    CONSTRAINT fk_returns_orders_restaurant_branch_order
        FOREIGN KEY (restaurant_id, branch_id, order_id)
        REFERENCES orders (restaurant_id, branch_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_returns_return_number CHECK (char_length(btrim(return_number)) > 0),
    CONSTRAINT ck_returns_reason_code CHECK (char_length(btrim(reason_code)) > 0),
    CONSTRAINT ck_returns_total_amount CHECK (total_amount >= 0),
    CONSTRAINT ck_returns_status
        CHECK (status IN ('requested', 'authorized', 'rejected', 'completed', 'cancelled')),
    CONSTRAINT ck_returns_audit_timestamps CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_returns_concurrency_version CHECK (concurrency_version > 0)
);

CREATE INDEX ix_returns_order_id_status ON returns (order_id, status, created_at_utc DESC);

CREATE TABLE return_lines
(
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    return_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    quantity numeric(12,3) NOT NULL,
    amount numeric(19,4) NOT NULL,
    reason_code text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT pk_return_lines PRIMARY KEY (id),
    CONSTRAINT fk_return_lines_returns_order_id_return_id
        FOREIGN KEY (order_id, return_id) REFERENCES returns (order_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_return_lines_order_lines_order_id_order_line_id
        FOREIGN KEY (order_id, order_line_id) REFERENCES order_lines (order_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_return_lines_quantity CHECK (quantity > 0),
    CONSTRAINT ck_return_lines_amount CHECK (amount >= 0),
    CONSTRAINT ck_return_lines_reason_code CHECK (char_length(btrim(reason_code)) > 0)
);

CREATE INDEX ix_return_lines_return_id ON return_lines (return_id, id);

CREATE TABLE idempotency_records
(
    operation_scope text NOT NULL,
    idempotency_key text NOT NULL,
    request_hash text NOT NULL,
    response_status integer NULL,
    response_body jsonb NULL,
    resource_id uuid NULL,
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_idempotency_records PRIMARY KEY (operation_scope, idempotency_key),
    CONSTRAINT ck_idempotency_records_values
        CHECK (char_length(btrim(operation_scope)) > 0 AND char_length(btrim(idempotency_key)) > 0 AND char_length(btrim(request_hash)) > 0),
    CONSTRAINT ck_idempotency_records_expiry CHECK (expires_at_utc > created_at_utc)
);

CREATE INDEX ix_idempotency_records_expires_at_utc ON idempotency_records (expires_at_utc);

CREATE TABLE outbox_messages
(
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    contract_version integer NOT NULL CHECK (contract_version > 0),
    aggregate_type text NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    correlation_id text NULL,
    causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL,
    published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0 CHECK (retry_count >= 0),
    next_attempt_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT ck_outbox_messages_published CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);

CREATE INDEX ix_outbox_messages_unpublished
    ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id) WHERE published_at_utc IS NULL;

COMMENT ON COLUMN orders.restaurant_id IS 'External Restaurant Management identifier; no cross-database foreign key.';
COMMENT ON COLUMN orders.customer_id IS 'External Customer service identifier; order history remains valid without a live customer record.';
COMMENT ON TABLE order_lines IS 'Immutable commercial snapshots are retained independently of later Catalog changes.';
COMMENT ON TABLE order_channel_contexts IS 'Contains channel context only; QR secrets and device credentials are prohibited.';
