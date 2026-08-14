ALTER TABLE media_assets ADD COLUMN upload_expires_at_utc timestamptz NULL;
CREATE INDEX ix_media_assets_pending_expiry ON media_assets(upload_expires_at_utc,id) WHERE processing_status='pending';
