CREATE TABLE settlement_event_receipts(event_id uuid PRIMARY KEY,payload_hash text NOT NULL CHECK(length(payload_hash)=64));
CREATE TABLE settlement_facts(
 event_id uuid PRIMARY KEY REFERENCES settlement_event_receipts(event_id),organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,
 branch_id uuid NOT NULL,settlement_id uuid NOT NULL,business_date date NOT NULL,contract_version integer NOT NULL CHECK(contract_version IN(1,2)),
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),projected_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 UNIQUE(organization_id,settlement_id),UNIQUE(organization_id,branch_id,business_date));
CREATE INDEX ix_settlement_facts_scope ON settlement_facts(organization_id,branch_id,business_date,event_id);
CREATE FUNCTION settlement_projection_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Settlement projections and receipts are immutable'; END $$;
CREATE TRIGGER settlement_fact_guard BEFORE UPDATE OR DELETE OR TRUNCATE ON settlement_facts FOR EACH STATEMENT EXECUTE FUNCTION settlement_projection_immutable();
CREATE TRIGGER settlement_receipt_guard BEFORE UPDATE OR DELETE OR TRUNCATE ON settlement_event_receipts FOR EACH STATEMENT EXECUTE FUNCTION settlement_projection_immutable();
