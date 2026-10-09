ALTER TABLE source_late_work ADD COLUMN work_id uuid NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE source_late_work ADD CONSTRAINT source_late_work_identity UNIQUE(organization_id,work_id);
CREATE INDEX source_late_work_received ON source_late_work(organization_id,received_at_utc,work_id);
CREATE TABLE source_late_work_reviews (
 organization_id uuid NOT NULL,barrier_id uuid NOT NULL,work_id uuid NOT NULL,version bigint NOT NULL CHECK(version>0),
 status text NOT NULL CHECK(status IN('investigating','correction_required','reviewed')),decision text NOT NULL CHECK(decision IN('investigate','require_correction','acknowledge')),
 reason_code text NOT NULL CHECK(reason_code IN('investigate_delivery','correction_needed','evidence_checked')),operation_id uuid NOT NULL,fingerprint varchar(64) NOT NULL,
 subject_id varchar(128) NOT NULL,authorization_decision_id uuid NOT NULL,reviewed_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(organization_id,barrier_id,work_id,version),UNIQUE(organization_id,operation_id),
 FOREIGN KEY(organization_id,work_id) REFERENCES source_late_work(organization_id,work_id),
 FOREIGN KEY(organization_id,barrier_id) REFERENCES source_day_barriers(organization_id,id));
CREATE TRIGGER source_late_reviews_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_late_work_reviews FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();

ALTER TABLE source_late_work_reviews ADD CONSTRAINT source_late_review_decision_pair CHECK (
 (decision='investigate' AND reason_code='investigate_delivery' AND status='investigating') OR
 (decision='require_correction' AND reason_code='correction_needed' AND status='correction_required') OR
 (decision='acknowledge' AND reason_code='evidence_checked' AND status='reviewed'));
CREATE FUNCTION validate_late_review_append() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('late-review-case:'||NEW.organization_id::text||':'||NEW.barrier_id::text||':'||NEW.work_id::text,0));
 IF NOT EXISTS(SELECT 1 FROM source_late_work w JOIN source_late_work_links l USING(organization_id,event_type,event_id)
 JOIN source_day_barriers b ON b.organization_id=l.organization_id AND b.id=l.barrier_id
 WHERE w.organization_id=NEW.organization_id AND w.work_id=NEW.work_id AND b.id=NEW.barrier_id AND b.phase='committed')
 OR NEW.version<>(SELECT COALESCE(max(version),0)+1 FROM source_late_work_reviews WHERE organization_id=NEW.organization_id AND barrier_id=NEW.barrier_id AND work_id=NEW.work_id)
 THEN RAISE EXCEPTION 'Invalid late-work review append' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END; $$;
CREATE TRIGGER source_late_review_append BEFORE INSERT ON source_late_work_reviews FOR EACH ROW EXECUTE FUNCTION validate_late_review_append();
