CREATE TABLE inventory_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,resource_id uuid NOT NULL,action text NOT NULL,actor_subject_id text NOT NULL,occurred_at_utc timestamptz NOT NULL);
CREATE FUNCTION prevent_inventory_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'inventory_audit_records is append-only'; END; $$;
CREATE TRIGGER tr_inventory_audit_records_append_only BEFORE UPDATE OR DELETE ON inventory_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_inventory_audit_mutation();
