CREATE TABLE inbox_messages
(
    message_id uuid NOT NULL,
    consumer_name text NOT NULL,
    status text NOT NULL DEFAULT 'queued',
    attempts integer NOT NULL DEFAULT 0,
    locked_until_utc timestamptz NULL,
    processed_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id, consumer_name),
    CONSTRAINT ck_inbox_messages_status CHECK (status IN ('queued', 'processing', 'completed')),
    CONSTRAINT ck_inbox_messages_attempts CHECK (attempts >= 0),
    CONSTRAINT ck_inbox_messages_consumer_name CHECK (char_length(btrim(consumer_name)) > 0)
);

CREATE INDEX ix_inbox_messages_retryable
    ON inbox_messages (locked_until_utc, message_id)
    WHERE status <> 'completed';
