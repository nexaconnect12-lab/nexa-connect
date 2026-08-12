ALTER TABLE catalog_menu_items
    ADD COLUMN organization_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE catalog_menu_items ALTER COLUMN organization_id DROP DEFAULT;
ALTER TABLE catalog_menu_items DROP CONSTRAINT pk_catalog_menu_items;
ALTER TABLE catalog_menu_items
    ADD CONSTRAINT pk_catalog_menu_items PRIMARY KEY (organization_id, branch_id, product_id);
CREATE INDEX ix_catalog_menu_items_organization_branch_name
    ON catalog_menu_items (organization_id, branch_id, name, product_id);
COMMENT ON COLUMN catalog_menu_items.organization_id IS
    'External Platform Directory identifier required in every customer-tenant query.';
