CREATE TABLE cash_correction_event_receipts(event_id uuid PRIMARY KEY, payload_hash text NOT NULL CHECK(length(payload_hash)=64));
CREATE TABLE cash_correction_facts(
 event_id uuid PRIMARY KEY REFERENCES cash_correction_event_receipts(event_id), organization_id uuid NOT NULL,
 restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,correction_id uuid NOT NULL,work_id uuid NOT NULL,tender_id uuid NOT NULL,order_id uuid NOT NULL,
 posted_at_utc timestamptz NOT NULL,adjustment numeric(19,4) NOT NULL CHECK(adjustment<0),payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 projected_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 UNIQUE(organization_id,correction_id),UNIQUE(organization_id,work_id),UNIQUE(organization_id,tender_id),UNIQUE(organization_id,order_id));
CREATE INDEX ix_cash_correction_facts_scope ON cash_correction_facts(organization_id,branch_id,posted_at_utc,event_id);
CREATE FUNCTION cash_correction_projection_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Cash correction projections and receipts are immutable'; END $$;
CREATE TRIGGER cash_correction_fact_guard BEFORE UPDATE OR DELETE OR TRUNCATE ON cash_correction_facts FOR EACH STATEMENT EXECUTE FUNCTION cash_correction_projection_immutable();
CREATE TRIGGER cash_correction_receipt_guard BEFORE UPDATE OR DELETE OR TRUNCATE ON cash_correction_event_receipts FOR EACH STATEMENT EXECUTE FUNCTION cash_correction_projection_immutable();
