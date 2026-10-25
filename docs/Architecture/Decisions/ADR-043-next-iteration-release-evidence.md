# ADR-043 — One source binding and explicit next-iteration release gates

Status: Accepted for release preparation, 23 October 2026. Actual freeze is gated.

## Context

Phases 1–7 add client UI/session/display behavior, approved media delivery, hosting
artifacts and an installer. Earlier draft tooling binds a different local stack and
sixteen-case UAT; it must not certify these additions. Hosting and installer producers
previously used different source-input inventories. A matching HEAD alone could associate
dirty or stale binaries with fresh tests. Server, trusted signing and device evidence
remain independently incomplete.

## Decision

Use one additive `releaseSource` contract in both artifact producers: exact HEAD, dirty
boolean, SHA-256 of Git-clean blob/path inventory and file count. Include all nonignored
non-Markdown source/configuration/packaging/workflow inputs. Capture before/after artifact
publication and verification; changed input fails. Existing producer metadata remains;
older artifacts are readable under their own contracts but cannot enter this freeze
without a new shared-source build. Documentation-only changes may be recorded afterward,
but a final freeze requires clean source and the exact committed CI candidate.

The release tool binds actual bundle manifest/file hashes, current owning schema targets,
actual installer/payload hashes and production configuration/certificate hashes. No secret
values or DPAPI state are embedded/copied. Existing owner/SYSTEM permissions protect local
run logs and records. Source/unit/architecture/HTTP/frontend/browser/audit/hosting/installer
gates are distinct from actual disposable financial/recovery gates. Strict report parsers
reject skips, retries, expected browser failures, missing inventories and failed cleanup.

Read CI from the configured GitHub API: exact source dispatch, expected workflow path and
successful Windows/Linux/joined jobs. Requery at freeze/UAT preparation to reject changed
run attempts. Never infer CI success from a local field, an older run, PR merge checks or
authored workflow YAML. No automatic Git commit, push or workflow dispatch is introduced.

Host login/denial, remote POS/Kitchen, reboot/outage, off-server full-graph restoration,
alerts, measured capacity and installer roundtrip/interruption remain typed operator
attestations, bound to the exact candidate/artifact/configuration. They are not automation
measurements or user acceptance. Trusted installer signatures are checked on Windows.
There are no booleans or flags that waive missing proof. Private unsigned candidates stay
reviewable but cannot be frozen as a signed release.

Freeze is a write-once manifest after every gate succeeds; status remains observational.
The manifest binds evidence and always leaves user acceptance pending. UAT preparation
revalidates the frozen bindings and creates a new fifteen-case second-test companion,
all pending. It never edits the first-draft UAT, accepts the product or substitutes
historical clocks for actual Singapore midnight.

## Consequences and limits

Source or configuration fixes require replacement binding and matching artifact/affected
verification, not spliced historical receipts. Exact-source signed release readiness can
remain blocked while local implementation/tests succeed. Files/hashes are integrity
evidence, not signatures; operator records retain their explicit trust/attestation class.
Private run administrators are trusted to preserve records; remote CI and Authenticode
are checked independently. No financial workflow, service permission, database migration,
provider operation, offline execution or cloud/Store deployment is added.

The existing joined fixtures test owning financial invariants, tenant authorization,
idempotency, concurrent versions, transactional audit/outbox and recovery. Their SQL-only
local restore is not the hosted full-graph disaster recovery gate. Server/environment
prerequisites and actual Windows/device proof cannot be replaced by a green source run.
See [Phase 8 operations](../../Deployment/Next-Iteration-Phase-8.md) and
[release tool contract](../../../scripts/release/README.md).

## QR6 local diagnostic candidate extension — 25 October 2026

QR6 adds a separate local integrity candidate after strict source/security/financial/QR
gates and the owning PostgreSQL/RabbitMQ journey. It binds current source/schema and
compiled SPA/Debug POS inventories, with write-once candidate and pending phone templates.
Private logs and arbitrary evidence payloads are not projected into that candidate.
Dirty source is recorded; releaseReady remains false regardless of local success.

This diagnostic binding cannot replace the signed installer/hosting/configuration/clean-CI
and actual target observations required by this ADR. Its phone template is an explicit
operator-attestation starting point, never automated device proof or a freeze input.
Changed source/artifacts require replacement verification and binding. See
[QR6 operations](../../Deployment/QR6-Integration.md).
