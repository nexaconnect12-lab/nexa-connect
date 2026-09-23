# NexaConnect Unit Tests

Cash-close tests protect snapshot translation, ownership/source-version invariants, late-settlement review invalidation, exact-store authorization before reads, cursor bounds, authoritative publication scope and repeated Restaurant-client requests. Migration discovery checks include POS 6 and Reporting 15. Run the `CashClose|MigrationRunner` fully qualified name filter; these tests do not exercise a live broker. See [cash-close verification](../../../docs/API/Cash-Close-Reporting.md).

The unit suite covers application and domain behavior without production infrastructure. POS coverage includes shift authorization and concurrency orchestration, cash-session validation and currency normalization, and terminal-enrollment scope/authorization decisions through controlled persistence and provider doubles. Platform identity coverage verifies role policies plus support-elevation duration, separation-of-duties, expiry, and Application persistence orchestration.

Cashier presentation tests cover combined menu filtering, currency labelling, and edit guards. SettlementAttemptTests prove recovery persistence precedes network I/O, persistence failure prevents sending, lost responses retain exact replay fields, and cleanup failure cannot report success. Opt-in PosPendingSettlementRecoveryTests verify Windows DPAPI restart/corruption behavior. See docs/Deployment/POS-Cashier-Acceptance.md for live acceptance limits.

PosCheckoutIntegrationTests exercise real client request serialization over controlled HttpMessageHandlers: Catalog origin/tenant headers, stable placement replay after lost response, scope fencing and invalid configuration. The opt-in protected-state suite also covers pending-checkout restart and corruption. These are client component tests, not live OIDC/server acceptance.

Kitchen queue tests cover active station-scoped keyset pagination, revoked/read-only access, foreign branch/product/workload denial, stale actions, cancellation races and all-or-none in-memory cancellation when another station is completed. Run `dotnet test --filter FullyQualifiedName~Kitchen` from this project.
