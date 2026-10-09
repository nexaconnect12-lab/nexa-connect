DO $$ BEGIN IF EXISTS(SELECT 1 FROM late_cash_corrections) THEN RAISE EXCEPTION 'Cash correction history requires forward recovery'; END IF; END; $$;
DROP TABLE late_cash_correction_audit;DROP TABLE late_cash_corrections;DROP FUNCTION validate_cash_correction();DROP FUNCTION record_cash_correction_revision();
CREATE OR REPLACE FUNCTION source_change_blocks_fence(entry jsonb,start_time timestamptz,end_time timestamptz) RETURNS boolean LANGUAGE plpgsql AS $$
 DECLARE item jsonb;kind text;status text;created timestamptz;financial timestamptz; BEGIN
 IF entry IS NULL OR entry->>'version' IS DISTINCT FROM '2' OR COALESCE((entry->>'ownershipUncertain')::boolean,true) OR entry->'before'->>'id' IS NOT NULL AND entry->'after'->>'id' IS NOT NULL AND entry->'before'->>'id'<>entry->'after'->>'id' THEN RETURN true; END IF;
 kind=entry->>'kind';IF kind IS NULL OR (entry->'before' IS NULL OR entry->'before'='null'::jsonb) AND (entry->'after' IS NULL OR entry->'after'='null'::jsonb) THEN RETURN true; END IF;
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
