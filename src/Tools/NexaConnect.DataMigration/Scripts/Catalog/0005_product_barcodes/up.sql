CREATE TABLE catalog_barcode_registry (
    organization_id uuid NOT NULL,
    barcode varchar(64) COLLATE "C" NOT NULL CHECK (barcode ~ '^[A-Za-z0-9._-]{1,64}$'),
    product_id uuid NOT NULL,
    PRIMARY KEY (organization_id, barcode),
    UNIQUE (organization_id, barcode, product_id),
    CHECK (organization_id <> '00000000-0000-0000-0000-000000000000'),
    CHECK (product_id <> '00000000-0000-0000-0000-000000000000')
);
CREATE TABLE catalog_branch_barcodes (
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    barcode varchar(64) COLLATE "C",
    version bigint NOT NULL CHECK (version > 0),
    PRIMARY KEY (organization_id, branch_id, product_id),
    UNIQUE (organization_id, branch_id, barcode),
    FOREIGN KEY (organization_id, branch_id, product_id) REFERENCES catalog_menu_items(organization_id, branch_id, product_id),
    FOREIGN KEY (organization_id, barcode, product_id) REFERENCES catalog_barcode_registry(organization_id, barcode, product_id)
);
CREATE TABLE catalog_barcode_audit (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    product_id uuid NOT NULL,
    version bigint NOT NULL,
    previous_barcode varchar(64),
    barcode varchar(64),
    actor_subject_id text NOT NULL,
    correlation_id uuid NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    UNIQUE (organization_id, branch_id, product_id, version)
);
CREATE FUNCTION prevent_catalog_barcode_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'catalog_barcode_audit is append-only'; END;
$$;
CREATE TRIGGER tr_catalog_barcode_audit_append_only BEFORE UPDATE OR DELETE ON catalog_barcode_audit
FOR EACH ROW EXECUTE FUNCTION prevent_catalog_barcode_audit_mutation();
