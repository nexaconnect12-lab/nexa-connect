DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM catalog_menu_details_audit) OR EXISTS(SELECT 1 FROM catalog_menu_items WHERE menu_version>0) THEN
  RAISE EXCEPTION 'Menu details history exists; use forward recovery.';
 END IF;
END $$;
DROP TABLE catalog_menu_details_audit;
DROP FUNCTION prevent_catalog_menu_details_audit_mutation();
ALTER TABLE catalog_menu_items DROP COLUMN menu_version;
