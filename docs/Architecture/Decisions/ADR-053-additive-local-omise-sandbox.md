# ADR-053: Additive local Omise sandbox capability

Status: Accepted — 29 October 2026.

## Context

The local POS had mutually exclusive manual/Omise test terminal profiles. The user selected
one POS with optional sandbox card checkout while retaining cash, manual PromptPay and QR.
Changing terminal defaults to enable card could hide or invalidate original manual recovery.

## Decision

Retain a manual configured default and opt in to an additive local-only sandbox capability.
Choose a method before creating a walk-up checkout; persist the method in the existing
protected pending request and pin it through verification/restart. QR collection stays
manual-only. Capabilities are not permissions; existing server gates/authorization and
provider idempotency remain authoritative. Existing Omise-only development profiles remain
supported, but hosted installers remain manual-only.

Isolated local draft storage may enable this capability without changing its path/scope or
manual default. Sandbox identity, POS, Order, Catalog and Payment endpoints remain loopback. Activation requires
stopped, build-enabled owned services, explicit test-key input, protected current-user DPAPI
credential retention and matching backend/client flags. Only Payment gets the secret; only
Order gets the test-workflow flag. Omission on restart cannot silently disable an enabled
profile. No automatic key rotation, provider POST retry, production payment activation,
cashier enable toggle or secret entry in POS.

## Consequences

No new HTTP contract, grant, migration, event or payment primitive is needed. Existing
correlated transport and owning invariants are reused. Frontend selection is nonpersistent
except in the original pending checkout; test tokens are transient. Removing capability
while card recovery exists must fail closed and require restoration/reconciliation, not a
new order. Local duplicate protection does not promise exactly-once provider execution.
Actual mixed-mode sandbox acceptance and existing card shift-link/settlement exclusions
remain explicit. See [setup and boundaries](../../Deployment/POS-Mixed-Omise-Sandbox.md).
