# ADR-015: Durable pre-payment order cancellation

- Status: Accepted
- Date: 2026-09-30

## Context

An operator must be able to abandon an Order before payment without leaving Kitchen work or Inventory reservations active. Kitchen and Inventory own separate databases, so no distributed transaction can atomically cancel all three contexts. Response loss, process interruption, concurrent payment work, and completed preparation make a simple sequence of HTTP calls unsafe.

## Decision

Order owns one durable cancellation operation per Order. It accepts only Submitted, InventoryReserved, and KitchenAccepted Orders after a live branch `order.cancel` decision. The initial transaction stores the stable operation, bounded reason, actor, authorization decision, original state and required compensations, then moves Order to `cancellation_pending` and appends request/audit outbox messages.

A lease-fenced recovery step cancels Kitchen before releasing Inventory. Both calls use Order identity for idempotency. Dependency failure remains pending for retry. A Kitchen terminal-state conflict moves the request and Order to explicit review and does not release Inventory. Successful compensation atomically records `Cancelled` and its final event/audit. Terminal cancellation history and its identity fields are database-immutable. Payment reconciliation and stale aggregate writers treat all cancellation states as terminal fences.

The operator API returns the durable state and accepts only exact operation-and-reason replay. POS retains and reuses its protected operation identity through uncertain responses. The first release has no supervisor override for a blocked Kitchen conflict.

## Consequences

The system is eventually consistent across service boundaries and may visibly remain pending. Operations can distinguish retryable dependency failure from a business conflict requiring review. Inventory cannot be released after Kitchen has refused cancellation. This workflow does not reverse captured payment; refunds remain a separate Payment/Order capability with separate authorization and receipts.
