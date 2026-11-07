CREATE TABLE manual_refund_sales (
    order_id uuid PRIMARY KEY,
    settlement_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    tender text NOT NULL CHECK (tender IN ('cash', 'promptpay_manual')),
    currency char(3) NOT NULL CHECK (currency = 'THB'),
    paid_amount numeric(19,4) NOT NULL CHECK (paid_amount > 0),
    paid_at_utc timestamptz NOT NULL
);

CREATE TABLE manual_refund_approvals (
    operation_id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES manual_refund_sales(order_id),
    amount numeric(19,4) NOT NULL CHECK (amount > 0),
    reason text NOT NULL CHECK (reason IN ('customer_request', 'duplicate_charge', 'item_unavailable', 'service_issue', 'other')),
    manager_subject varchar(200) NOT NULL,
    authorization_decision_id uuid NOT NULL,
    cashier_subject varchar(200) NOT NULL,
    approved_at_utc timestamptz NOT NULL,
    CHECK (manager_subject <> cashier_subject)
);
CREATE INDEX ix_manual_refund_approvals_order ON manual_refund_approvals(order_id);

CREATE TABLE manual_refund_payouts (
    operation_id uuid PRIMARY KEY REFERENCES manual_refund_approvals(operation_id),
    terminal_id uuid NOT NULL REFERENCES terminals(id),
    shift_id uuid NOT NULL REFERENCES shifts(id),
    cash_session_id uuid REFERENCES cash_sessions(id),
    started_at_utc timestamptz NOT NULL
);
CREATE INDEX ix_manual_refund_payouts_shift ON manual_refund_payouts(shift_id);
CREATE INDEX ix_manual_refund_payouts_drawer ON manual_refund_payouts(cash_session_id) WHERE cash_session_id IS NOT NULL;

CREATE TABLE manual_refund_completions (
    operation_id uuid PRIMARY KEY REFERENCES manual_refund_payouts(operation_id),
    cash_movement_id uuid UNIQUE REFERENCES cash_movements(id),
    protected_transfer_reference text,
    recorded_at_utc timestamptz NOT NULL,
    CHECK ((cash_movement_id IS NULL) <> (protected_transfer_reference IS NULL))
);

CREATE FUNCTION prevent_manual_refund_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'manual_refund_history_is_append_only';
END;
$$;

CREATE TRIGGER manual_refund_sales_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_sales
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();
CREATE TRIGGER manual_refund_approvals_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_approvals
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();
CREATE TRIGGER manual_refund_payouts_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_payouts
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();
CREATE TRIGGER manual_refund_completions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_completions
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();

CREATE FUNCTION prevent_close_during_manual_refund() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM manual_refund_payouts payout
        WHERE (CASE WHEN TG_TABLE_NAME = 'shifts' THEN payout.shift_id ELSE payout.cash_session_id END) = OLD.id
          AND NOT EXISTS (SELECT 1 FROM manual_refund_completions done WHERE done.operation_id = payout.operation_id)
    ) THEN
        RAISE EXCEPTION USING ERRCODE = 'PMR01', MESSAGE = 'manual_refund_payout_pending';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER manual_refund_shift_close BEFORE UPDATE OF status ON shifts
    FOR EACH ROW WHEN (OLD.status = 'open' AND NEW.status <> 'open')
    EXECUTE FUNCTION prevent_close_during_manual_refund();
CREATE TRIGGER manual_refund_drawer_close BEFORE UPDATE OF status ON cash_sessions
    FOR EACH ROW WHEN (OLD.status = 'open' AND NEW.status <> 'open')
    EXECUTE FUNCTION prevent_close_during_manual_refund();
