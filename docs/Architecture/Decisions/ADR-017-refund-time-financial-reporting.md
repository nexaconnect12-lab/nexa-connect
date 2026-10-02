# ADR-017: Refund-time financial reporting

Status: Accepted

Date: 2026-10-02

## Context

Payment owns completed provider refunds and immutable receipts. Reporting's existing sales/payment tables and Payment audit feed do not provide a dedicated refund financial ledger. Rewriting a sale fact on refund would hide refund-period activity and couple fulfillment history to payment operations.

## Decision

Reporting consumes `payment.refunded.v1` into service-owned, rebuildable `refund_facts` through an opt-in durable consumer. An anti-corruption translation validates identifiers, currency, amount, reason, receipt and cumulative/capture bounds. Facts, normalized event hash receipts and projector checkpoints commit atomically before acknowledgement. Identical event replay is a no-op; identity/content conflicts dead-letter.

Gross sales use the existing completed-order calculation and order occurrence range. Refunds use refund occurrence time in the same UTC range. Net sales equals gross sales minus refunds, including refunds of older sales and negative refund-only periods. Dashboard net paid remains the existing payment-fact measure. Mixed currencies reject aggregation. Tenant/branch filters cover every source.

The portal displays these measures explicitly. No fiscal tax allocation, financial source ownership transfer, sale-time refund restatement, Payment database access or fulfillment Order mutation is introduced.

## Alternatives

Updating original sale-period facts would support restated sale cohorts but conceal refund-period flows and require a separate accounting policy. Inferring refund amounts from audit events would overload a deliberately safe activity contract. Reading Payment's database would violate service ownership. The dedicated versioned completion contract keeps these concerns separate.

## Consequences

Reporting 18 requires application 0.21.0. Destructive downgrade removes refund facts, receipts and checkpoint, requiring retained Payment event replay after re-upgrade. Checkpoint position counts unique applied events; it is not a source offset or completeness watermark. Provider payloads and credentials stay outside Reporting.

Sales/payment source consumers, historical backfill tooling, certified reconciliation and joined production acceptance remain unimplemented. Until those prerequisites exist, net sales describes projected inputs and does not certify complete accounting. See [contract and rollout](../../API/Refund-Financial-Reporting.md).
