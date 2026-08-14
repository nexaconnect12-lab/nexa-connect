CREATE TABLE media_processing_jobs
(
    asset_id uuid PRIMARY KEY REFERENCES media_assets(id),
    organization_id uuid NOT NULL,
    object_key text NOT NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at_utc timestamptz NOT NULL,
    last_error varchar(200),
    created_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_media_processing_jobs_due ON media_processing_jobs(next_attempt_at_utc) WHERE attempts < 10;
CREATE INDEX ix_media_assets_quota ON media_assets(organization_id,processing_status) WHERE deleted_at_utc IS NULL;
ALTER TABLE media_object_deletions DROP CONSTRAINT media_object_deletions_pkey;
ALTER TABLE media_object_deletions ADD COLUMN id uuid NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE media_object_deletions ADD CONSTRAINT pk_media_object_deletions PRIMARY KEY(id);
ALTER TABLE media_object_deletions ADD CONSTRAINT uq_media_object_deletions_key UNIQUE(object_key);
