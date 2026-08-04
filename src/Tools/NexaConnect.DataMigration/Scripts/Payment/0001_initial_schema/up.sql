CREATE TABLE payment_intents
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    order_id uuid NOT NULL,
    idempotency_key text NOT NULL,
    amount numeric(19,4) NOT NULL CHECK (amount > 0),
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    payment_method text NOT NULL,
    status text NOT NULL,
    expires_at_utc timestamptz NULL,
    authorized_at_utc timestamptz NULL,
    captured_at_utc timestamptz NULL,
    failed_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_payment_intents_restaurant_idempotency UNIQUE (restaurant_id, idempotency_key),
    CONSTRAINT ck_payment_intents_key CHECK (char_length(btrim(idempotency_key)) > 0),
    CONSTRAINT ck_payment_intents_method CHECK (payment_method IN ('cash', 'card', 'wallet', 'bank_transfer', 'other')),
    CONSTRAINT ck_payment_intents_status CHECK (status IN ('pending', 'requires_action', 'authorized', 'captured', 'failed', 'cancelled', 'expired')),
    CONSTRAINT ck_payment_intents_audit CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_payment_intents_order_status ON payment_intents (order_id, status, created_at_utc DESC);

CREATE TABLE provider_transactions
(
    id uuid PRIMARY KEY,
    payment_intent_id uuid NOT NULL,
    provider_code text NOT NULL,
    provider_transaction_id text NOT NULL,
    transaction_type text NOT NULL,
    amount numeric(19,4) NOT NULL CHECK (amount > 0),
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    status text NOT NULL,
    sanitized_response jsonb NULL,
    processed_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_provider_transactions_provider_reference UNIQUE (provider_code, provider_transaction_id, transaction_type),
    CONSTRAINT fk_provider_transactions_payment_intents_payment_intent_id
        FOREIGN KEY (payment_intent_id) REFERENCES payment_intents (id) ON DELETE RESTRICT,
    CONSTRAINT ck_provider_transactions_provider CHECK (char_length(btrim(provider_code)) > 0 AND char_length(btrim(provider_transaction_id)) > 0),
    CONSTRAINT ck_provider_transactions_type CHECK (transaction_type IN ('authorize', 'capture', 'sale', 'void', 'refund')),
    CONSTRAINT ck_provider_transactions_status CHECK (status IN ('pending', 'succeeded', 'failed', 'unknown')),
    CONSTRAINT ck_provider_transactions_response CHECK (sanitized_response IS NULL OR jsonb_typeof(sanitized_response) = 'object')
);

CREATE INDEX ix_provider_transactions_intent_processed ON provider_transactions (payment_intent_id, processed_at_utc DESC);

CREATE TABLE refunds
(
    id uuid PRIMARY KEY,
    payment_intent_id uuid NOT NULL,
    order_return_id uuid NULL,
    idempotency_key text NOT NULL,
    amount numeric(19,4) NOT NULL CHECK (amount > 0),
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    reason_code text NOT NULL,
    status text NOT NULL,
    requested_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    failed_at_utc timestamptz NULL,
    requested_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_refunds_payment_intent_idempotency UNIQUE (payment_intent_id, idempotency_key),
    CONSTRAINT fk_refunds_payment_intents_payment_intent_id
        FOREIGN KEY (payment_intent_id) REFERENCES payment_intents (id) ON DELETE RESTRICT,
    CONSTRAINT ck_refunds_reason CHECK (char_length(btrim(reason_code)) > 0),
    CONSTRAINT ck_refunds_status CHECK (status IN ('requested', 'processing', 'completed', 'failed', 'cancelled'))
);

CREATE INDEX ix_refunds_payment_intent_status ON refunds (payment_intent_id, status, requested_at_utc DESC);

CREATE TABLE reconciliation_records
(
    id uuid PRIMARY KEY,
    provider_code text NOT NULL,
    settlement_reference text NOT NULL,
    payment_intent_id uuid NULL,
    provider_transaction_id uuid NULL,
    settlement_date date NOT NULL,
    gross_amount numeric(19,4) NOT NULL,
    fee_amount numeric(19,4) NOT NULL DEFAULT 0 CHECK (fee_amount >= 0),
    net_amount numeric(19,4) NOT NULL,
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    status text NOT NULL CHECK (status IN ('matched', 'unmatched', 'disputed', 'resolved')),
    details jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(details) = 'object'),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_reconciliation_provider_reference UNIQUE (provider_code, settlement_reference),
    CONSTRAINT fk_reconciliation_payment_intents_payment_intent_id
        FOREIGN KEY (payment_intent_id) REFERENCES payment_intents (id) ON DELETE RESTRICT,
    CONSTRAINT fk_reconciliation_provider_transactions_provider_transaction_id
        FOREIGN KEY (provider_transaction_id) REFERENCES provider_transactions (id) ON DELETE RESTRICT
);

CREATE INDEX ix_reconciliation_status_date ON reconciliation_records (status, settlement_date, id);

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

COMMENT ON COLUMN payment_intents.order_id IS 'External Order service identifier; no cross-database foreign key.';
COMMENT ON TABLE provider_transactions IS 'Only sanitized provider results are permitted; card numbers, CVV, tokens, and secrets are prohibited.';
