# NexaConnect Unit Tests

`DayCutoffTests` additionally reject legacy/unproven delivery, preserve revision equality boundaries, require current revision-bound sources and reject changed retained revision identities. Migration catalog expectations include Order 13 / Payment 12 / POS 10. See [revision protocol](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md).

`DayClosePreparationTests` cover aggregate readiness/blocker policy, same-total version drift, immutable exported evidence, expired restart/resume fencing, live permissions/hierarchy, source outage invalidation and cancellation. The source-interface fake does not replace PostgreSQL/HTTP/browser acceptance; see [verification](../../../docs/Deployment/Day-Close-Preparation.md).

OrderPricingTests verifies inclusive/exclusive calculations, decimal rounding, changed-price confirmation, snapshot replay, scope/content conflicts, tamper rejection and provider totals. ProductConfigurationTests bounds tax precision; PosCheckoutIntegrationTests covers quote identity and explicit reconfirmation responses.

`AuthorizationPolicyTests` supplies 12 cases covering explicit override precedence, unknown-effect denial, nonnegative bounded amounts, missing limits and audit-write failure. These passed for policy version 2; persistence scope selection is covered separately by live PostgreSQL tests.

Cash-close tests protect snapshot translation, ownership/source-version invariants, late-settlement review invalidation, exact-store authorization before reads, cursor bounds, authoritative publication scope and repeated Restaurant-client requests. Migration discovery checks include POS 6 and Reporting 15. Run the `CashClose|MigrationRunner` fully qualified name filter; these tests do not exercise a live broker. See [cash-close verification](../../../docs/API/Cash-Close-Reporting.md).

The unit suite covers application and domain behavior without production infrastructure. POS coverage includes shift authorization and concurrency orchestration, cash-session validation and currency normalization, and terminal-enrollment scope/authorization decisions through controlled persistence and provider doubles. Platform identity coverage verifies role policies plus support-elevation duration, separation-of-duties, expiry, and Application persistence orchestration.

Cashier presentation tests cover combined menu filtering, currency labelling, and edit guards. SettlementAttemptTests prove recovery persistence precedes network I/O, persistence failure prevents sending, lost responses retain exact replay fields, and cleanup failure cannot report success. Opt-in PosPendingSettlementRecoveryTests verify Windows DPAPI restart/corruption behavior. See docs/Deployment/POS-Cashier-Acceptance.md for live acceptance limits.

PosCheckoutIntegrationTests exercise real client request serialization over controlled HttpMessageHandlers: Catalog origin/tenant headers, stable placement replay after lost response, scope fencing and invalid configuration. The opt-in protected-state suite also covers pending-checkout restart and corruption. These are client component tests, not live OIDC/server acceptance.

Kitchen queue tests cover active station-scoped keyset pagination, revoked/read-only access, foreign branch/product/workload denial, stale actions, cancellation races and all-or-none in-memory cancellation when another station is completed. Run `dotnet test --filter FullyQualifiedName~Kitchen` from this project.

`CashCloseReplayTests` covers read-only preview, manifest drift, bounded selection, attribution, durable intent before publication, audit failure and interrupted delivery with original-event retry. Migration discovery includes POS 7. These tests do not establish PostgreSQL grants, broker acknowledgement or live replay acceptance.
