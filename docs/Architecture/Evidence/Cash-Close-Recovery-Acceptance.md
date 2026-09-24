# Cash-close recovery, restricted replay and alert acceptance

Local Windows acceptance passed on **2026-09-24** against disposable PostgreSQL 17 and RabbitMQ 4. Both recovery cases passed with **zero skips** and verified Compose cleanup. Prometheus 3.5.0 rule validation and synthetic alert evaluation also passed. This is local evidence, not a remote CI or production sign-off.

## Retained evidence

| Run | Result and retained location |
| --- | --- |
| Final restricted-replay matrix | `45e6779d87be46e0ada986a02cf310fd`, completed `2026-09-24T10:18:00.0362819Z`; `.runstate/cash-close/45e6779d87be46e0ada986a02cf310fd/evidence.json` and `verification.json`: 2 passed, 0 skipped, cleanup and restricted replay verified |
| Earlier restricted-replay matrix | `cc38f5510a2f47f0a77f4ebb98776127`, completed `2026-09-24T10:16:40.7535773Z`; 2 passed and cleanup verified; the final run additionally checks denied default-exchange publication |
| Alert rules | `234b78e2aae94bc2a313488e2cdfdad3`, completed `2026-09-24T10:14:18.6089081Z`; `.runstate/cash-close-alerts/234b78e2aae94bc2a313488e2cdfdad3/verification.json`: passed with `prom/prometheus:v3.5.0`, receiver delivery not verified |

Recovery evidence records source revision `3a72310ca949888a5258f1bf98198f3f1d99fef7` with `sourceDirty=true`: the run includes the working-tree implementation, not just that commit. Bounded JSON evidence contains no credentials or financial payloads. Raw TRX remains local and is excluded from CI uploads.

## What passed

The matrix exercises concurrent snapshot publication, the real outbox dispatcher and Reporting consumer, termination after projection commit before acknowledgement, broker stop/start, delayed approval/settlement snapshots, duplicate/stale and poison delivery, destructive Reporting-15 rebuild through the replay CLI twice, unchanged source financial/checkpoint/outbox fingerprints, append-only audit and guarded downgrade.

The CLI runs under generated restricted credentials. PostgreSQL grants schema usage, outbox SELECT and replay-audit INSERT to a login with no superuser, database-creation, role-creation or inherited rights. Attempts to update outbox flags, delete checkpoints, read cash sessions or audit runs, delete audit attempts and create schema objects return insufficient privilege. Audit investigators use the separate fixture administrator; the CLI does not require audit SELECT. Both replay runs retain the restricted login as `database_actor`.

RabbitMQ grants configure/write on only the exact generated exchange and no queue read. Queue creation, queue reading, unrelated exchange declaration and publication to the default exchange are denied with 403. Replaying originals to the permitted exchange succeeds. This verifies the fixture's grants, not every deployment's users, default privileges or virtual-host policy.

The alert fixture tests all six rules under healthy input, pending duration, firing and resolved states, plus the exact outbox-age threshold remaining quiet. `promtool check rules` and `promtool test rules` pass in a network-disabled container with read-only mounted rule files. This does not prove live exporter scraping, production threshold suitability or receiver delivery.

## Fixture and CI boundaries

The runner discovers Docker in PATH or the per-user Windows Docker Desktop installation. Earlier environment checks missed the latter installation; Docker was available locally. Dynamically selected loopback database, broker and management ports remain explicitly bound across restart. Tests obtain fixture shift/event times from PostgreSQL to avoid host/database clock skew. No production service behavior, contract or schema changed in this verification slice.

The [GitHub workflow](../../../.github/workflows/cash-close-verification.yml) is authored for pull requests and manual dispatch on Ubuntu 24.04 with .NET 10. It runs alert verification then the two-case no-skips recovery gate using generated credentials, read-only repository token permissions and no repository secrets. Only allowlisted verification/evidence JSON is uploaded for 14 days. It has not run remotely, and repository branch protection has not been configured to require its job.

Live OIDC/portal/Restaurant workflows, full migration-runner acceptance, deployed least privilege, production topology, receiver notification delivery, retained-source backup restoration and report completeness remain outside this pass. See [the recovery runbook](../../Deployment/Cash-Close-Recovery.md) for reruns and operational limits.
