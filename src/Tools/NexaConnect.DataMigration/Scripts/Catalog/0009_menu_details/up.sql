ALTER TABLE catalog_menu_items ADD COLUMN menu_version bigint NOT NULL DEFAULT 0 CHECK(menu_version>=0);
CREATE TABLE catalog_menu_details_audit (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, product_id uuid NOT NULL,
 version bigint NOT NULL CHECK(version>0), previous_details jsonb NOT NULL CHECK(jsonb_typeof(previous_details)='object'), details jsonb NOT NULL CHECK(jsonb_typeof(details)='object'),
 actor_subject_id text NOT NULL, correlation_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
 UNIQUE(organization_id,branch_id,product_id,version)
);
CREATE FUNCTION prevent_catalog_menu_details_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Menu details audit is append-only'; END;
$$;
CREATE TRIGGER tr_catalog_menu_details_audit_append_only BEFORE UPDATE OR DELETE ON catalog_menu_details_audit
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_menu_details_audit_mutation();
