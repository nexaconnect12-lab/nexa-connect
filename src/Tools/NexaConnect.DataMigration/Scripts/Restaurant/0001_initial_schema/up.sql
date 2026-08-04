CREATE TABLE restaurants
(
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    legal_name text NULL,
    default_currency char(3) NOT NULL,
    default_time_zone text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_restaurants PRIMARY KEY (id),
    CONSTRAINT uq_restaurants_organization_id_code UNIQUE (organization_id, code),
    CONSTRAINT ck_restaurants_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_restaurants_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_restaurants_default_currency
        CHECK (default_currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_restaurants_default_time_zone
        CHECK (char_length(btrim(default_time_zone)) > 0),
    CONSTRAINT ck_restaurants_status
        CHECK (status IN ('pending', 'active', 'suspended', 'closed')),
    CONSTRAINT ck_restaurants_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_restaurants_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_restaurants_organization_id_status
    ON restaurants (organization_id, status);

CREATE TABLE branches
(
    id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    time_zone text NOT NULL,
    currency char(3) NOT NULL,
    phone_number text NULL,
    email_address text NULL,
    address_line_1 text NULL,
    address_line_2 text NULL,
    city text NULL,
    region text NULL,
    postal_code text NULL,
    country_code char(2) NULL,
    business_configuration jsonb NOT NULL DEFAULT '{}'::jsonb,
    status text NOT NULL,
    opened_at_utc timestamptz NULL,
    closed_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_branches PRIMARY KEY (id),
    CONSTRAINT fk_branches_restaurants_restaurant_id
        FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE RESTRICT,
    CONSTRAINT uq_branches_restaurant_id_code UNIQUE (restaurant_id, code),
    CONSTRAINT uq_branches_restaurant_id_id UNIQUE (restaurant_id, id),
    CONSTRAINT ck_branches_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_branches_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_branches_time_zone
        CHECK (char_length(btrim(time_zone)) > 0),
    CONSTRAINT ck_branches_currency
        CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_branches_country_code
        CHECK (country_code IS NULL OR country_code ~ '^[A-Z]{2}$'),
    CONSTRAINT ck_branches_business_configuration
        CHECK (jsonb_typeof(business_configuration) = 'object'),
    CONSTRAINT ck_branches_status
        CHECK (status IN ('pending', 'active', 'suspended', 'closed')),
    CONSTRAINT ck_branches_lifecycle_timestamps
        CHECK (closed_at_utc IS NULL OR opened_at_utc IS NULL OR closed_at_utc >= opened_at_utc),
    CONSTRAINT ck_branches_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_branches_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_branches_restaurant_id_status
    ON branches (restaurant_id, status);

CREATE TABLE dining_areas
(
    id uuid NOT NULL,
    branch_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_dining_areas PRIMARY KEY (id),
    CONSTRAINT fk_dining_areas_branches_branch_id
        FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT uq_dining_areas_branch_id_code UNIQUE (branch_id, code),
    CONSTRAINT uq_dining_areas_branch_id_id UNIQUE (branch_id, id),
    CONSTRAINT ck_dining_areas_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_dining_areas_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_dining_areas_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_dining_areas_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_dining_areas_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_dining_areas_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_dining_areas_branch_id_status_display_order
    ON dining_areas (branch_id, status, display_order);

CREATE TABLE dining_tables
(
    id uuid NOT NULL,
    branch_id uuid NOT NULL,
    dining_area_id uuid NULL,
    code text NOT NULL,
    display_name text NOT NULL,
    capacity smallint NOT NULL,
    qr_context_id uuid NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_dining_tables PRIMARY KEY (id),
    CONSTRAINT fk_dining_tables_branches_branch_id
        FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT fk_dining_tables_dining_areas_branch_id_dining_area_id
        FOREIGN KEY (branch_id, dining_area_id)
        REFERENCES dining_areas (branch_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_dining_tables_branch_id_code UNIQUE (branch_id, code),
    CONSTRAINT uq_dining_tables_qr_context_id UNIQUE (qr_context_id),
    CONSTRAINT ck_dining_tables_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_dining_tables_display_name
        CHECK (char_length(btrim(display_name)) > 0),
    CONSTRAINT ck_dining_tables_capacity
        CHECK (capacity > 0),
    CONSTRAINT ck_dining_tables_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_dining_tables_status
        CHECK (status IN ('available', 'unavailable', 'out_of_service', 'inactive')),
    CONSTRAINT ck_dining_tables_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_dining_tables_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_dining_tables_branch_id_dining_area_id_status
    ON dining_tables (branch_id, dining_area_id, status);

CREATE TABLE business_hours
(
    id uuid NOT NULL,
    branch_id uuid NOT NULL,
    schedule_kind text NOT NULL,
    day_of_week smallint NULL,
    effective_date date NULL,
    interval_sequence smallint NOT NULL DEFAULT 1,
    opens_at time without time zone NULL,
    closes_at time without time zone NULL,
    is_closed boolean NOT NULL DEFAULT false,
    label text NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_business_hours PRIMARY KEY (id),
    CONSTRAINT fk_business_hours_branches_branch_id
        FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT ck_business_hours_schedule_kind
        CHECK (schedule_kind IN ('weekly', 'exception')),
    CONSTRAINT ck_business_hours_schedule_selector
        CHECK
        (
            (schedule_kind = 'weekly' AND day_of_week BETWEEN 0 AND 6 AND effective_date IS NULL)
            OR
            (schedule_kind = 'exception' AND day_of_week IS NULL AND effective_date IS NOT NULL)
        ),
    CONSTRAINT ck_business_hours_interval_sequence
        CHECK (interval_sequence > 0),
    CONSTRAINT ck_business_hours_open_interval
        CHECK
        (
            (is_closed AND opens_at IS NULL AND closes_at IS NULL)
            OR
            (NOT is_closed AND opens_at IS NOT NULL AND closes_at IS NOT NULL AND opens_at <> closes_at)
        ),
    CONSTRAINT ck_business_hours_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_business_hours_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE UNIQUE INDEX uq_business_hours_weekly_slot
    ON business_hours (branch_id, day_of_week, interval_sequence)
    WHERE schedule_kind = 'weekly';

CREATE UNIQUE INDEX uq_business_hours_exception_slot
    ON business_hours (branch_id, effective_date, interval_sequence)
    WHERE schedule_kind = 'exception';

CREATE INDEX ix_business_hours_branch_id_schedule_kind
    ON business_hours (branch_id, schedule_kind);

CREATE TABLE preparation_stations
(
    id uuid NOT NULL,
    branch_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    station_type text NOT NULL,
    display_order integer NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_preparation_stations PRIMARY KEY (id),
    CONSTRAINT fk_preparation_stations_branches_branch_id
        FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT uq_preparation_stations_branch_id_code UNIQUE (branch_id, code),
    CONSTRAINT ck_preparation_stations_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_preparation_stations_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_preparation_stations_station_type
        CHECK (station_type IN ('kitchen', 'bar', 'dessert', 'expediter', 'other')),
    CONSTRAINT ck_preparation_stations_display_order
        CHECK (display_order >= 0),
    CONSTRAINT ck_preparation_stations_status
        CHECK (status IN ('active', 'inactive')),
    CONSTRAINT ck_preparation_stations_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_preparation_stations_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_preparation_stations_branch_id_status_display_order
    ON preparation_stations (branch_id, status, display_order);

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

COMMENT ON COLUMN restaurants.organization_id IS
    'Stable Platform Directory organization identifier; intentionally has no cross-database foreign key.';

COMMENT ON COLUMN dining_tables.qr_context_id IS
    'Public lookup context only; QR secrets and credentials must not be stored here.';
