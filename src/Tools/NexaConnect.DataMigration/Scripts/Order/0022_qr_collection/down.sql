DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM order_qr_collections) THEN
 RAISE EXCEPTION 'QR collection history exists; forward recovery required'; END IF;
END $$;
DROP TABLE order_qr_collections;
