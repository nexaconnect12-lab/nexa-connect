DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM catalog_barcode_audit) OR EXISTS (SELECT 1 FROM catalog_branch_barcodes)
       OR EXISTS (SELECT 1 FROM catalog_barcode_registry) THEN
        RAISE EXCEPTION 'Barcode history exists; use forward recovery.';
    END IF;
END $$;
DROP TABLE catalog_branch_barcodes;
DROP TABLE catalog_barcode_registry;
DROP TABLE catalog_barcode_audit;
DROP FUNCTION prevent_catalog_barcode_audit_mutation();
