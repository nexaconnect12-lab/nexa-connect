# NexaConnect Infrastructure

`Persistence/PostgresLateWorkReview` supplies owning-schema scoped seek pagination, repeatable-read detail, serialized append transactions and actor-bound exact replay. Owning adapters provide safe projection and source Domain review policy; Application supplies live authorization. The helper does not change financial state, custody disposition or settlement receipts. See [review contract](../../../docs/API/Late-Work-Reviews.md) and [storage/grants](../../../docs/Deployment/Late-Work-Reviews.md).

PostgresDayBarrier and PostgresLateFinancialWork provide parameterized owning-schema retention/locking mechanics; owning Domain supplies transition/window admission. Strict ServiceWorkloadPrincipal.IsClientCredentials checks exact authenticated client and service-account username for the POS settlement protocol. No service-specific financial policy is placed here. See [settlement contract](../../../docs/API/Day-Close-Settlements.md) and [operations/telemetry](../../../docs/Deployment/Day-Close-Settlements.md).

`PostgresWindowFence` provides shared low-level parameterized lease, locking and bounded journal persistence mechanics. Source-owned Domain callbacks supply financial admission; SQL backstops stay in owning migrations. It does not own business selection, customer authorization or settlement state.

Exact historical-day attribution is implemented in Order 16 / Payment 15 / POS 13 (compatibility 0.29.0). Source-owned before/after selection distinguishes creation-time sales, Paid-time tenders, refund completion and drawer closure/review. Unrelated historical changes are excluded; older unresolved work, missing history, legacy descriptors and ownership uncertainty block readiness. Managers see bounded record identities, reasons, statuses, versions and effective times beside the preserved baseline/current comparison. Snapshot approval is implemented separately; durable online settlement is implemented separately by ADR-029; verified POS late-cash correction posting is implemented separately by ADR-031; other correction types and offline finalization remain planned. See [attribution and comparison policy](../../../docs/Architecture/Decisions/ADR-026-exact-historical-day-attribution.md).

`PostgresEvidenceSeals<T>` supplies parameterized scoped retention, actor/payload replay fingerprints, owning branch/operation locks preceding a repeatable-read snapshot, immutable source manifest lookup and bounded journal suffix reads. Owning Domain supplies eligibility and exact before/after attribution through service-owned callbacks returning stable `SourceSealImpact` transport metadata; the primitive grants no financial authorization/readiness policy. Source migration triggers use the same gate for revision advancement. Reads verify the full revision chain, evaluate at most 10,000 rows/16 MiB, count unevaluated entries as unknown and expose at most 256 relevant/unknown entries with safe identity/status/version/effective-time fields. Responses advertise attribution protocol two. A source-owned callback reads current totals in the same snapshot for advisory comparison. See [ADR-026](../../../docs/Architecture/Decisions/ADR-026-exact-historical-day-attribution.md).

`Persistence/PostgresFinancialRevision` reads the owning epoch and Restaurant/branch revision through parameterized SQL in the caller's snapshot transaction. An untouched branch returns revision zero; a missing epoch fails unavailable. Its equality primitive requires protocol two, a nonempty matching epoch, nonnegative matching revision and matching evidence fingerprint. Physical tables, mutation triggers, scoped authorization and readiness policy remain service-owned. See [ADR-023](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md) and [migration prerequisites and verification](../../../docs/Deployment/Day-Close-Cutoffs.md). No revision or financial payload is logged.

Low-level bounded evidence hashing may return the complete selected rows to the owning adapter for immutable retention. `PostgresSnapshotRetention<T>` supplies scoped operation/window serialization, repeatable-read transaction, bounded JSON persistence and exact replay for independently owned `source_day_cutoffs` tables. It contains no source selection, authorization or readiness policy. `BoundedJson` bounds streamed dependency bodies. See [retained cutoff evidence](../../../docs/API/Day-Close-Cutoffs.md).

`Persistence/BoundedEvidenceHash` supplies length-delimited SHA-256 over a caller-owned SQL string-row stream and owner/window prefix. It returns null beyond 10,000 rows or 16 MiB of UTF-8 evidence; it never emits a partial version or logs evidence. Owning Infrastructure selects rows and shares its local repeatable-read transaction with totals. No financial/readiness policy lives in this primitive. See [fingerprint contract](../../../docs/API/Day-Close-Preparation.md).

This project contains narrowly scoped infrastructure registration shared by API hosts.

`AddNexaConnectApiAuthentication` configures strict Keycloak JWT bearer validation and a fallback authorization policy that denies anonymous access unless an endpoint is explicitly marked `AllowAnonymous`.

Required configuration:

```json
{
  "Authentication": {
    "Authority": "https://identity.example.com/realms/nexa-prod",
    "Audience": "nexaconnect-api",
    "RequireHttpsMetadata": true,
    "ClockSkewSeconds": 30
  }
}
```

The registration rejects missing configuration, non-HTTPS production metadata, invalid audiences, excessive clock skew, and the checked-in `.invalid` deployment placeholder.

Production hosts call `AddNexaConnectDataProtection(configuration, environment, applicationName)`. Outside Development it requires `DataProtection:KeyDirectory` to already exist and be writable, and requires a password-protected PFX at `DataProtection:CertificatePath`; the certificate encrypts the durable key ring. `EnsureProductionHttps` requires an HTTPS listener plus a password-protected PFX at `Tls:CertificatePath`. Keep both passwords in a secret manager and grant each service access only to its own key directory and certificate.

`PostgresInboxStore` inserts the message identity and acquires its processing lease through separate parameterized commands in one PostgreSQL transaction. Completed messages remain suppressed, active leases return busy, and released/expired claims are retryable. `InboxPersistenceTests` provides the live PostgreSQL verification boundary.

`RabbitMqOutboxTransport` lazily establishes its publisher connection, enables RabbitMQ automatic/topology recovery, and replaces an established connection when it is no longer open. A publish failure invalidates the owned connection; the dispatcher leaves the outbox row unpublished and retries it on a later poll. Publisher confirms and persistent delivery remain required, and applications never delete durable outbox state merely because the broker is unavailable.

`BranchScopeReader` is an endpoint-specific authenticated workload policy for Restaurant's branch authorization-scope GET. It preserves the six existing generic service clients and additionally permits `nexaconnect-reporting-service`. The generic `ServiceWorkload` allowlist does not include Reporting, so scope lookup does not grant pricing, mutation or other service access. Owning Application code still validates the returned resource hierarchy and authorizes the customer independently.
