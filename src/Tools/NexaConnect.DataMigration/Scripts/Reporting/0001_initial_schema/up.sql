CREATE TABLE sales_facts
(
    order_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    terminal_id uuid NULL,
    employee_identity_subject_id text NULL,
    customer_id uuid NULL,
    channel text NOT NULL,
    service_type text NOT NULL,
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    subtotal_amount numeric(19,4) NOT NULL,
    discount_amount numeric(19,4) NOT NULL,
    service_charge_amount numeric(19,4) NOT NULL,
    tax_amount numeric(19,4) NOT NULL,
    total_amount numeric(19,4) NOT NULL,
    order_status text NOT NULL,
    ordered_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    projected_at_utc timestamptz NOT NULL,
    source_event_version bigint NOT NULL CHECK (source_event_version > 0)
);

CREATE INDEX ix_sales_facts_branch_ordered ON sales_facts (organization_id, restaurant_id, branch_id, ordered_at_utc DESC, order_id);
CREATE INDEX ix_sales_facts_channel_ordered ON sales_facts (restaurant_id, channel, ordered_at_utc DESC);

CREATE TABLE item_sales_facts
(
    order_line_id uuid PRIMARY KEY,
    order_id uuid NOT NULL,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    product_name_snapshot text NOT NULL,
    category_id uuid NULL,
    category_name_snapshot text NULL,
    quantity numeric(12,3) NOT NULL,
    gross_amount numeric(19,4) NOT NULL,
    discount_amount numeric(19,4) NOT NULL,
    tax_amount numeric(19,4) NOT NULL,
    net_amount numeric(19,4) NOT NULL,
    ordered_at_utc timestamptz NOT NULL,
    projected_at_utc timestamptz NOT NULL,
    source_event_version bigint NOT NULL CHECK (source_event_version > 0)
);

CREATE INDEX ix_item_sales_facts_branch_ordered ON item_sales_facts (restaurant_id, branch_id, ordered_at_utc DESC, order_line_id);
CREATE INDEX ix_item_sales_facts_product_ordered ON item_sales_facts (restaurant_id, product_id, ordered_at_utc DESC);

CREATE TABLE payment_facts
(
    payment_intent_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    payment_method text NOT NULL,
    provider_code text NULL,
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    paid_amount numeric(19,4) NOT NULL,
    refunded_amount numeric(19,4) NOT NULL DEFAULT 0,
    payment_status text NOT NULL,
    paid_at_utc timestamptz NULL,
    projected_at_utc timestamptz NOT NULL,
    source_event_version bigint NOT NULL CHECK (source_event_version > 0)
);

CREATE INDEX ix_payment_facts_branch_paid ON payment_facts (restaurant_id, branch_id, paid_at_utc DESC, payment_intent_id);
CREATE INDEX ix_payment_facts_order_id ON payment_facts (order_id, payment_intent_id);

CREATE TABLE kitchen_time_facts
(
    kitchen_ticket_item_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    preparation_station_id uuid NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    started_at_utc timestamptz NULL,
    ready_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    queue_seconds integer NULL CHECK (queue_seconds IS NULL OR queue_seconds >= 0),
    preparation_seconds integer NULL CHECK (preparation_seconds IS NULL OR preparation_seconds >= 0),
    total_seconds integer NULL CHECK (total_seconds IS NULL OR total_seconds >= 0),
    final_status text NOT NULL,
    projected_at_utc timestamptz NOT NULL,
    source_event_version bigint NOT NULL CHECK (source_event_version > 0)
);

CREATE INDEX ix_kitchen_time_facts_station_queued
    ON kitchen_time_facts (restaurant_id, branch_id, preparation_station_id, queued_at_utc DESC);

CREATE TABLE shift_cash_facts
(
    shift_id uuid PRIMARY KEY,
    source_event_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    terminal_id uuid NOT NULL,
    employee_identity_subject_id text NOT NULL,
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    opening_amount numeric(19,4) NOT NULL,
    cash_sales_amount numeric(19,4) NOT NULL,
    cash_refunds_amount numeric(19,4) NOT NULL,
    pay_in_amount numeric(19,4) NOT NULL,
    pay_out_amount numeric(19,4) NOT NULL,
    expected_closing_amount numeric(19,4) NOT NULL,
    actual_closing_amount numeric(19,4) NOT NULL,
    variance_amount numeric(19,4) NOT NULL,
    opened_at_utc timestamptz NOT NULL,
    closed_at_utc timestamptz NOT NULL,
    projected_at_utc timestamptz NOT NULL,
    source_event_version bigint NOT NULL CHECK (source_event_version > 0),
    CONSTRAINT ck_shift_cash_facts_period CHECK (closed_at_utc >= opened_at_utc)
);

CREATE INDEX ix_shift_cash_facts_branch_closed ON shift_cash_facts (restaurant_id, branch_id, closed_at_utc DESC, shift_id);

CREATE TABLE projection_checkpoints
(
    projector_name text NOT NULL,
    source_stream text NOT NULL,
    position bigint NOT NULL CHECK (position >= 0),
    last_event_id uuid NULL,
    last_event_occurred_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_projection_checkpoints PRIMARY KEY (projector_name, source_stream),
    CONSTRAINT ck_projection_checkpoints_names CHECK (char_length(btrim(projector_name)) > 0 AND char_length(btrim(source_stream)) > 0)
);

CREATE TABLE processed_messages
(
    message_id uuid NOT NULL,
    consumer_name text NOT NULL,
    processed_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_processed_messages PRIMARY KEY (message_id, consumer_name)
);

CREATE INDEX ix_processed_messages_processed_at_utc ON processed_messages (processed_at_utc);

COMMENT ON TABLE sales_facts IS 'Rebuildable event projection; never writes back to operational service databases.';
COMMENT ON TABLE projection_checkpoints IS 'Report APIs must expose freshness derived from these projector checkpoints.';
