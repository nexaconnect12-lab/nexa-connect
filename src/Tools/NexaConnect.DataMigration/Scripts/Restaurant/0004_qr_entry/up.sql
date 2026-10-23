CREATE TABLE restaurant_qr_entries (
 id uuid PRIMARY KEY,
 organization_id uuid NOT NULL,
 restaurant_id uuid NOT NULL,
 branch_id uuid NOT NULL,
 table_code text NOT NULL CHECK (table_code ~ '^[A-Z0-9_-]{1,32}$'),
 secret_hash text NOT NULL UNIQUE CHECK (secret_hash ~ '^[A-F0-9]{64}$'),
 version bigint NOT NULL CHECK(version > 0),
 active boolean NOT NULL,
 expires_at_utc timestamptz NOT NULL,
 FOREIGN KEY (restaurant_id,branch_id) REFERENCES branches(restaurant_id,id) ON DELETE RESTRICT,
 UNIQUE (branch_id,table_code)
);
CREATE INDEX ix_qr_entries_owner ON restaurant_qr_entries(organization_id,branch_id,id);
CREATE TABLE restaurant_qr_sessions (
 visitor_id uuid PRIMARY KEY,
 entry_id uuid NOT NULL REFERENCES restaurant_qr_entries(id) ON DELETE RESTRICT,
 entry_version bigint NOT NULL CHECK(entry_version > 0),
 ended boolean NOT NULL DEFAULT false,
 secret_hash text NOT NULL UNIQUE CHECK(secret_hash ~ '^[A-F0-9]{64}$'),
 created_at_utc timestamptz NOT NULL,
 expires_at_utc timestamptz NOT NULL CHECK(expires_at_utc > created_at_utc)
);
CREATE INDEX ix_qr_sessions_entry_time ON restaurant_qr_sessions(entry_id,created_at_utc);
CREATE TABLE restaurant_qr_audit (
 id uuid PRIMARY KEY,
 organization_id uuid NOT NULL,
 branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
 entry_id uuid NOT NULL REFERENCES restaurant_qr_entries(id) ON DELETE RESTRICT,
 entry_version bigint NOT NULL,
 action text NOT NULL CHECK(action IN ('qr.created','qr.rotated','qr.revoked')),
 actor_subject_id text NOT NULL CHECK(length(btrim(actor_subject_id)) BETWEEN 1 AND 200),
 occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX ix_qr_audit_owner_time ON restaurant_qr_audit(organization_id,branch_id,occurred_at_utc);
CREATE TRIGGER tr_qr_audit_append_only BEFORE UPDATE OR DELETE ON restaurant_qr_audit
 FOR EACH ROW EXECUTE FUNCTION prevent_branch_management_audit_mutation();
COMMENT ON TABLE restaurant_qr_sessions IS 'Hashed visitor admission only; no customer profile, order or payment authority.';
