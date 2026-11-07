ALTER TABLE settlement_facts DROP CONSTRAINT settlement_facts_contract_version_check;
ALTER TABLE settlement_facts ADD CONSTRAINT settlement_facts_contract_version_check CHECK(contract_version IN(1,2,3));
