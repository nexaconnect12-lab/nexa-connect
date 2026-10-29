# ADR-050 — isolate local QR pilot POS recovery within one Windows account

Status: accepted, 25 October 2026.

29 October amendment: [ADR-053](ADR-053-additive-local-omise-sandbox.md) permits an explicit
local sandbox capability beside the retained manual default. Scope/path isolation and
hosted manual-only policy remain unchanged; the original manual-only capability restriction
below describes the initial rollout.

## Context

A newly provisioned QR pilot has a different terminal scope from an earlier draft. The
POS correctly refuses to open the earlier terminal's current-user recovery database.
The user needs to test the new pilot in the same Windows account. Deleting, moving,
copying or rebinding the original recovery would lose its financial and operator custody.
A separate Windows account remains possible, but is not required for this local pilot.

## Decision

`Pos:UseIsolatedLocalPilotStorage` is an explicit Boolean, absent/false by default.
The QR first-draft profile generates it as true. It is valid only with manual tender,
disabled test-card checkout, loopback identity/service URLs, and an exact generated
`/realms/nexa-review-it-<32-lowercase-hex-run-id>` issuer path. Hosted installation
rejects it. There is no configurable storage path or arbitrary profile label.

The default directory remains `%LOCALAPPDATA%\NexaConnect\POS`. An enabled pilot uses
`%LOCALAPPDATA%\NexaConnect\POS-Pilots\<scope-hash>`, where the hash is lowercase SHA256
of the UTF-8 text `organizationN|branchN|storeN|terminalN`; each UUID uses its lowercase
32-character `N` representation. Endpoint relocation or tender changes do not select a
new directory. File-system links/junctions in the selected path are rejected.

Read-only startup/installation validation, SQLite recovery, outbox and protected token/
lock-marker storage all select the same directory. Existing database scope, schema,
integrity, pending checkout and original-operator checks still apply inside it. DPAPI
continues binding payloads to the Windows user/device. The old directory is not scanned
for adoption, migrated, renamed, overwritten or deleted. Its unresolved work remains the
responsibility of its original terminal/cashier.

Storage selection is part of package/update custody. An existing installed release cannot
silently flip between default and pilot data. Select a separate installation for a new
pilot; use the existing single-instance/callback ownership controls. This is local test
isolation, not concurrent multi-terminal operation or a production storage migration.

## Consequences and verification

The current Windows account can retain the earlier terminal while running a separately
provisioned QR test terminal. Existing installations/default configuration remain
compatible. No API, PostgreSQL or SQLite schema, permission, business workflow or financial
identity changes. Clearing a test profile is not a way to reconcile original work.

Refreshing a previously configured QR pilot after a source change retains its exact
service-owned branch/realm/terminal scope and uses a fresh candidate plus new immutable
activation plan. The old activation is not rewritten. Reusing a still-valid certificate
requires the same origin/owner and verified identity; see the reviewed procedure below.

Tests cover fixed scope-derived paths, original-directory preservation, scope/mode rejection,
restart, DPAPI checkout/token storage, remote/invalid configuration rejection and immutable
package selection. Actual WPF sign-in, cashier recovery and PDF acceptance remain distinct
from these source checks. See [POS storage](../../Database/POS-Local-SQLite.md),
[Windows setup](../../Deployment/First-Draft-Windows-POS.md) and
[QR pilot refresh](../../Deployment/QR7-Pilot.md).
