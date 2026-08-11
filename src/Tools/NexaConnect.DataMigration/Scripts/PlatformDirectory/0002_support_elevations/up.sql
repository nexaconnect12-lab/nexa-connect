CREATE TABLE support_elevations
(
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    application_code text NOT NULL,
    support_subject_id text NOT NULL,
    reason text NOT NULL,
    duration_minutes integer NOT NULL,
    status text NOT NULL,
    requested_at_utc timestamptz NOT NULL,
    approved_at_utc timestamptz NULL,
    expires_at_utc timestamptz NULL,
    revoked_at_utc timestamptz NULL,
    approved_by_subject_id text NULL,
    revoked_by_subject_id text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_support_elevations PRIMARY KEY (id),
    CONSTRAINT fk_support_elevations_organizations FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT,
    CONSTRAINT ck_support_elevations_application_code CHECK (char_length(btrim(application_code)) > 0),
    CONSTRAINT ck_support_elevations_support_subject CHECK (char_length(btrim(support_subject_id)) > 0),
    CONSTRAINT ck_support_elevations_reason CHECK (char_length(btrim(reason)) >= 10),
    CONSTRAINT ck_support_elevations_duration CHECK (duration_minutes BETWEEN 5 AND 240),
    CONSTRAINT ck_support_elevations_status CHECK (status IN ('pending', 'active', 'revoked')),
    CONSTRAINT ck_support_elevations_approval CHECK
    (
        (status = 'pending' AND approved_at_utc IS NULL AND expires_at_utc IS NULL AND approved_by_subject_id IS NULL)
        OR
        (status IN ('active', 'revoked') AND approved_at_utc IS NOT NULL AND expires_at_utc > approved_at_utc AND approved_by_subject_id IS NOT NULL)
        OR
        (status = 'revoked' AND approved_at_utc IS NULL AND expires_at_utc IS NULL AND approved_by_subject_id IS NULL)
    ),
    CONSTRAINT ck_support_elevations_revocation CHECK
    (
        (status <> 'revoked' AND revoked_at_utc IS NULL AND revoked_by_subject_id IS NULL)
        OR
        (status = 'revoked' AND revoked_at_utc IS NOT NULL AND revoked_by_subject_id IS NOT NULL)
    ),
    CONSTRAINT ck_support_elevations_separation_of_duties CHECK
        (approved_by_subject_id IS NULL OR approved_by_subject_id <> support_subject_id),
    CONSTRAINT ck_support_elevations_audit_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_support_elevations_effective
    ON support_elevations (support_subject_id, organization_id, application_code, expires_at_utc)
    WHERE status = 'active';

CREATE INDEX ix_support_elevations_pending
    ON support_elevations (requested_at_utc, id)
    WHERE status = 'pending';

CREATE TABLE support_elevation_audit
(
    id uuid NOT NULL,
    support_elevation_id uuid NOT NULL,
    action text NOT NULL,
    actor_subject_id text NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_support_elevation_audit PRIMARY KEY (id),
    CONSTRAINT fk_support_elevation_audit_elevation FOREIGN KEY (support_elevation_id) REFERENCES support_elevations (id) ON DELETE RESTRICT,
    CONSTRAINT ck_support_elevation_audit_action CHECK (action IN ('requested', 'approved', 'revoked')),
    CONSTRAINT ck_support_elevation_audit_actor CHECK (char_length(btrim(actor_subject_id)) > 0)
);

CREATE INDEX ix_support_elevation_audit_elevation
    ON support_elevation_audit (support_elevation_id, occurred_at_utc, id);

CREATE FUNCTION prevent_support_elevation_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'support_elevation_audit is append-only';
END;
$$;

CREATE TRIGGER tr_support_elevation_audit_append_only
BEFORE UPDATE OR DELETE ON support_elevation_audit
FOR EACH ROW EXECUTE FUNCTION prevent_support_elevation_audit_mutation();

COMMENT ON TABLE support_elevations IS
    'Approved, time-limited platform support access. Effective access requires active status and an unexpired expires_at_utc.';

COMMENT ON TABLE support_elevation_audit IS
    'Append-only audit history for support elevation request, approval, and revocation actions.';
