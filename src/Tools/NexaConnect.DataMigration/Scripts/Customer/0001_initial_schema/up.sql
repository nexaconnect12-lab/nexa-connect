CREATE TABLE customers
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    customer_number text NOT NULL,
    identity_subject_id text NULL,
    display_name text NOT NULL,
    status text NOT NULL,
    contact_preferences jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(contact_preferences) = 'object'),
    attributes jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(attributes) = 'object'),
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_customers_organization_number UNIQUE (organization_id, customer_number),
    CONSTRAINT ck_customers_number CHECK (char_length(btrim(customer_number)) > 0),
    CONSTRAINT ck_customers_name CHECK (char_length(btrim(display_name)) > 0),
    CONSTRAINT ck_customers_status CHECK (status IN ('active', 'inactive', 'blocked', 'anonymized')),
    CONSTRAINT ck_customers_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE UNIQUE INDEX uq_customers_organization_identity
    ON customers (organization_id, identity_subject_id) WHERE identity_subject_id IS NOT NULL;
CREATE INDEX ix_customers_organization_status_name ON customers (organization_id, status, display_name, id);

CREATE TABLE customer_contacts
(
    id uuid PRIMARY KEY,
    customer_id uuid NOT NULL,
    contact_type text NOT NULL CHECK (contact_type IN ('email', 'phone')),
    contact_value text NOT NULL,
    normalized_value text NOT NULL,
    is_primary boolean NOT NULL DEFAULT false,
    is_verified boolean NOT NULL DEFAULT false,
    verified_at_utc timestamptz NULL,
    status text NOT NULL CHECK (status IN ('active', 'inactive')),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_customer_contacts_customer_type_value UNIQUE (customer_id, contact_type, normalized_value),
    CONSTRAINT fk_customer_contacts_customers_customer_id
        FOREIGN KEY (customer_id) REFERENCES customers (id) ON DELETE RESTRICT,
    CONSTRAINT ck_customer_contacts_values CHECK (char_length(btrim(contact_value)) > 0 AND char_length(btrim(normalized_value)) > 0),
    CONSTRAINT ck_customer_contacts_verified CHECK (NOT is_verified OR verified_at_utc IS NOT NULL)
);

CREATE UNIQUE INDEX uq_customer_contacts_primary_type
    ON customer_contacts (customer_id, contact_type) WHERE is_primary AND status = 'active';

CREATE TABLE customer_addresses
(
    id uuid PRIMARY KEY,
    customer_id uuid NOT NULL,
    address_type text NOT NULL CHECK (address_type IN ('home', 'work', 'delivery', 'billing', 'other')),
    recipient_name text NULL,
    line_1 text NOT NULL,
    line_2 text NULL,
    city text NOT NULL,
    region text NULL,
    postal_code text NULL,
    country_code char(2) NOT NULL CHECK (country_code ~ '^[A-Z]{2}$'),
    delivery_instructions text NULL,
    is_primary boolean NOT NULL DEFAULT false,
    status text NOT NULL CHECK (status IN ('active', 'inactive')),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_customer_addresses_customers_customer_id
        FOREIGN KEY (customer_id) REFERENCES customers (id) ON DELETE RESTRICT,
    CONSTRAINT ck_customer_addresses_line_1 CHECK (char_length(btrim(line_1)) > 0),
    CONSTRAINT ck_customer_addresses_city CHECK (char_length(btrim(city)) > 0)
);

CREATE UNIQUE INDEX uq_customer_addresses_primary_type
    ON customer_addresses (customer_id, address_type) WHERE is_primary AND status = 'active';

CREATE TABLE loyalty_accounts
(
    id uuid PRIMARY KEY,
    customer_id uuid NOT NULL,
    program_code text NOT NULL,
    loyalty_number text NOT NULL,
    points_balance numeric(19,4) NOT NULL DEFAULT 0 CHECK (points_balance >= 0),
    tier_code text NULL,
    status text NOT NULL CHECK (status IN ('active', 'suspended', 'closed')),
    joined_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_loyalty_accounts_program_number UNIQUE (program_code, loyalty_number),
    CONSTRAINT uq_loyalty_accounts_customer_program UNIQUE (customer_id, program_code),
    CONSTRAINT fk_loyalty_accounts_customers_customer_id
        FOREIGN KEY (customer_id) REFERENCES customers (id) ON DELETE RESTRICT,
    CONSTRAINT ck_loyalty_accounts_codes CHECK (char_length(btrim(program_code)) > 0 AND char_length(btrim(loyalty_number)) > 0)
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

COMMENT ON COLUMN customers.organization_id IS 'External Platform Directory identifier; no cross-database foreign key.';
COMMENT ON COLUMN customers.identity_subject_id IS 'Optional stable Keycloak subject; credentials are never stored here.';
COMMENT ON TABLE customer_contacts IS 'Personally identifiable data: access, logging, retention, and export must be restricted.';
