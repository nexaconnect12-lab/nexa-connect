DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM cashier_pin_credentials) OR EXISTS(SELECT 1 FROM cashier_pin_audit) THEN
        RAISE EXCEPTION 'Cashier PIN security history exists; downgrade requires an explicit archival design';
    END IF;
END $$;
DROP TABLE cashier_pin_audit;
DROP TABLE cashier_pin_credentials;
DROP FUNCTION cashier_pin_audit_immutable();
