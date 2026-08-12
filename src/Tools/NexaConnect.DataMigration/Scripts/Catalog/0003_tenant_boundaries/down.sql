DROP INDEX ix_catalog_menu_items_organization_branch_name;
ALTER TABLE catalog_menu_items DROP CONSTRAINT pk_catalog_menu_items;
ALTER TABLE catalog_menu_items
    ADD CONSTRAINT pk_catalog_menu_items PRIMARY KEY (branch_id, product_id);
ALTER TABLE catalog_menu_items DROP COLUMN organization_id;
