DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM catalog_menu_organizations) OR EXISTS(SELECT 1 FROM catalog_menu_organization_audit) THEN
  RAISE EXCEPTION 'Menu organization history exists; use forward recovery.';
 END IF;
END $$;
DROP TABLE catalog_menu_organizations;
DROP TABLE catalog_menu_organization_audit;
DROP FUNCTION prevent_catalog_menu_organization_audit_mutation();
