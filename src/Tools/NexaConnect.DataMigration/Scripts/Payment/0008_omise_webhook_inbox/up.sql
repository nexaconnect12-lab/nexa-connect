CREATE TABLE omise_webhook_inbox (
 event_id text PRIMARY KEY CHECK(event_id ~ '^evnt_test_[a-z0-9]{10,64}$'),
 correlation_id uuid NOT NULL CHECK(correlation_id <> '00000000-0000-0000-0000-000000000000'),
 trace_correlation_id varchar(128) NOT NULL CHECK(trace_correlation_id ~ '^[A-Za-z0-9._:-]+$'),
 status text NOT NULL DEFAULT 'pending' CHECK(status IN('pending','processing','completed','rejected','exhausted')),
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts>=0),
 received_at_utc timestamptz NOT NULL DEFAULT now(),
 next_attempt_at_utc timestamptz NOT NULL DEFAULT now(),
 claim_id uuid NULL,
 locked_until_utc timestamptz NULL,
 completed_at_utc timestamptz NULL,
 CHECK((status='processing')=(claim_id IS NOT NULL AND locked_until_utc IS NOT NULL))
);
CREATE INDEX ix_omise_webhook_inbox_due ON omise_webhook_inbox(next_attempt_at_utc,received_at_utc) WHERE status IN('pending','processing');
COMMENT ON TABLE omise_webhook_inbox IS 'Test-only Omise event identifiers and fenced processing status; never store webhook bodies, secrets, signatures, card data or provider charge references.';
