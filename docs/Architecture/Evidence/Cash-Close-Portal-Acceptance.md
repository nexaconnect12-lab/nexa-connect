# Joined cash-close portal acceptance evidence

The local Windows run `2dc486af68544961be43dd7e2c962d2a` completed at `2026-09-27T06:42:46.6467980Z`. Its launcher verification records `passed=true`, `authorizationPassed=true`, `cleanupVerified=true` and `productionVerified=false`. The matching browser summary records five of five passed and `verified=true` at `2026-09-27T06:42:43.564Z`. The runner required six Authorization PostgreSQL cases with zero skips before starting browser acceptance. Twelve focused Authorization unit cases also passed during implementation verification.

Source provenance is `f8a9bed3a1b7c018bbd5d35cd83b5f4c00646df5` with `sourceDirty=true`; the tested working changes were subsequently committed as `2e91c54`. This is not an execution of a clean checkout of that later commit.

The five serial browser cases use real Keycloak, Customer BFF, Platform Directory, Restaurant, Authorization, POS and Reporting, plus PostgreSQL and RabbitMQ. They cover existing-session backfill, approval and late-settlement propagation, branch/store and tenant denial, POS-access outage and recovery, accountant read-only access, and explicit read-deny enforcement in an existing session. Source fixtures use POS repositories; the actual scanner/outbox/consumer supply Reporting data. The six database cases cover hierarchical assignments, role separation, deny precedence, fresh audited decisions, scope specificity and active scoped financial limits.

The successful run includes the POS workload API audience correction and Authorization policy version 2. Earlier failed runs exposed missing workload audience and role grants overriding explicit deny; those runs are not acceptance evidence.

Local files are `.runstate/cash-close-portal/2dc486af68544961be43dd7e2c962d2a/verification.json` and `src/Frontend/test-results/cash-close-live/2dc486af68544961be43dd7e2c962d2a/summary.json`. Raw TRX, service logs, fixture state and secrets are not CI artifacts. This document retains the bounded result because local run directories are ignored.

Remote GitHub execution, branch protection, production topology and deployed credentials remain unverified. This does not prove POS mutation HTTP/WPF behavior, Order ingestion, external alert delivery, production recovery, complete settlement data or multi-store reporting. See the [runner and prerequisites](../../Deployment/Cash-Close-Portal-Acceptance.md) and separate [recovery evidence](Cash-Close-Recovery-Acceptance.md).

A separate focused Authorization Domain dependency check passed 1/1 and is included in CI. This architecture check does not replace runtime policy or database acceptance.
