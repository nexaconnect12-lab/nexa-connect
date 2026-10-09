CREATE TABLE financial_completeness_checks
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    from_utc timestamptz NOT NULL,
    to_utc timestamptz NOT NULL CHECK(to_utc>from_utc AND to_utc-from_utc<=interval '31 days'),
    checked_at_utc timestamptz NOT NULL,
    actor text NOT NULL CHECK(length(btrim(actor)) BETWEEN 1 AND 128),
    result jsonb NOT NULL CHECK(jsonb_typeof(result)='object')
);
CREATE INDEX ix_financial_completeness_scope ON financial_completeness_checks(organization_id,branch_id,from_utc,to_utc,checked_at_utc DESC,id);
CREATE FUNCTION protect_financial_completeness_check() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Financial completeness observations are immutable'; END $$;
CREATE TRIGGER financial_completeness_check_guard BEFORE UPDATE OR DELETE ON financial_completeness_checks
FOR EACH ROW EXECUTE FUNCTION protect_financial_completeness_check();
