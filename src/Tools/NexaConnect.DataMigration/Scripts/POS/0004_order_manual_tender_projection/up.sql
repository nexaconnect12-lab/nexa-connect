CREATE TABLE pos_order_settlements
(
    event_id uuid PRIMARY KEY,
    settlement_id uuid NOT NULL UNIQUE,
    order_id uuid NOT NULL UNIQUE,
    organization_id uuid NOT NULL,
    restaurant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    terminal_id uuid NOT NULL REFERENCES terminals(id) ON DELETE RESTRICT,
    cash_session_id uuid NULL REFERENCES cash_sessions(id) ON DELETE RESTRICT,
    method text NOT NULL CHECK(method IN('cash','promptpay_manual')),
    amount numeric(19,4) NOT NULL CHECK(amount>0),
    currency char(3) NOT NULL CHECK(btrim(currency)='THB'),
    occurred_at_utc timestamptz NOT NULL,
    projected_at_utc timestamptz NOT NULL CHECK(projected_at_utc>=occurred_at_utc),
    CONSTRAINT ck_pos_order_settlement_cash_session CHECK((method='cash' AND cash_session_id IS NOT NULL) OR (method='promptpay_manual' AND cash_session_id IS NULL))
);
CREATE INDEX ix_pos_order_settlements_branch_time ON pos_order_settlements(organization_id,branch_id,occurred_at_utc,event_id);
CREATE UNIQUE INDEX uq_cash_movements_manual_order ON cash_movements(order_id) WHERE movement_type='sale' AND order_id IS NOT NULL AND reason_code='ORDER_MANUAL_TENDER';
CREATE FUNCTION reject_pos_order_settlement_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'pos_order_settlements is append-only'; END $$;
CREATE TRIGGER trg_pos_order_settlement_append_only BEFORE UPDATE OR DELETE ON pos_order_settlements FOR EACH ROW EXECUTE FUNCTION reject_pos_order_settlement_mutation();
