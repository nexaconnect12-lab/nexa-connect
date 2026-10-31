CREATE TABLE catalog_menu_organizations (
 organization_id uuid NOT NULL, branch_id uuid NOT NULL, version bigint NOT NULL CHECK(version>0),
 categories jsonb NOT NULL CHECK(jsonb_typeof(categories)='array' AND jsonb_array_length(categories)<=50),
 items jsonb NOT NULL CHECK(jsonb_typeof(items)='array' AND jsonb_array_length(items)<=200),
 PRIMARY KEY(organization_id,branch_id)
);
CREATE TABLE catalog_menu_organization_audit (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, version bigint NOT NULL CHECK(version>0),
 previous_layout jsonb NOT NULL, layout jsonb NOT NULL, actor_subject_id text NOT NULL,
 correlation_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
 UNIQUE(organization_id,branch_id,version)
);
CREATE FUNCTION prevent_catalog_menu_organization_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'catalog_menu_organization_audit is append-only'; END;
$$;
CREATE TRIGGER tr_catalog_menu_organization_audit_append_only BEFORE UPDATE OR DELETE ON catalog_menu_organization_audit
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_menu_organization_audit_mutation();
