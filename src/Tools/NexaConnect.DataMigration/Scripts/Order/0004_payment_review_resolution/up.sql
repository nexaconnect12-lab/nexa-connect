CREATE TABLE order_payment_reviews
(
    order_id uuid PRIMARY KEY REFERENCES orders(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    payment_intent_id uuid NOT NULL,
    status text NOT NULL CHECK(status IN('open','resolved')),
    reason text NOT NULL CHECK(char_length(btrim(reason)) BETWEEN 1 AND 200),
    resolution text NULL CHECK(resolution IS NULL OR resolution IN('confirm_void','resume_payment','escalate')),
    resolution_reason text NULL CHECK(resolution_reason IS NULL OR char_length(btrim(resolution_reason)) BETWEEN 1 AND 200),
    resolved_by text NULL CHECK(resolved_by IS NULL OR char_length(btrim(resolved_by)) BETWEEN 1 AND 200),
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK(concurrency_version > 0),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    resolved_at_utc timestamptz NULL,
    CONSTRAINT uq_order_payment_reviews_intent UNIQUE(organization_id,payment_intent_id),
    CONSTRAINT ck_order_payment_reviews_timestamps CHECK(updated_at_utc>=created_at_utc AND (resolved_at_utc IS NULL OR resolved_at_utc>=created_at_utc))
);
CREATE INDEX ix_order_payment_reviews_open ON order_payment_reviews(organization_id,branch_id,created_at_utc,order_id) WHERE status='open';

CREATE TABLE order_payment_review_history
(
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES order_payment_reviews(order_id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    action text NOT NULL CHECK(action IN('confirm_void','resume_payment','escalate')),
    reason text NOT NULL CHECK(char_length(btrim(reason)) BETWEEN 1 AND 200),
    actor_subject_id text NOT NULL CHECK(char_length(btrim(actor_subject_id)) BETWEEN 1 AND 200),
    concurrency_version bigint NOT NULL CHECK(concurrency_version > 1),
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_order_payment_review_history_version UNIQUE(order_id,concurrency_version)
);
CREATE INDEX ix_order_payment_review_history_order ON order_payment_review_history(order_id,occurred_at_utc,id);

CREATE FUNCTION reject_order_payment_review_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'order_payment_review_history is append-only'; END $$;
CREATE TRIGGER trg_order_payment_review_history_append_only BEFORE UPDATE OR DELETE ON order_payment_review_history
FOR EACH ROW EXECUTE FUNCTION reject_order_payment_review_history_mutation();
