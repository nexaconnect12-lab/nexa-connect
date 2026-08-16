DROP TRIGGER tr_customer_audit_append_only ON customer_audit_records;
DROP FUNCTION prevent_customer_audit_mutation();
DROP TABLE customer_audit_records;
