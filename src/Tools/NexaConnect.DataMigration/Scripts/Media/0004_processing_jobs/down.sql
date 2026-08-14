DROP INDEX IF EXISTS ix_media_assets_quota;
DROP TABLE IF EXISTS media_processing_jobs;
ALTER TABLE media_object_deletions DROP CONSTRAINT IF EXISTS uq_media_object_deletions_key;
ALTER TABLE media_object_deletions DROP CONSTRAINT IF EXISTS pk_media_object_deletions;
DELETE FROM media_object_deletions a USING media_object_deletions b WHERE a.ctid>b.ctid AND a.asset_id=b.asset_id;
ALTER TABLE media_object_deletions DROP COLUMN IF EXISTS id;
ALTER TABLE media_object_deletions ADD CONSTRAINT media_object_deletions_pkey PRIMARY KEY(asset_id);
