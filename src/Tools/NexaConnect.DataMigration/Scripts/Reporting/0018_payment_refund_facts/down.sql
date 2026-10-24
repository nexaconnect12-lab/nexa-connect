-- Stop the refund consumer, retain Payment source outbox events, then replay after re-upgrade.
DELETE FROM projection_checkpoints WHERE projector_name='payment-refund-financial' AND source_stream='payment.refunded.v1';
DROP TABLE refund_fact_event_receipts;
DROP TABLE refund_facts;
