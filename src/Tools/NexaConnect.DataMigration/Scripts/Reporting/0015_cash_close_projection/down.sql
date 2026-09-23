-- Stop the consumer, retain POS source outbox events, then replay after re-upgrade.
DROP TABLE cash_close_event_receipts;
DROP TABLE cash_close_facts;
