-- Stop consumer and reads; retain POS original publications and replay after re-upgrade.
DROP TABLE cash_correction_facts;
DROP TABLE cash_correction_event_receipts;
DROP FUNCTION cash_correction_projection_immutable();
DELETE FROM projection_checkpoints WHERE projector_name='pos-cash-correction';
