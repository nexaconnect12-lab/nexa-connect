CREATE TABLE kitchen_tickets
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    preparation_station_id uuid NOT NULL,
    ticket_number text NOT NULL,
    service_sequence integer NOT NULL DEFAULT 1,
    priority integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    started_at_utc timestamptz NULL,
    ready_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    cancelled_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_kitchen_tickets PRIMARY KEY (id),
    CONSTRAINT uq_kitchen_tickets_branch_ticket_number
        UNIQUE (restaurant_id, branch_id, ticket_number),
    CONSTRAINT uq_kitchen_tickets_order_station_sequence
        UNIQUE (order_id, preparation_station_id, service_sequence),
    CONSTRAINT ck_kitchen_tickets_ticket_number CHECK (char_length(btrim(ticket_number)) > 0),
    CONSTRAINT ck_kitchen_tickets_sequence CHECK (service_sequence > 0),
    CONSTRAINT ck_kitchen_tickets_priority CHECK (priority >= 0),
    CONSTRAINT ck_kitchen_tickets_status
        CHECK (status IN ('queued', 'in_progress', 'ready', 'completed', 'cancelled')),
    CONSTRAINT ck_kitchen_tickets_audit_timestamps CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_kitchen_tickets_concurrency_version CHECK (concurrency_version > 0)
);

CREATE INDEX ix_kitchen_tickets_branch_station_status_queue
    ON kitchen_tickets (restaurant_id, branch_id, preparation_station_id, status, priority DESC, queued_at_utc, id);
CREATE INDEX ix_kitchen_tickets_order_id ON kitchen_tickets (order_id, id);

CREATE TABLE kitchen_ticket_items
(
    id uuid NOT NULL,
    kitchen_ticket_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    product_id uuid NOT NULL,
    product_variant_id uuid NULL,
    item_name_snapshot text NOT NULL,
    variant_name_snapshot text NULL,
    modifiers_snapshot jsonb NOT NULL DEFAULT '[]'::jsonb,
    quantity numeric(12,3) NOT NULL,
    notes text NULL,
    status text NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    started_at_utc timestamptz NULL,
    ready_at_utc timestamptz NULL,
    completed_at_utc timestamptz NULL,
    cancelled_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_kitchen_ticket_items PRIMARY KEY (id),
    CONSTRAINT uq_kitchen_ticket_items_ticket_order_line UNIQUE (kitchen_ticket_id, order_line_id),
    CONSTRAINT uq_kitchen_ticket_items_ticket_id UNIQUE (kitchen_ticket_id, id),
    CONSTRAINT fk_kitchen_ticket_items_kitchen_tickets_kitchen_ticket_id
        FOREIGN KEY (kitchen_ticket_id) REFERENCES kitchen_tickets (id) ON DELETE RESTRICT,
    CONSTRAINT ck_kitchen_ticket_items_name CHECK (char_length(btrim(item_name_snapshot)) > 0),
    CONSTRAINT ck_kitchen_ticket_items_modifiers CHECK (jsonb_typeof(modifiers_snapshot) = 'array'),
    CONSTRAINT ck_kitchen_ticket_items_quantity CHECK (quantity > 0),
    CONSTRAINT ck_kitchen_ticket_items_status
        CHECK (status IN ('queued', 'in_progress', 'ready', 'completed', 'cancelled')),
    CONSTRAINT ck_kitchen_ticket_items_concurrency_version CHECK (concurrency_version > 0)
);

CREATE INDEX ix_kitchen_ticket_items_ticket_status
    ON kitchen_ticket_items (kitchen_ticket_id, status, queued_at_utc, id);

CREATE TABLE kitchen_status_history
(
    id uuid NOT NULL,
    kitchen_ticket_id uuid NOT NULL,
    kitchen_ticket_item_id uuid NULL,
    entity_type text NOT NULL,
    from_status text NULL,
    to_status text NOT NULL,
    reason_code text NULL,
    changed_at_utc timestamptz NOT NULL,
    changed_by text NOT NULL,
    CONSTRAINT pk_kitchen_status_history PRIMARY KEY (id),
    CONSTRAINT fk_kitchen_status_history_kitchen_tickets_kitchen_ticket_id
        FOREIGN KEY (kitchen_ticket_id) REFERENCES kitchen_tickets (id) ON DELETE RESTRICT,
    CONSTRAINT fk_kitchen_status_history_kitchen_ticket_items_ticket_item
        FOREIGN KEY (kitchen_ticket_id, kitchen_ticket_item_id)
        REFERENCES kitchen_ticket_items (kitchen_ticket_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_kitchen_status_history_entity
        CHECK ((entity_type = 'ticket' AND kitchen_ticket_item_id IS NULL) OR (entity_type = 'item' AND kitchen_ticket_item_id IS NOT NULL)),
    CONSTRAINT ck_kitchen_status_history_to_status
        CHECK (to_status IN ('queued', 'in_progress', 'ready', 'completed', 'cancelled'))
);

CREATE INDEX ix_kitchen_status_history_ticket_changed
    ON kitchen_status_history (kitchen_ticket_id, changed_at_utc, id);

CREATE TABLE kitchen_adjustments
(
    id uuid NOT NULL,
    source_message_id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_line_id uuid NULL,
    adjustment_type text NOT NULL,
    quantity_delta numeric(12,3) NULL,
    instructions jsonb NOT NULL DEFAULT '{}'::jsonb,
    received_at_utc timestamptz NOT NULL,
    applied_at_utc timestamptz NULL,
    status text NOT NULL,
    CONSTRAINT pk_kitchen_adjustments PRIMARY KEY (id),
    CONSTRAINT uq_kitchen_adjustments_source_message_id UNIQUE (source_message_id),
    CONSTRAINT ck_kitchen_adjustments_type CHECK (adjustment_type IN ('add', 'quantity_change', 'cancel', 'void')),
    CONSTRAINT ck_kitchen_adjustments_instructions CHECK (jsonb_typeof(instructions) = 'object'),
    CONSTRAINT ck_kitchen_adjustments_status CHECK (status IN ('received', 'applied', 'rejected'))
);

CREATE INDEX ix_kitchen_adjustments_order_status ON kitchen_adjustments (order_id, status, received_at_utc);

CREATE TABLE processed_messages
(
    message_id uuid NOT NULL,
    consumer_name text NOT NULL,
    processed_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_processed_messages PRIMARY KEY (message_id, consumer_name),
    CONSTRAINT ck_processed_messages_consumer_name CHECK (char_length(btrim(consumer_name)) > 0)
);

CREATE INDEX ix_processed_messages_processed_at_utc ON processed_messages (processed_at_utc);

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

COMMENT ON COLUMN kitchen_tickets.order_id IS 'External Order service identifier; no cross-database foreign key.';
COMMENT ON COLUMN kitchen_tickets.preparation_station_id IS 'External Restaurant Management station identifier.';
COMMENT ON TABLE kitchen_ticket_items IS 'Preparation snapshots only; Kitchen never recalculates commercial totals.';
