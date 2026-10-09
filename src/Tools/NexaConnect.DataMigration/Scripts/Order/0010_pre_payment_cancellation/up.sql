ALTER TABLE orders DROP CONSTRAINT ck_orders_status;
ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN
('draft','submitted','accepted','inventory_reserved','kitchen_accepted','payment_pending','payment_review','preparing','ready','completed','cancelled','cancellation_pending','cancellation_review'));

CREATE TABLE order_cancellations (
    order_id uuid PRIMARY KEY REFERENCES orders(id),
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    operation_id uuid NOT NULL UNIQUE,
    reason varchar(200) NOT NULL,
    actor_subject_id varchar(200) NOT NULL,
    authorization_decision_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    from_status varchar(32) NOT NULL,
    release_inventory boolean NOT NULL,
    cancel_kitchen boolean NOT NULL,
    status varchar(16) NOT NULL,
    claim_id uuid NULL,
    locked_until_utc timestamptz NULL,
    next_attempt_at_utc timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    last_error_category varchar(64) NULL,
    requested_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_order_cancellations_reason CHECK (char_length(btrim(reason)) BETWEEN 1 AND 200),
    CONSTRAINT ck_order_cancellations_from_status CHECK (from_status IN ('submitted','inventory_reserved','kitchen_accepted')),
    CONSTRAINT ck_order_cancellations_status CHECK (status IN ('pending','completed','blocked')),
    CONSTRAINT ck_order_cancellations_attempts CHECK (attempt_count >= 0),
    CONSTRAINT ck_order_cancellations_completion CHECK ((status='completed') = (completed_at_utc IS NOT NULL)),
    CONSTRAINT ck_order_cancellations_dependencies CHECK
      ((from_status='submitted' AND NOT release_inventory AND NOT cancel_kitchen)
       OR (from_status='inventory_reserved' AND release_inventory AND NOT cancel_kitchen)
       OR (from_status='kitchen_accepted' AND release_inventory AND cancel_kitchen))
);
CREATE INDEX ix_order_cancellations_recovery ON order_cancellations(next_attempt_at_utc,order_id)
WHERE status='pending';
COMMENT ON TABLE order_cancellations IS 'One immutable operator cancellation identity plus fenced compensation state per pre-payment Order.';

CREATE FUNCTION protect_order_cancellation_history() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Order cancellation history is immutable';
    END IF;
    IF ROW(OLD.order_id,OLD.organization_id,OLD.branch_id,OLD.operation_id,OLD.reason,
           OLD.actor_subject_id,OLD.authorization_decision_id,OLD.correlation_id,OLD.from_status,
           OLD.release_inventory,OLD.cancel_kitchen,OLD.requested_at_utc)
       IS DISTINCT FROM
       ROW(NEW.order_id,NEW.organization_id,NEW.branch_id,NEW.operation_id,NEW.reason,
           NEW.actor_subject_id,NEW.authorization_decision_id,NEW.correlation_id,NEW.from_status,
           NEW.release_inventory,NEW.cancel_kitchen,NEW.requested_at_utc) THEN
        RAISE EXCEPTION 'Order cancellation identity and audit fields are immutable';
    END IF;
    IF OLD.status IN ('completed','blocked') AND ROW(OLD.*) IS DISTINCT FROM ROW(NEW.*) THEN
        RAISE EXCEPTION 'Terminal order cancellation history is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_order_cancellation_history
BEFORE UPDATE OR DELETE ON order_cancellations
FOR EACH ROW EXECUTE FUNCTION protect_order_cancellation_history();
