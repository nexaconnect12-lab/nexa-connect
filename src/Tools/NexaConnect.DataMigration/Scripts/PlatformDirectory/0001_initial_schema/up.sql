CREATE TABLE organizations
(
    id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    default_time_zone text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_organizations PRIMARY KEY (id),
    CONSTRAINT uq_organizations_code UNIQUE (code),
    CONSTRAINT ck_organizations_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_organizations_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_organizations_status
        CHECK (status IN ('pending', 'active', 'suspended', 'closed')),
    CONSTRAINT ck_organizations_default_time_zone
        CHECK (char_length(btrim(default_time_zone)) > 0),
    CONSTRAINT ck_organizations_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_organizations_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_organizations_status_name
    ON organizations (status, name, id);

CREATE TABLE organization_memberships
(
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    identity_subject_id text NOT NULL,
    status text NOT NULL,
    invited_at_utc timestamptz NULL,
    joined_at_utc timestamptz NULL,
    suspended_at_utc timestamptz NULL,
    removed_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_organization_memberships PRIMARY KEY (id),
    CONSTRAINT fk_organization_memberships_organizations_organization_id
        FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT,
    CONSTRAINT uq_organization_memberships_organization_id_identity_subject_id
        UNIQUE (organization_id, identity_subject_id),
    CONSTRAINT ck_organization_memberships_identity_subject_id
        CHECK (char_length(btrim(identity_subject_id)) > 0),
    CONSTRAINT ck_organization_memberships_status
        CHECK (status IN ('invited', 'active', 'suspended', 'removed')),
    CONSTRAINT ck_organization_memberships_joined_at
        CHECK (status <> 'active' OR joined_at_utc IS NOT NULL),
    CONSTRAINT ck_organization_memberships_suspended_at
        CHECK (status <> 'suspended' OR suspended_at_utc IS NOT NULL),
    CONSTRAINT ck_organization_memberships_removed_at
        CHECK ((status = 'removed') = (removed_at_utc IS NOT NULL)),
    CONSTRAINT ck_organization_memberships_lifecycle_timestamps
        CHECK
        (
            (joined_at_utc IS NULL OR invited_at_utc IS NULL OR joined_at_utc >= invited_at_utc)
            AND
            (suspended_at_utc IS NULL OR joined_at_utc IS NULL OR suspended_at_utc >= joined_at_utc)
            AND
            (removed_at_utc IS NULL OR invited_at_utc IS NULL OR removed_at_utc >= invited_at_utc)
        ),
    CONSTRAINT ck_organization_memberships_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_organization_memberships_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_organization_memberships_identity_subject_id_status
    ON organization_memberships (identity_subject_id, status, organization_id);

CREATE INDEX ix_organization_memberships_organization_id_status
    ON organization_memberships (organization_id, status, id);

CREATE TABLE applications
(
    id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_applications PRIMARY KEY (id),
    CONSTRAINT uq_applications_code UNIQUE (code),
    CONSTRAINT ck_applications_code
        CHECK (code ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_applications_name
        CHECK (char_length(btrim(name)) > 0),
    CONSTRAINT ck_applications_status
        CHECK (status IN ('active', 'suspended', 'retired')),
    CONSTRAINT ck_applications_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_applications_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_applications_status_name
    ON applications (status, name, id);

CREATE TABLE organization_application_access
(
    organization_id uuid NOT NULL,
    application_id uuid NOT NULL,
    status text NOT NULL,
    enabled_at_utc timestamptz NOT NULL,
    suspended_at_utc timestamptz NULL,
    disabled_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    updated_by text NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_organization_application_access
        PRIMARY KEY (organization_id, application_id),
    CONSTRAINT fk_org_app_access_organizations_org_id
        FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT,
    CONSTRAINT fk_organization_application_access_applications_application_id
        FOREIGN KEY (application_id) REFERENCES applications (id) ON DELETE RESTRICT,
    CONSTRAINT ck_organization_application_access_status
        CHECK (status IN ('enabled', 'suspended', 'disabled')),
    CONSTRAINT ck_organization_application_access_suspended_at
        CHECK (status <> 'suspended' OR suspended_at_utc IS NOT NULL),
    CONSTRAINT ck_organization_application_access_disabled_at
        CHECK ((status = 'disabled') = (disabled_at_utc IS NOT NULL)),
    CONSTRAINT ck_organization_application_access_lifecycle_timestamps
        CHECK
        (
            (suspended_at_utc IS NULL OR suspended_at_utc >= enabled_at_utc)
            AND
            (disabled_at_utc IS NULL OR disabled_at_utc >= enabled_at_utc)
        ),
    CONSTRAINT ck_organization_application_access_audit_timestamps
        CHECK (updated_at_utc >= created_at_utc),
    CONSTRAINT ck_organization_application_access_concurrency_version
        CHECK (concurrency_version > 0)
);

CREATE INDEX ix_organization_application_access_application_id_status
    ON organization_application_access (application_id, status, organization_id);

CREATE INDEX ix_organization_application_access_organization_id_status
    ON organization_application_access (organization_id, status, application_id);

CREATE TABLE outbox_messages
(
    id uuid NOT NULL,
    event_type text NOT NULL,
    contract_version integer NOT NULL,
    aggregate_type text NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    correlation_id text NULL,
    causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL,
    published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0,
    next_attempt_at_utc timestamptz NULL,
    last_error_category text NULL,
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id),
    CONSTRAINT ck_outbox_messages_event_type
        CHECK (char_length(btrim(event_type)) > 0),
    CONSTRAINT ck_outbox_messages_contract_version
        CHECK (contract_version > 0),
    CONSTRAINT ck_outbox_messages_aggregate_type
        CHECK (char_length(btrim(aggregate_type)) > 0),
    CONSTRAINT ck_outbox_messages_payload
        CHECK (jsonb_typeof(payload) = 'object'),
    CONSTRAINT ck_outbox_messages_retry_count
        CHECK (retry_count >= 0),
    CONSTRAINT ck_outbox_messages_publish_timestamp
        CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);

CREATE INDEX ix_outbox_messages_unpublished
    ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id)
    WHERE published_at_utc IS NULL;

COMMENT ON TABLE organizations IS
    'Owned by Platform Directory. Other projects consume this data only through APIs or versioned events.';

COMMENT ON COLUMN organization_memberships.identity_subject_id IS
    'Stable Keycloak subject identifier; credentials and authentication data remain owned by Keycloak.';

COMMENT ON TABLE applications IS
    'Cross-product application registry. Product-specific roles and permissions do not belong here.';
