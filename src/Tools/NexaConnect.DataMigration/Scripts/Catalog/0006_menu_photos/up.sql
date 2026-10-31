CREATE TABLE catalog_menu_photos (
 organization_id uuid NOT NULL, branch_id uuid NOT NULL, product_id uuid NOT NULL,
 asset_id uuid, version bigint NOT NULL CHECK(version>0),
 PRIMARY KEY(organization_id,branch_id,product_id),
 FOREIGN KEY(organization_id,branch_id,product_id) REFERENCES catalog_menu_items(organization_id,branch_id,product_id),
 CHECK(asset_id IS NULL OR asset_id<>'00000000-0000-0000-0000-000000000000')
);
CREATE TABLE catalog_menu_photo_audit (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, branch_id uuid NOT NULL, product_id uuid NOT NULL,
 version bigint NOT NULL, previous_asset_id uuid, asset_id uuid, actor_subject_id text NOT NULL,
 correlation_id uuid NOT NULL, occurred_at_utc timestamptz NOT NULL,
 UNIQUE(organization_id,branch_id,product_id,version)
);
CREATE FUNCTION prevent_catalog_menu_photo_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'catalog_menu_photo_audit is append-only'; END;
$$;
CREATE TRIGGER tr_catalog_menu_photo_audit_append_only BEFORE UPDATE OR DELETE ON catalog_menu_photo_audit
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_menu_photo_audit_mutation();
