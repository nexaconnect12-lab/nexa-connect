CREATE TABLE stores
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    operational_status text NOT NULL,
    configuration jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(configuration) = 'object'),
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_stores_restaurant_branch UNIQUE (restaurant_id, branch_id),
    CONSTRAINT uq_stores_restaurant_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_stores_restaurant_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_stores_code CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_stores_name CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_stores_status CHECK (operational_status IN ('active', 'offline', 'suspended', 'closed')),
    CONSTRAINT ck_stores_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE terminals
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    store_id uuid NOT NULL,
    code text NOT NULL,
    device_type text NOT NULL,
    registration_status text NOT NULL,
    registered_at_utc timestamptz NULL,
    revoked_at_utc timestamptz NULL,
    last_seen_at_utc timestamptz NULL,
    last_sync_at_utc timestamptz NULL,
    configuration jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(configuration) = 'object'),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_terminals_store_code UNIQUE (store_id, code),
    CONSTRAINT uq_terminals_store_id UNIQUE (store_id, id),
    CONSTRAINT fk_terminals_stores_restaurant_store
        FOREIGN KEY (restaurant_id, store_id) REFERENCES stores (restaurant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_terminals_code CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_terminals_type CHECK (device_type IN ('pos', 'kiosk', 'kds', 'edge')),
    CONSTRAINT ck_terminals_status CHECK (registration_status IN ('pending', 'active', 'suspended', 'revoked')),
    CONSTRAINT ck_terminals_revoked CHECK ((registration_status = 'revoked') = (revoked_at_utc IS NOT NULL)),
    CONSTRAINT ck_terminals_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_terminals_store_status ON terminals (store_id, registration_status, code);

CREATE TABLE shifts
(
    id uuid PRIMARY KEY,
    store_id uuid NOT NULL,
    terminal_id uuid NOT NULL,
    employee_identity_subject_id text NOT NULL,
    shift_number text NOT NULL,
    status text NOT NULL,
    opened_at_utc timestamptz NOT NULL,
    closed_at_utc timestamptz NULL,
    opened_by text NOT NULL,
    closed_by text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_shifts_store_shift_number UNIQUE (store_id, shift_number),
    CONSTRAINT uq_shifts_store_id UNIQUE (store_id, id),
    CONSTRAINT fk_shifts_terminals_store_terminal
        FOREIGN KEY (store_id, terminal_id) REFERENCES terminals (store_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_shifts_employee CHECK (char_length(btrim(employee_identity_subject_id)) > 0),
    CONSTRAINT ck_shifts_status CHECK (status IN ('open', 'closing', 'closed', 'cancelled')),
    CONSTRAINT ck_shifts_closed CHECK (closed_at_utc IS NULL OR closed_at_utc >= opened_at_utc),
    CONSTRAINT ck_shifts_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE UNIQUE INDEX uq_shifts_terminal_open
    ON shifts (terminal_id) WHERE status IN ('open', 'closing');
CREATE INDEX ix_shifts_store_status_opened ON shifts (store_id, status, opened_at_utc DESC);

CREATE TABLE cash_sessions
(
    id uuid PRIMARY KEY,
    store_id uuid NOT NULL,
    shift_id uuid NOT NULL,
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    opening_amount numeric(19,4) NOT NULL CHECK (opening_amount >= 0),
    expected_closing_amount numeric(19,4) NULL,
    actual_closing_amount numeric(19,4) NULL,
    variance_amount numeric(19,4) NULL,
    status text NOT NULL CHECK (status IN ('open', 'counting', 'closed')),
    opened_at_utc timestamptz NOT NULL,
    closed_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_cash_sessions_shift_id UNIQUE (shift_id),
    CONSTRAINT fk_cash_sessions_shifts_store_shift
        FOREIGN KEY (store_id, shift_id) REFERENCES shifts (store_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_cash_sessions_closed CHECK (closed_at_utc IS NULL OR closed_at_utc >= opened_at_utc),
    CONSTRAINT ck_cash_sessions_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE cash_movements
(
    id uuid PRIMARY KEY,
    cash_session_id uuid NOT NULL,
    movement_type text NOT NULL CHECK (movement_type IN ('sale', 'refund', 'pay_in', 'pay_out', 'float_adjustment')),
    amount numeric(19,4) NOT NULL CHECK (amount > 0),
    order_id uuid NULL,
    payment_id uuid NULL,
    reason_code text NULL,
    occurred_at_utc timestamptz NOT NULL,
    recorded_by text NOT NULL,
    CONSTRAINT fk_cash_movements_cash_sessions_cash_session_id
        FOREIGN KEY (cash_session_id) REFERENCES cash_sessions (id) ON DELETE RESTRICT
);

CREATE INDEX ix_cash_movements_session_occurred ON cash_movements (cash_session_id, occurred_at_utc, id);

CREATE TABLE sync_operations
(
    id uuid PRIMARY KEY,
    terminal_id uuid NOT NULL,
    client_operation_id uuid NOT NULL,
    operation_type text NOT NULL,
    payload_hash text NOT NULL,
    status text NOT NULL CHECK (status IN ('received', 'processing', 'completed', 'rejected')),
    response_status integer NULL,
    response_reference_id uuid NULL,
    error_code text NULL,
    received_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    CONSTRAINT uq_sync_operations_terminal_client_operation UNIQUE (terminal_id, client_operation_id),
    CONSTRAINT fk_sync_operations_terminals_terminal_id
        FOREIGN KEY (terminal_id) REFERENCES terminals (id) ON DELETE RESTRICT,
    CONSTRAINT ck_sync_operations_type CHECK (char_length(btrim(operation_type)) > 0),
    CONSTRAINT ck_sync_operations_hash CHECK (char_length(btrim(payload_hash)) > 0)
);

CREATE INDEX ix_sync_operations_terminal_status_received
    ON sync_operations (terminal_id, status, received_at_utc, id);

CREATE TABLE sync_checkpoints
(
    terminal_id uuid NOT NULL,
    stream_name text NOT NULL,
    cursor_value text NOT NULL,
    synchronized_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT pk_sync_checkpoints PRIMARY KEY (terminal_id, stream_name),
    CONSTRAINT fk_sync_checkpoints_terminals_terminal_id
        FOREIGN KEY (terminal_id) REFERENCES terminals (id) ON DELETE RESTRICT,
    CONSTRAINT ck_sync_checkpoints_stream CHECK (char_length(btrim(stream_name)) > 0),
    CONSTRAINT ck_sync_checkpoints_cursor CHECK (char_length(btrim(cursor_value)) > 0)
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

COMMENT ON COLUMN stores.branch_id IS 'External Restaurant Management branch identifier; no cross-database foreign key.';
COMMENT ON COLUMN shifts.employee_identity_subject_id IS 'Stable identity subject identifier; credentials remain in Keycloak.';
COMMENT ON TABLE sync_operations IS 'Terminal-scoped client operation uniqueness prevents duplicate offline processing.';
