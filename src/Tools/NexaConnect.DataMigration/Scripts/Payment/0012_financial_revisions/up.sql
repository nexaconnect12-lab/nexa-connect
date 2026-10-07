CREATE TABLE source_financial_epoch (
 singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton), epoch uuid NOT NULL DEFAULT gen_random_uuid()
);
INSERT INTO source_financial_epoch(singleton) VALUES(true);
CREATE TABLE source_financial_revisions (
 restaurant_id uuid NOT NULL, branch_id uuid NOT NULL, revision bigint NOT NULL CHECK(revision>0),
 PRIMARY KEY(restaurant_id,branch_id)
);
CREATE FUNCTION protect_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='source_financial_epoch' OR TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth()<2 THEN
  RAISE EXCEPTION 'Financial revision evidence is source-managed';
 END IF;
 IF TG_OP='UPDATE' AND (NEW.restaurant_id<>OLD.restaurant_id OR NEW.branch_id<>OLD.branch_id OR NEW.revision<>OLD.revision+1) THEN
  RAISE EXCEPTION 'Financial revisions must advance monotonically';
 END IF;
 IF TG_OP='INSERT' AND NEW.revision<>1 THEN RAISE EXCEPTION 'Invalid initial financial revision'; END IF;
 RETURN NEW;
END; $$;
CREATE TRIGGER financial_epoch_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_financial_epoch
 FOR EACH STATEMENT EXECUTE FUNCTION protect_financial_revision();
CREATE TRIGGER financial_revision_guard BEFORE INSERT OR UPDATE OR DELETE ON source_financial_revisions
 FOR EACH ROW EXECUTE FUNCTION protect_financial_revision();
CREATE TRIGGER financial_revision_no_truncate BEFORE TRUNCATE ON source_financial_revisions
 FOR EACH STATEMENT EXECUTE FUNCTION protect_financial_revision();
CREATE FUNCTION reject_financial_source_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Financial source truncation would bypass revision evidence'; END; $$;
CREATE FUNCTION advance_source_financial_revision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item jsonb; r uuid; b uuid; scopes jsonb='[]'::jsonb; scope record;
BEGIN
 IF TG_OP='UPDATE' AND NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 -- Retain both scopes on reassignment; sort keys before locking to reduce deadlocks.
 FOR item IN SELECT value FROM jsonb_array_elements(
   CASE TG_OP WHEN 'INSERT' THEN jsonb_build_array(to_jsonb(NEW))
     WHEN 'DELETE' THEN jsonb_build_array(to_jsonb(OLD))
     ELSE jsonb_build_array(to_jsonb(OLD),to_jsonb(NEW)) END)
 LOOP
  r=NULL; b=NULL;
  IF TG_NARGS=0 THEN r=(item->>'restaurant_id')::uuid; b=(item->>'branch_id')::uuid;
  ELSE
   SELECT restaurant_id,branch_id INTO r,b FROM refunds WHERE id=(item->>TG_ARGV[0])::uuid;
  END IF;
  IF r IS NOT NULL AND b IS NOT NULL THEN scopes=scopes||jsonb_build_array(jsonb_build_object('r',r,'b',b)); END IF;
 END LOOP;
 FOR scope IN SELECT DISTINCT (value->>'r')::uuid AS r,(value->>'b')::uuid AS b
   FROM jsonb_array_elements(scopes) ORDER BY r,b
 LOOP
  INSERT INTO source_financial_revisions(restaurant_id,branch_id,revision) VALUES(scope.r,scope.b,1)
   ON CONFLICT(restaurant_id,branch_id) DO UPDATE SET revision=source_financial_revisions.revision+1;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END; $$;
CREATE TRIGGER financial_revision_change AFTER INSERT OR UPDATE OR DELETE ON payment_intents FOR EACH ROW EXECUTE FUNCTION advance_source_financial_revision();
CREATE TRIGGER financial_revision_no_truncate BEFORE TRUNCATE ON payment_intents FOR EACH STATEMENT EXECUTE FUNCTION reject_financial_source_truncate();
CREATE TRIGGER financial_revision_change AFTER INSERT OR UPDATE OR DELETE ON refunds FOR EACH ROW EXECUTE FUNCTION advance_source_financial_revision();
CREATE TRIGGER financial_revision_no_truncate BEFORE TRUNCATE ON refunds FOR EACH STATEMENT EXECUTE FUNCTION reject_financial_source_truncate();
CREATE TRIGGER financial_revision_change AFTER INSERT OR UPDATE OR DELETE ON refund_financial_publications FOR EACH ROW EXECUTE FUNCTION advance_source_financial_revision('refund_id');
CREATE TRIGGER financial_revision_no_truncate BEFORE TRUNCATE ON refund_financial_publications FOR EACH STATEMENT EXECUTE FUNCTION reject_financial_source_truncate();
COMMENT ON TABLE source_financial_revisions IS 'Owning-source branch revisions. All dates invalidate conservatively; increments commit or roll back with financial mutations.';
