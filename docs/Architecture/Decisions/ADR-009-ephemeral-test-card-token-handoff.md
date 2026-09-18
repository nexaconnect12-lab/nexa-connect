# ADR-009: Ephemeral test-card token handoff

- Date: 2026-09-18
- Status: Accepted for Development/Testing

## Context

Omise direct and four-scenario hosted recovery gates passed. POS/Order could not forward the initial single-use card token. Saving a token with durable client or server recovery would expose sensitive credentials and invite replay after uncertainty. Recovery must also handle an interruption before any authorization starts without creating another Order or replacing a started charge.

## Decision

Add opt-in local WPF `card_omise_test` checkout, a masked transient token argument and optional Order placement input. The operator uses the existing HTTPS success-card helper; production browser/card entry remains a separate design. Client service APIs must be loopback. Server enablement defaults off and is rejected outside Development/Testing. Payment keeps its fixed official HTTPS test-key restrictions.

Exclude tokens from saved checkout state, aggregates, workflow context, events, audit and telemetry. Application commands ignore the token during JSON serialization and command/request string representations redact it. Forward it only on initial pending-intent authorization using existing exact Order-workload and tenant boundaries. No automatic transport retry is allowed on Order's Payment client.

Without a token, recovery creates/reloads the stable intent and returns awaiting-token state without an authorization POST. An explicit fresh-token request uses the original Order identity, validates immutable scope and line quantities, and resumes the stored checkout without repeating reservation or Kitchen creation. Payment's authoritative state and durable leases determine whether authorization can begin. Started/uncertain operations cannot consume a replacement token; capture/reconciliation/review retain existing ownership. Foreground persistence retains claim fencing and transactions. A deterministic card completion event identity lets the transactional outbox suppress concurrent completion re-enqueue.

An additive `cardTokenRequired` placement response flag enables the masked field only after authoritative awaiting-token state is observed. This action hint stays in POS memory and resets on attempt, lock or process restart. It is not a payment-success proof and cannot bypass Payment's state check. All card completion paths share the deterministic event identity.

Extend the guarded hosted matrix only with explicit `-IncludeCardTokenHandoff`, a fifth distinct token and fresh generated infrastructure. Preserve the passed historical four-scenario gate and distinguish its reconciliation inbox checks from pre-authorization direct completion. No new schema or cross-context persistence ownership is introduced.

## Consequences

Interrupted token delivery requires an operator to confirm the original intent is still pending and supply a fresh token. Recovery cannot silently collect again. The token can remain in managed memory until garbage collection; UI clearing does not guarantee zeroization. Clipboard handling remains explicit. Test-only token paste does not constitute a professional production card-entry integration or PCI onboarding. Production collection, live UI/OIDC and new fifth-scenario credentialed acceptance remain separate gates. See the [operating guide](../../Deployment/Omise-POS-Card-Token-Handoff.md).
