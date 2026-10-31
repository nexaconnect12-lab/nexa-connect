ALTER TABLE catalog_menu_items ADD COLUMN availability_version bigint NOT NULL DEFAULT 0 CHECK(availability_version>=0);
CREATE TABLE catalog_menu_availability_audit (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, product_id uuid NOT NULL,
 version bigint NOT NULL CHECK(version>0), previous_available boolean NOT NULL, available boolean NOT NULL,
 actor_subject_id text NOT NULL, correlation_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
 CHECK(previous_available<>available), UNIQUE(organization_id,branch_id,product_id,version)
);
CREATE FUNCTION prevent_catalog_menu_availability_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Menu availability audit is append-only'; END;
$$;
CREATE TRIGGER tr_catalog_menu_availability_audit_append_only BEFORE UPDATE OR DELETE ON catalog_menu_availability_audit
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_menu_availability_audit_mutation();
