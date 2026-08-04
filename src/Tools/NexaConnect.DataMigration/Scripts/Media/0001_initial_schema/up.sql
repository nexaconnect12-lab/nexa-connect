CREATE TABLE media_assets
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    owner_service text NOT NULL,
    owner_type text NOT NULL,
    owner_id uuid NOT NULL,
    object_key text NOT NULL,
    original_file_name text NOT NULL,
    content_type text NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    checksum_sha256 text NOT NULL,
    width_pixels integer NULL CHECK (width_pixels IS NULL OR width_pixels > 0),
    height_pixels integer NULL CHECK (height_pixels IS NULL OR height_pixels > 0),
    processing_status text NOT NULL,
    uploaded_at_utc timestamptz NOT NULL,
    processed_at_utc timestamptz NULL,
    deleted_at_utc timestamptz NULL,
    created_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    concurrency_version bigint NOT NULL DEFAULT 1 CHECK (concurrency_version > 0),
    CONSTRAINT uq_media_assets_object_key UNIQUE (object_key),
    CONSTRAINT ck_media_assets_owner CHECK (char_length(btrim(owner_service)) > 0 AND char_length(btrim(owner_type)) > 0),
    CONSTRAINT ck_media_assets_file CHECK (char_length(btrim(original_file_name)) > 0 AND char_length(btrim(content_type)) > 0),
    CONSTRAINT ck_media_assets_checksum CHECK (checksum_sha256 ~ '^[0-9A-Fa-f]{64}$'),
    CONSTRAINT ck_media_assets_status CHECK (processing_status IN ('pending', 'processing', 'ready', 'failed', 'quarantined', 'deleted')),
    CONSTRAINT ck_media_assets_deleted CHECK ((processing_status = 'deleted') = (deleted_at_utc IS NOT NULL)),
    CONSTRAINT ck_media_assets_updated CHECK (updated_at_utc >= uploaded_at_utc)
);

CREATE INDEX ix_media_assets_owner ON media_assets (organization_id, owner_service, owner_type, owner_id, processing_status);
CREATE INDEX ix_media_assets_checksum ON media_assets (organization_id, checksum_sha256);

CREATE TABLE media_variants
(
    id uuid PRIMARY KEY,
    media_asset_id uuid NOT NULL,
    variant_name text NOT NULL,
    object_key text NOT NULL,
    content_type text NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    checksum_sha256 text NOT NULL,
    width_pixels integer NULL CHECK (width_pixels IS NULL OR width_pixels > 0),
    height_pixels integer NULL CHECK (height_pixels IS NULL OR height_pixels > 0),
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_media_variants_asset_name UNIQUE (media_asset_id, variant_name),
    CONSTRAINT uq_media_variants_object_key UNIQUE (object_key),
    CONSTRAINT fk_media_variants_media_assets_media_asset_id
        FOREIGN KEY (media_asset_id) REFERENCES media_assets (id) ON DELETE RESTRICT,
    CONSTRAINT ck_media_variants_name CHECK (variant_name ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CONSTRAINT ck_media_variants_content_type CHECK (char_length(btrim(content_type)) > 0),
    CONSTRAINT ck_media_variants_checksum CHECK (checksum_sha256 ~ '^[0-9A-Fa-f]{64}$'),
    CONSTRAINT ck_media_variants_status CHECK (status IN ('pending', 'ready', 'failed', 'deleted'))
);

CREATE INDEX ix_media_variants_asset_status ON media_variants (media_asset_id, status, variant_name);

CREATE TABLE media_processing_attempts
(
    id uuid PRIMARY KEY,
    media_asset_id uuid NOT NULL,
    attempt_number integer NOT NULL CHECK (attempt_number > 0),
    worker_id text NULL,
    outcome text NOT NULL CHECK (outcome IN ('started', 'succeeded', 'failed', 'abandoned')),
    error_category text NULL,
    error_message text NULL,
    started_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    CONSTRAINT uq_media_processing_attempts_asset_attempt UNIQUE (media_asset_id, attempt_number),
    CONSTRAINT fk_media_processing_attempts_media_assets_media_asset_id
        FOREIGN KEY (media_asset_id) REFERENCES media_assets (id) ON DELETE RESTRICT,
    CONSTRAINT ck_media_processing_attempts_completed CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
);

CREATE INDEX ix_media_processing_attempts_asset_started ON media_processing_attempts (media_asset_id, started_at_utc DESC);

CREATE TABLE outbox_messages
(
    id uuid PRIMARY KEY, event_type text NOT NULL, contract_version integer NOT NULL CHECK (contract_version > 0),
    aggregate_type text NOT NULL, aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'), correlation_id text NULL, causation_id text NULL,
    occurred_at_utc timestamptz NOT NULL, published_at_utc timestamptz NULL,
    retry_count integer NOT NULL DEFAULT 0 CHECK (retry_count >= 0), next_attempt_at_utc timestamptz NULL, last_error_category text NULL,
    CONSTRAINT ck_outbox_messages_published CHECK (published_at_utc IS NULL OR published_at_utc >= occurred_at_utc)
);
CREATE INDEX ix_outbox_messages_unpublished ON outbox_messages (next_attempt_at_utc, occurred_at_utc, id) WHERE published_at_utc IS NULL;

COMMENT ON TABLE media_assets IS 'Metadata only. Media bytes belong in S3-compatible object storage.';
COMMENT ON COLUMN media_assets.organization_id IS 'External Platform Directory identifier; no cross-database foreign key.';
