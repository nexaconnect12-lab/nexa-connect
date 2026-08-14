CREATE TABLE media_object_deletions
(
    asset_id uuid PRIMARY KEY REFERENCES media_assets(id),
    organization_id uuid NOT NULL,
    object_key text NOT NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at_utc timestamptz NOT NULL,
    last_error varchar(200),
    created_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_media_object_deletions_due ON media_object_deletions(next_attempt_at_utc);
