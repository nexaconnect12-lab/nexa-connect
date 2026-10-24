CREATE TABLE source_day_fences(
 organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,from_utc timestamptz NOT NULL,to_utc timestamptz NOT NULL,
 operation_id uuid NOT NULL,approval_id uuid NOT NULL,seal_id uuid NOT NULL,expires_at_utc timestamptz NOT NULL,command_hash varchar(64) NOT NULL,
 actor varchar(128) NOT NULL,cancelled boolean NOT NULL,acquired_revision jsonb,PRIMARY KEY(organization_id,operation_id),
 CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '27 hours'));
 CREATE FUNCTION protect_source_day_fence_state() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Source fence history is retained'; END IF;
 IF TG_OP='UPDATE' AND (OLD.cancelled OR NOT NEW.cancelled OR to_jsonb(NEW)-'cancelled' IS DISTINCT FROM to_jsonb(OLD)-'cancelled') THEN RAISE EXCEPTION 'Source fence binding is immutable; cancellation is irreversible'; END IF;RETURN NEW; END; $$;
 CREATE TRIGGER source_day_fence_state_guard BEFORE UPDATE OR DELETE ON source_day_fences FOR EACH ROW EXECUTE FUNCTION protect_source_day_fence_state();
 CREATE TRIGGER source_day_fence_no_truncate BEFORE TRUNCATE ON source_day_fences FOR EACH STATEMENT EXECUTE FUNCTION protect_source_day_fence_state();
 CREATE INDEX source_day_fences_window ON source_day_fences(restaurant_id,branch_id,from_utc,to_utc) WHERE NOT cancelled;
 CREATE TABLE source_day_fence_audit(organization_id uuid NOT NULL,operation_id uuid NOT NULL,action text NOT NULL CHECK(action IN('acquire','cancel')),
 actor varchar(128) NOT NULL,occurred_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),command jsonb NOT NULL,PRIMARY KEY(organization_id,operation_id,action));
 CREATE FUNCTION protect_source_day_fence_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Fence audit is immutable'; END; $$;
 CREATE TRIGGER source_day_fence_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_day_fence_audit FOR EACH STATEMENT EXECUTE FUNCTION protect_source_day_fence_audit();
 ALTER TABLE source_financial_revisions ADD COLUMN fence_generation bigint NOT NULL DEFAULT 0 CHECK(fence_generation>=0);
 ALTER TABLE source_financial_revisions DROP CONSTRAINT source_financial_revisions_revision_check;
 ALTER TABLE source_financial_revisions ADD CONSTRAINT source_financial_revisions_revision_check CHECK(revision>0 OR revision=0 AND fence_generation>0);
 CREATE OR REPLACE FUNCTION protect_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_TABLE_NAME='source_financial_epoch' OR TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth()<2 THEN RAISE EXCEPTION 'Financial revision evidence is source-managed'; END IF;
 IF TG_OP='UPDATE' AND (NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NOT (NEW.revision=OLD.revision+1 AND NEW.fence_generation=OLD.fence_generation OR NEW.revision=OLD.revision AND NEW.fence_generation=OLD.fence_generation+1)) THEN RAISE EXCEPTION 'Financial revisions must advance monotonically'; END IF;
 IF TG_OP='INSERT' AND NOT (NEW.revision=1 AND NEW.fence_generation=0 OR NEW.revision=0 AND NEW.fence_generation=1) THEN RAISE EXCEPTION 'Invalid initial financial revision'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('financial-revision:'||NEW.restaurant_id::text||':'||NEW.branch_id::text,0));RETURN NEW; END; $$;
 CREATE FUNCTION advance_source_fence_generation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision,fence_generation) VALUES(NEW.restaurant_id,NEW.branch_id,0,1)
 ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET fence_generation=source_financial_revisions.fence_generation+1;RETURN NEW; END; $$;
 CREATE TRIGGER source_fence_generation AFTER INSERT OR UPDATE ON source_day_fences FOR EACH ROW EXECUTE FUNCTION advance_source_fence_generation();
 -- Database backstop mirrors the service-owned FinancialChange/FinancialDayFence selection.
 CREATE FUNCTION source_change_blocks_fence(entry jsonb,start_time timestamptz,end_time timestamptz) RETURNS boolean LANGUAGE plpgsql AS $$
 DECLARE item jsonb;kind text;status text;created timestamptz;financial timestamptz; BEGIN
 IF entry IS NULL OR entry->>'version' IS DISTINCT FROM '2' OR COALESCE((entry->>'ownershipUncertain')::boolean,true) OR entry->'before'->>'id' IS NOT NULL AND entry->'after'->>'id' IS NOT NULL AND entry->'before'->>'id'<>entry->'after'->>'id' THEN RETURN true; END IF;
 kind=entry->>'kind';IF kind IS NULL OR (entry->'before' IS NULL OR entry->'before'='null'::jsonb) AND (entry->'after' IS NULL OR entry->'after'='null'::jsonb) THEN RETURN true; END IF;
 FOR item IN SELECT value FROM jsonb_array_elements(jsonb_build_array(entry->'before',entry->'after')) WHERE value IS NOT NULL AND value<>'null'::jsonb LOOP
 IF item->>'id' IS NULL OR (item->>'id')::uuid='00000000-0000-0000-0000-000000000000'::uuid OR COALESCE((item->>'version')::bigint,0)<=0 OR item->>'createdAtUtc' IS NULL OR item->>'status' IS NULL THEN RETURN true; END IF;
 status=item->>'status';created=(item->>'createdAtUtc')::timestamptz;financial=(item->>'financialAtUtc')::timestamptz;
 IF created='0001-01-01T00:00:00Z'::timestamptz OR financial='0001-01-01T00:00:00Z'::timestamptz THEN RETURN true; END IF;
 IF kind NOT IN ('payment_intents','refunds','refund_financial_publications') THEN RETURN true; END IF;
 IF kind='payment_intents' THEN
 IF status NOT IN ('pending','authorizing','unknown','requires_action','authorized','capturing','capture_unknown','captured','failed','cancelled','expired','voiding','void_unknown','voided','void_failed') THEN RETURN true; END IF;
 IF created<end_time AND status NOT IN ('captured','failed','cancelled','expired','voided') THEN RETURN true; END IF;
 ELSE
 IF status NOT IN ('processing','refund_unknown','review_required','completed','failed') THEN RETURN true; END IF;
 IF created<end_time AND status NOT IN ('completed','failed') THEN RETURN true; END IF;
 IF status='completed' THEN IF financial IS NULL OR NOT COALESCE((item->>'hasReceipt')::boolean,false) THEN RETURN true; END IF;
 IF financial>=start_time AND financial<end_time THEN RETURN true; END IF; END IF; END IF;
 END LOOP; RETURN false;
 EXCEPTION WHEN OTHERS THEN RETURN true; END; $$;
 CREATE FUNCTION enforce_source_day_fence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM source_day_fences f WHERE f.restaurant_id=NEW.restaurant_id AND f.branch_id=NEW.branch_id AND NOT f.cancelled
 AND f.expires_at_utc>clock_timestamp() AND source_change_blocks_fence(to_jsonb(NEW)->'attribution',f.from_utc,f.to_utc)) THEN RAISE EXCEPTION 'financial_day_fenced'; END IF;RETURN NEW; END; $$;
 CREATE TRIGGER source_day_fence_write_guard BEFORE INSERT ON source_financial_changes FOR EACH ROW EXECUTE FUNCTION enforce_source_day_fence();
 