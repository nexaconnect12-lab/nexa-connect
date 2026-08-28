# Phase 12 Payment operational-rehearsal evidence

## Local execution

On 2026-08-28, `scripts/test-payment-capture-recovery-operations.ps1` passed against the local disposable PostgreSQL 17 and isolated Alertmanager Compose profile.

Alert delivery evidence:

- the script started a separate Alertmanager on loopback port `19093` and an in-memory webhook receiver on `19094`;
- a uniquely labelled synthetic critical Payment alert was accepted through Alertmanager's v2 API;
- the receiver observed a normalized `firing` webhook with that exact rehearsal ID;
- the same label set was ended and the receiver observed a normalized `resolved` webhook with that ID;
- the successful run removed both rehearsal containers and its transient artifacts.

Rollback-forward evidence:

- the Payment generated-database test executed a confirmed destructive `0→6→5→6`, including migration history/checksum/schema verification;
- the Order generated-database test executed `0→2→1→2`, including refusal to downgrade unresolved `payment_pending` financial state;
- TRX counters proved exactly two tests ran, two passed, and none were skipped;
- both generated databases were removed after execution.

## Evidence boundary

The webhook receiver is a local synthetic fixture with no authentication, paging, acknowledgement, or escalation integration. `TargetEnvironment=Staging` labels this evidence; the tests use the safety environment `Testing`, generated empty databases, and controlled seeded state. They do not drain live traffic or reconcile a concrete provider/accounting ledger. Production receiver delivery and live-environment rollback/forward recovery remain release gates.
