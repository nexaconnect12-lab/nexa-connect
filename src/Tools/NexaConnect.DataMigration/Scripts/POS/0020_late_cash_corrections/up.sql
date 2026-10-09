CREATE TABLE late_cash_corrections(
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,id uuid NOT NULL,work_id uuid NOT NULL,barrier_id uuid NOT NULL,reviewed_version bigint NOT NULL,
 source_event_id uuid NOT NULL,tender_id uuid NOT NULL,order_id uuid NOT NULL,drawer_id uuid NOT NULL,operation_id uuid NOT NULL,request_fingerprint varchar(64) NOT NULL,preview_fingerprint varchar(64) NOT NULL,
 posting_date date NOT NULL,posting_timezone varchar(128) NOT NULL,posting_from_utc timestamptz NOT NULL,posting_to_utc timestamptz NOT NULL,posted_at_utc timestamptz NOT NULL,
 adjustment numeric(19,4) NOT NULL CHECK(adjustment<0),currency text NOT NULL CHECK(currency='THB'),actor varchar(128) NOT NULL,authorization_decision_id uuid NOT NULL,receipt jsonb NOT NULL,
 PRIMARY KEY(organization_id,id),UNIQUE(organization_id,operation_id),UNIQUE(organization_id,work_id),UNIQUE(organization_id,source_event_id),UNIQUE(organization_id,tender_id),UNIQUE(organization_id,order_id),
 FOREIGN KEY(organization_id,work_id) REFERENCES source_late_work(organization_id,work_id),FOREIGN KEY(drawer_id) REFERENCES cash_sessions(id),
 FOREIGN KEY(organization_id,barrier_id,work_id,reviewed_version) REFERENCES source_late_work_reviews(organization_id,barrier_id,work_id,version),
 CHECK(posting_to_utc>posting_from_utc AND posting_to_utc-posting_from_utc<=interval '27 hours'),CHECK(posted_at_utc>=posting_from_utc AND posted_at_utc<posting_to_utc));
CREATE INDEX late_cash_corrections_posted ON late_cash_corrections(organization_id,restaurant_id,branch_id,posted_at_utc,id);
CREATE TABLE late_cash_correction_audit(organization_id uuid NOT NULL,correction_id uuid NOT NULL,actor varchar(128) NOT NULL,authorization_decision_id uuid NOT NULL,occurred_at_utc timestamptz NOT NULL,
 PRIMARY KEY(organization_id,correction_id),FOREIGN KEY(organization_id,correction_id) REFERENCES late_cash_corrections(organization_id,id));
