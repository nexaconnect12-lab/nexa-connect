DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM order_sale_publications) THEN
        RAISE EXCEPTION 'Sale publication history exists; use forward recovery';
    END IF;
END $$;
DROP TABLE order_sale_replay_audit;
DROP TABLE order_sale_publications;
DROP FUNCTION protect_sale_publication();
