DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM catalog_menu_availability_audit) OR EXISTS(SELECT 1 FROM catalog_menu_items WHERE availability_version>0) THEN
  RAISE EXCEPTION 'Menu availability history exists; use forward recovery.';
 END IF;
END $$;
DROP TABLE catalog_menu_availability_audit;
DROP FUNCTION prevent_catalog_menu_availability_audit_mutation();
ALTER TABLE catalog_menu_items DROP COLUMN availability_version;
