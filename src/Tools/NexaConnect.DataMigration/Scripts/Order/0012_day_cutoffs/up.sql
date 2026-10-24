CREATE TABLE source_day_cutoffs (
 organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
 from_utc timestamptz NOT NULL, to_utc timestamptz NOT NULL,
 id uuid NOT NULL, operation_id uuid NOT NULL, generation bigint NOT NULL CHECK(generation>0),
 fingerprint varchar(64) NOT NULL, actor varchar(128) NOT NULL, payload jsonb NOT NULL,
 PRIMARY KEY(organization_id,id), UNIQUE(organization_id,operation_id),
 UNIQUE(organization_id,restaurant_id,branch_id,from_utc,to_utc,generation),
 CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '27 hours'),
 CHECK((payload->>'manifestId')::uuid=id AND (payload->>'operationId')::uuid=operation_id
   AND (payload->>'generation')::bigint=generation)
);
CREATE FUNCTION protect_source_day_cutoffs() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Source day-cutoff history is immutable'; END; $$;
CREATE TRIGGER source_day_cutoffs_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON source_day_cutoffs
FOR EACH STATEMENT EXECUTE FUNCTION protect_source_day_cutoffs();
