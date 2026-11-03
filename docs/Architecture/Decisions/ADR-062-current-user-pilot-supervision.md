# ADR-062: current-user supervision for the Windows local pilot

Status: Accepted — UX23 local test tooling.

## Context

The native POS can remain open while tool-launched backend processes have stopped. The
historical cause is unconfirmed. A disposable kill-on-close Windows job test establishes
that a scheduler-launched current-user worker survives launcher-job termination. Existing
pilot credentials and native recovery belong to Windows current-user DPAPI; moving them
to SYSTEM or another service identity would change ownership and recovery prerequisites.

## Decision

Use a non-elevated current-user interactive Task Scheduler action as the local graph owner,
without a stored Windows password. Demand startup is the default; explicit at-logon startup
is optional. Bind exact normal installation/reference/source/artifact and task identities;
exclude recovery labs. Add guarded missing-process Resume to the existing lifecycle, preserve
healthy PIDs, fence maintenance and bound durable recovery attempts. No automatic business
request replay, rebuild, migration, reference provisioning or identity/grant changes occur.

The supervisor emits fixed operational events through a small telemetry tool using the
shared JSON/OpenTelemetry logging foundation. It does not become a business service or
share domain models. Existing financial aggregates, transactions, outbox/inbox, tenant
authorization, audit and idempotency remain authoritative.

## Consequences

This survives launcher closure and supports recovery after restart and owner sign-in. It
does not run before logon, replace Docker Desktop, establish production service hosting or
prove reboot acceptance. Source/package/QR proof drift requires explicit maintenance; a
live unresponsive or foreign process is not automatically killed. Task/evidence files are
retained for review; data/recovery are not moved or reset. Future unattended production
hosting needs a separate service identity, secret and operational design.

See [UX23 operations and verification](../../Deployment/POS-UX23-Background-Hosting.md).
