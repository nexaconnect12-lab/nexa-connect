DO $$ BEGIN IF EXISTS(SELECT 1 FROM source_late_work_reviews) OR EXISTS(SELECT 1 FROM source_late_work) THEN RAISE EXCEPTION 'Late-work review identity/history requires forward recovery'; END IF; END; $$;
DROP TABLE source_late_work_reviews;
DROP FUNCTION validate_late_review_append();DROP INDEX source_late_work_received;
ALTER TABLE source_late_work DROP CONSTRAINT source_late_work_identity;ALTER TABLE source_late_work DROP COLUMN work_id;
