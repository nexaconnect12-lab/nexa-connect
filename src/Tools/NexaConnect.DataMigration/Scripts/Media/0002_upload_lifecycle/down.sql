DROP INDEX IF EXISTS ix_media_assets_pending_expiry;
ALTER TABLE media_assets DROP COLUMN IF EXISTS upload_expires_at_utc;
