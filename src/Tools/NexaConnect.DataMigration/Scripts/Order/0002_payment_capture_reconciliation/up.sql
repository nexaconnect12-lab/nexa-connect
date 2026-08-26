ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status
    CHECK (status IN ('draft', 'submitted', 'accepted', 'payment_pending', 'preparing', 'ready', 'completed', 'cancelled'));

ALTER TABLE order_status_history DROP CONSTRAINT ck_order_status_history_to_status;
ALTER TABLE order_status_history ADD CONSTRAINT ck_order_status_history_to_status
    CHECK (to_status IN ('draft', 'submitted', 'accepted', 'payment_pending', 'preparing', 'ready', 'completed', 'cancelled'));

CREATE TABLE inbox_messages
(
    message_id uuid NOT NULL,
    consumer_name text NOT NULL,
    status text NOT NULL,
    attempts integer NOT NULL DEFAULT 0,
    locked_until_utc timestamptz NULL,
    processed_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id, consumer_name),
    CONSTRAINT ck_inbox_messages_status CHECK (status IN ('queued', 'processing', 'completed')),
    CONSTRAINT ck_inbox_messages_attempts CHECK (attempts >= 0)
);

CREATE INDEX ix_inbox_messages_claim
    ON inbox_messages (status, locked_until_utc, message_id);
