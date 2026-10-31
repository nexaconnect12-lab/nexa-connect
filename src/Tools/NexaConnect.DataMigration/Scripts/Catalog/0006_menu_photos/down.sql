DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM catalog_menu_photos) OR EXISTS(SELECT 1 FROM catalog_menu_photo_audit) THEN
  RAISE EXCEPTION 'Menu photo history exists; use forward recovery.';
 END IF;
END $$;
DROP TABLE catalog_menu_photos;
DROP TABLE catalog_menu_photo_audit;
DROP FUNCTION prevent_catalog_menu_photo_audit_mutation();
