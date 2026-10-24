# ADR-041 — Dedicated-server pilot artifacts and explicit deployment gates

Status: Accepted for artifact preparation, 23 October 2026. Target deployment is pending.

## Context

The user owns a dedicated server/static IP and selected own-server hosting before cloud.
Its OS, capacity, DNS, backup target and administrative access remain unconfirmed (E01–E04).
The local draft runs Development fixtures and omits parts of the hosted graph. It must
not become a public production stack or lose retained financial/terminal history.

## Decision

Prepare a portable framework-dependent Linux-container application bundle: thirteen
independently owned services, Customer BFF with its built Customer Portal, PlatformAdmin
BFF with its built Product Owner Portal, plus the existing migration runner/catalog.
The dependency-free Node tooling in `docker/pilot` never starts containers or provisions
external resources. Linux container capability is an explicit prerequisite, not an
assertion about the user's unknown server OS. Unsupported target environments require
a reviewed adaptation rather than silently falling back to Development.

The generated Compose graph supervises application containers with bounded resources,
nonroot identities, read-only roots, separate writable certificate-protected key rings,
read-only service-specific configuration/secrets, bounded local logs and restart policy.
Infrastructure is independently operated on an explicitly named external private network.
No application database, broker, Redis, storage, identity or monitoring port is published
by this graph. This is a single-host pilot, not an HA architecture.

An edge proxy provides separate portal and cashier API hostnames. It re-encrypts and
verifies HTTPS to Kestrel using matching internal DNS aliases and certificate SANs;
apps continue to see HTTPS directly. It preserves the configured external Host,
removes forwarded headers, hides management/internal API paths and disables URL access
logs. Authentication/authorization and correlation validation remain in existing owners.
The public POS remains a public PKCE client without any client secret. Keycloak's
existing production deployment and separately restricted admin origin remain independent.

Issuer, clients, per-service database ownership, secure Redis ticket stores, recovery
workers, durable consumers, scan-gated Media and disabled card/provider paths are checked
before rendering. Configuration checks are structural; actual certificate trust,
least-privilege grants, queue readiness and isolation require target measurements.
The plan records those prerequisites; marking them true is an operator assertion, not
an automated acceptance result. Generated receipts explicitly say deployment was not run.

Bundles bind runtime, revision, dirty status, non-Markdown nonignored source input hash,
catalog targets and an exact published file/hash inventory. Hashes detect accidental
change; they are not signatures or clean-source CI. Phase 8 supplies the frozen release.
No automatic financial schema downgrade, live restore or competing-writer cutover exists.

## Consequences

The same application bundle can move to a later compatible cloud runtime with changed
configuration/endpoints and separately rehearsed data/identity migration. Infrastructure
provisioning remains target-specific until server facts and operational choices are known.
Backups must include service/identity databases, object versions, required broker/session
state, key rings, certificate keys, configuration and infrastructure definitions. A SQL
dump alone never establishes restored-application readiness. Off-server encrypted backup,
isolated full-graph restore, reboot, real login and remote device tests remain deployment
gates documented in the [Phase 6 runbook](../../Deployment/Next-Iteration-Phase-6.md).

Alternatives rejected: publicizing the draft launcher (Development security/data ownership
assumptions); plaintext backend HTTP (violates current production HTTPS contract); choosing
a cloud provider now (contrary to agreed own-server-first sequence); choosing a server OS
or reusing local DPAPI recovery without confirmed target facts.