CREATE TRIGGER late_cash_corrections_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON late_cash_corrections FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();
CREATE TRIGGER late_cash_correction_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON late_cash_correction_audit FOR EACH STATEMENT EXECUTE FUNCTION protect_day_barrier_history();
CREATE FUNCTION validate_cash_correction() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('financial-revision:'||NEW.restaurant_id::text||':'||NEW.branch_id::text,0));
 PERFORM pg_advisory_xact_lock(hashtextextended('late-review-case:'||NEW.organization_id::text||':'||NEW.barrier_id::text||':'||NEW.work_id::text,0));
 IF clock_timestamp()<NEW.posting_from_utc OR clock_timestamp()>=NEW.posting_to_utc
 OR NEW.posting_date<>(NEW.posted_at_utc AT TIME ZONE NEW.posting_timezone)::date
 OR NEW.posting_from_utc<>(NEW.posting_date::timestamp AT TIME ZONE NEW.posting_timezone)
 OR NEW.posting_to_utc<>((NEW.posting_date+1)::timestamp AT TIME ZONE NEW.posting_timezone)
 OR NOT EXISTS(SELECT 1 FROM source_late_work w JOIN source_late_work_links l USING(organization_id,event_type,event_id)
  JOIN source_day_barriers b ON b.organization_id=l.organization_id AND b.id=l.barrier_id
  JOIN source_late_work_reviews r ON r.organization_id=w.organization_id AND r.barrier_id=b.id AND r.work_id=w.work_id
  WHERE w.organization_id=NEW.organization_id AND w.work_id=NEW.work_id AND b.id=NEW.barrier_id AND b.phase='committed'
   AND b.restaurant_id=NEW.restaurant_id AND b.branch_id=NEW.branch_id AND b.to_utc<=NEW.posting_from_utc
   AND w.event_type='order.manual-tender-settled.v1' AND w.event_id=NEW.source_event_id::text AND w.payload->>'method'='cash'
   AND (w.payload->>'settlementId')::uuid=NEW.tender_id AND (w.payload->>'orderId')::uuid=NEW.order_id AND -(w.payload->>'amount')::numeric=NEW.adjustment AND w.payload->>'currency'='THB'
   AND NOT EXISTS(SELECT 1 FROM pos_order_settlements p WHERE p.event_id=NEW.source_event_id OR p.settlement_id=NEW.tender_id OR p.order_id=NEW.order_id)
   AND EXISTS(SELECT 1 FROM cash_sessions c JOIN stores s ON s.id=c.store_id JOIN shifts sh ON sh.id=c.shift_id AND sh.store_id=s.id
    WHERE c.id=NEW.drawer_id AND s.restaurant_id=NEW.restaurant_id AND s.branch_id=NEW.branch_id AND c.status='closed' AND btrim(c.currency)='THB'
     AND sh.terminal_id=(w.payload->>'terminalId')::uuid AND c.opened_at_utc<=(w.payload->>'occurredAtUtc')::timestamptz AND c.closed_at_utc>=(w.payload->>'occurredAtUtc')::timestamptz
     AND sh.opened_at_utc<=(w.payload->>'occurredAtUtc')::timestamptz AND (sh.closed_at_utc IS NULL OR sh.closed_at_utc>=(w.payload->>'occurredAtUtc')::timestamptz)
     AND c.closed_at_utc>=b.from_utc AND c.closed_at_utc<b.to_utc)
   AND r.version=NEW.reviewed_version AND r.status='correction_required' AND r.version=(SELECT max(version) FROM source_late_work_reviews WHERE organization_id=r.organization_id AND barrier_id=r.barrier_id AND work_id=r.work_id))
 THEN RAISE EXCEPTION 'Cash correction evidence changed' USING ERRCODE='23514'; END IF;RETURN NEW;
