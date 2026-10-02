# ADR-016: Durable provider refunds and immutable refund receipts

## Status

Accepted

## Decision

Payment owns a refund aggregate separate from the Payment intent state machine. Each operation requires a live manager permission decision with amount and currency bounded by an active restaurant/action/currency approval limit, then reserves part of one captured amount under a row lock. Completed, uncertain, and review-required operations count toward the reservation; definitive failures do not. The operation identity, authorization decision, provider reference, lease, bounded recovery attempts, audit, outbox event, and receipt snapshot are durable. Customer responses omit the actor, decision, provider, lease, attempt, and concurrency evidence.

Provider I/O occurs outside database transactions. A command is issued once with stable identity. Any transport, timeout, or ambiguous response becomes `refund_unknown`; recovery uses status lookup and never repeats that command. Exhausted ambiguity requires operations review and remains financially reserved. Definitive completion atomically writes the receipt and event. The database prevents mutation or deletion of completed refund evidence.

Order remains the source of fulfillment and paid-order history; it is not changed back from Paid. Payment events provide the financial correction. Reporting 17 accepts the audit vocabulary, while a complete financial fact consumer remains a follow-up. The WPF POS protects the operation locally before sending and queries by operation identity after response loss.

## Consequences

Partial refunds can proceed concurrently without exceeding the capture, and replay cannot create an extra refund. Refund creation fails closed when no eligible financial limit exists. Provider uncertainty can reduce temporarily available refundable balance until review. Refund receipts are operational customer documents, not tax credit notes or fiscal documents. Payment 9 refuses upgrade over pre-existing baseline refund rows and cannot be downgraded after refund history exists. Manual-tender refund policy, return-line allocation, Inventory restocking, tax credit documents, production Omise onboarding, and automated Reporting financial facts require later slices.
