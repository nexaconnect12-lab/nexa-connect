CREATE TABLE order_qr_collections (
 order_id uuid PRIMARY KEY REFERENCES order_qr_acceptances(order_id),
 organization_id uuid NOT NULL, branch_id uuid NOT NULL,
 terminal_id uuid NOT NULL, store_id uuid NOT NULL, shift_id uuid NOT NULL,
 settlement_key uuid NOT NULL UNIQUE, actor varchar(200) NOT NULL,
 decision_id uuid NOT NULL, correlation_id uuid NOT NULL,
 created_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER tr_order_qr_collection_immutable BEFORE UPDATE OR DELETE ON order_qr_collections
 FOR EACH ROW EXECUTE FUNCTION prevent_order_qr_acceptance_mutation();