END; $$;
CREATE TRIGGER validate_cash_correction BEFORE INSERT ON late_cash_corrections FOR EACH ROW EXECUTE FUNCTION validate_cash_correction();
CREATE OR REPLACE FUNCTION source_change_blocks_fence(entry jsonb,start_time timestamptz,end_time timestamptz) RETURNS boolean LANGUAGE plpgsql AS $$
 DECLARE item jsonb;kind text;status text;created timestamptz;financial timestamptz; BEGIN
 IF entry IS NULL OR entry->>'version' IS DISTINCT FROM '2' OR COALESCE((entry->>'ownershipUncertain')::boolean,true) OR entry->'before'->>'id' IS NOT NULL AND entry->'after'->>'id' IS NOT NULL AND entry->'before'->>'id'<>entry->'after'->>'id' THEN RETURN true; END IF;
 kind=entry->>'kind';
 IF kind='late_cash_corrections' THEN
  IF (entry->'before' IS NULL OR entry->'before'='null'::jsonb) AND (entry->'after' IS NULL OR entry->'after'='null'::jsonb) THEN RETURN true; END IF;
  FOR item IN SELECT value FROM jsonb_array_elements(jsonb_build_array(entry->'before',entry->'after')) WHERE value IS NOT NULL AND value<>'null'::jsonb LOOP
   IF item->>'id' IS NULL OR (item->>'id')::uuid='00000000-0000-0000-0000-000000000000'::uuid OR item->>'version' IS DISTINCT FROM '1' OR item->>'status' IS DISTINCT FROM 'posted' OR item->>'createdAtUtc' IS NULL OR (item->>'createdAtUtc')::timestamptz='0001-01-01T00:00:00Z'::timestamptz OR item->>'financialAtUtc' IS NULL THEN RETURN true; END IF;
   financial=(item->>'financialAtUtc')::timestamptz;
   IF financial='0001-01-01T00:00:00Z'::timestamptz OR financial>=start_time AND financial<end_time THEN RETURN true; END IF;
  END LOOP;RETURN false;
 END IF;IF kind IS NULL OR (entry->'before' IS NULL OR entry->'before'='null'::jsonb) AND (entry->'after' IS NULL OR entry->'after'='null'::jsonb) THEN RETURN true; END IF;
 FOR item IN SELECT value FROM jsonb_array_elements(jsonb_build_array(entry->'before',entry->'after')) WHERE value IS NOT NULL AND value<>'null'::jsonb LOOP
 IF item->>'id' IS NULL OR (item->>'id')::uuid='00000000-0000-0000-0000-000000000000'::uuid OR COALESCE((item->>'version')::bigint,0)<=0 OR item->>'createdAtUtc' IS NULL OR item->>'status' IS NULL THEN RETURN true; END IF;
 status=item->>'status';created=(item->>'createdAtUtc')::timestamptz;financial=(item->>'financialAtUtc')::timestamptz;
 IF created='0001-01-01T00:00:00Z'::timestamptz OR financial='0001-01-01T00:00:00Z'::timestamptz THEN RETURN true; END IF;
 IF kind NOT IN ('stores','shifts','cash_sessions','cash_movements','cash_session_review_states') THEN RETURN true; END IF;
 IF kind='stores' THEN RETURN NOT (entry->'before' IS NOT NULL AND entry->'before'<>'null'::jsonb AND entry->'after' IS NOT NULL AND entry->'after'<>'null'::jsonb AND entry->'before'->'id'=entry->'after'->'id'); END IF;
 IF kind='shifts' AND status NOT IN ('open','closing','closed','cancelled') OR kind<>'shifts' AND status NOT IN ('open','counting','closed') THEN RETURN true; END IF;
 IF item->>'reviewStatus' IS NOT NULL AND item->>'reviewStatus' NOT IN ('approved','investigating','review_required') THEN RETURN true; END IF;
 IF created<end_time THEN IF kind='shifts' THEN IF status IN ('open','closing') THEN RETURN true; END IF;
 ELSE IF status IN ('open','counting') THEN RETURN true; END IF;
 IF financial IS NULL OR item->>'hasVariance' IS NULL THEN RETURN true; END IF;
 IF financial>=start_time AND financial<end_time THEN RETURN true; END IF;
 IF financial<end_time AND (item->>'hasVariance')::boolean AND (item->>'reviewStatus' IS DISTINCT FROM 'approved' OR item->>'reviewedVersion' IS DISTINCT FROM item->>'version') THEN RETURN true; END IF; END IF; END IF;
 END LOOP; RETURN false;
 EXCEPTION WHEN OTHERS THEN RETURN true; END; $$;
CREATE FUNCTION record_cash_correction_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE next_revision bigint;
BEGIN
 INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(NEW.restaurant_id,NEW.branch_id,1)
 ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1 RETURNING revision INTO next_revision;
 INSERT INTO source_financial_changes(restaurant_id,branch_id,epoch,revision,attribution)
 SELECT NEW.restaurant_id,NEW.branch_id,epoch,next_revision,jsonb_build_object('version',2,'kind','late_cash_corrections','recordId',NEW.id,'operation','INSERT','ownershipUncertain',false,'before',NULL,
 'after',jsonb_build_object('id',NEW.id,'status','posted','version',1,'createdAtUtc',NEW.posted_at_utc,'financialAtUtc',NEW.posted_at_utc)) FROM source_financial_epoch WHERE singleton;RETURN NEW;
END; $$;
CREATE TRIGGER cash_correction_revision AFTER INSERT ON late_cash_corrections FOR EACH ROW EXECUTE FUNCTION record_cash_correction_revision();
