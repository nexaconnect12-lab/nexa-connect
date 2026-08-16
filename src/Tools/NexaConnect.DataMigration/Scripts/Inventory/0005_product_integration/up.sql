CREATE TABLE inventory_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,resource_id uuid NOT NULL,action text NOT NULL,actor_subject_id text NOT NULL,occurred_at_utc timestamptz NOT NULL);
CREATE FUNCTION prevent_inventory_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'inventory_audit_records is append-only'; END; $$;
CREATE TRIGGER tr_inventory_audit_records_append_only BEFORE UPDATE OR DELETE ON inventory_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_inventory_audit_mutation();
ALTER TABLE inventory_reservation_lines ADD COLUMN reservation_id uuid NULL;
UPDATE inventory_reservation_lines SET reservation_id=md5(organization_id::text||':'||order_id::text)::uuid;
ALTER TABLE inventory_reservation_lines ALTER COLUMN reservation_id SET NOT NULL;
CREATE INDEX ix_inventory_reservation_id ON inventory_reservation_lines(organization_id,reservation_id);
