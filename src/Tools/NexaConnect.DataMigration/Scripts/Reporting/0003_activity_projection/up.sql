CREATE TABLE activity_records
(
    event_id uuid NOT NULL,
    organization_id uuid NOT NULL,
    application_code text NOT NULL,
    source_service text NOT NULL,
    actor_subject_id text NOT NULL,
    action text NOT NULL,
    resource_type text NOT NULL,
    resource_id text NOT NULL,
    outcome text NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    projected_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_activity_records PRIMARY KEY (event_id),
    CONSTRAINT ck_activity_records_text CHECK (application_code='nexa_connect' AND char_length(source_service) BETWEEN 1 AND 64 AND char_length(actor_subject_id) BETWEEN 1 AND 200 AND char_length(resource_id) BETWEEN 1 AND 300),
    CONSTRAINT ck_activity_records_action CHECK (action IN ('customer-membership.changed','branch.created','branch.updated','branch.configuration.updated','media.asset.created','media.asset.deleted')),
    CONSTRAINT ck_activity_records_resource CHECK (resource_type IN ('organization-membership','branch','branch-configuration','media-asset')),
    CONSTRAINT ck_activity_records_outcome CHECK (outcome IN ('succeeded','failed','denied'))
);
CREATE INDEX ix_activity_records_tenant_time ON activity_records (organization_id, application_code, occurred_at_utc DESC, event_id DESC);
COMMENT ON TABLE activity_records IS 'Rebuildable, tenant-scoped safe audit projection. Contains identifiers and outcomes, never request bodies or credentials.';
