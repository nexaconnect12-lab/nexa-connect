LOCK TABLE settlement_facts IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM settlement_facts WHERE contract_version=3) THEN
 RAISE EXCEPTION 'V3 settlements are retained; Reporting24 requires forward recovery'; END IF; END $$;
ALTER TABLE settlement_facts DROP CONSTRAINT settlement_facts_contract_version_check;
ALTER TABLE settlement_facts ADD CONSTRAINT settlement_facts_contract_version_check CHECK(contract_version IN(1,2));
