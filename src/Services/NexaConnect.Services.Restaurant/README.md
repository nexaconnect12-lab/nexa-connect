# Restaurant Service

UX4 adds a Media-workload-only internal gallery selection use case. It resolves active
owning hierarchy through existing persistence, then rechecks original-subject Directory
membership and fixed Authorization menu-read via dedicated bounded, redirect-disabled
correlation-propagating clients using existing service addresses. No schema or configuration
write is added. Service `nexaconnect-restaurant` emits safe `Gallery reader`/`Gallery authority`
boundary events, searchable by correlation ID. See [API](../../../docs/API/Screensaver-Gallery.md)
and [deployment/tests](../../../docs/Deployment/POS-UX4-Locked-Gallery.md).

QR4 adds exact-Order-workload-only internal QR visitor/entry context read, including
current dine-in policy. Owning parameterized lookup checks organization/branch/visitor/
entry, active hierarchy/version, session ended/expiry; no secret recovery or public
visitor-ID authority. No Restaurant migration. Correlated Order workload calls and safe
QR denial/unavailable events use nexaconnect-restaurant. See [QR4](../../../docs/Deployment/QR4-Acceptance.md).


QR3 adds operational discovery capability qr-requests using existing order.read; no
Restaurant schema/admission change. Order revalidates the current visitor on guest
submission/status, while staff operations require live branch decisions independently.
See [QR3](../../../docs/Deployment/QR3-Requests.md).


QR2 reuses the existing QR1 context and exact Order-workload branch pricing reads, without
new Restaurant routes/schema/grants. Order validates visitor context before and after
scoped menu/quote dependencies; see [QR2](../../../docs/Deployment/QR2-Menu.md).

QR1 adds scoped table-entry creation/rotation/revocation and independent visitor admission
under Restaurant migration 4. Set `QrEntry__Enabled=true` deliberately after migration/grants;
the default is disabled. Logical ordering labels do not mutate floor/table-account records.
Staff requires live exact-branch `restaurant.configuration.manage`; guest capabilities
grant context only. Domain versions/budgets and transactional hashed persistence/audit/outbox
preserve ownership and revocation. See [contract](../../../docs/API/QR-Entry.md),
[persistence](../../../docs/Database/QR-Entry.md), [identity](../../../docs/Identity/QR-Guest-Entry.md)
and [operations/verification](../../../docs/Deployment/QR1-Entry.md). Query
`{service_name="nexaconnect-restaurant"} |= "QR"` by validated `CorrelationId`; secrets,
cookies, bodies, table labels and addresses are omitted. No guest Order/Payment authority.

Phase 5 adds branch screensaver gallery configuration with existing branch concurrency,
configuration-manage authority, audit and outbox. `ScreensaverGallery` is an additive JSON
property; pricing remains untouched. Read uses live branch `catalog.menu.read`; save also
validates selected ready Media variants with caller Media read authority. Set `Services__Media`.
The readiness endpoint does not call back into Restaurant. Shared correlation and bounded
Gallery configuration/asset events cover dependency/authorization boundaries. See
[contract](../../../docs/API/Screensaver-Gallery.md),
[persistence](../../../docs/Database/Branch-Gallery.md) and
[deployment](../../../docs/Deployment/Next-Iteration-Phase-5.md).

Gallery reads and saves first resolve the active owning restaurant for the requested
organization/branch through Restaurant persistence. Authorization receives all three scope
IDs, preserving restaurant-scoped manager access and live denials. Missing, inactive and
foreign hierarchies fail closed; no role or permission grant changes. The parameterized
lookup adds no schema change. Diagnose `Gallery configuration authorization denied` under
`nexaconnect-restaurant` with the validated correlation ID.

[Operational branch discovery](../../../docs/API/Operational-Branches.md) admits customer realm users/viewers and filters active organization branches by live exact-scope workflow permission and directory access. The admin branch-management contract is unchanged. nexaconnect-restaurant logs bounded discovery availability/empty-result events with existing correlation propagation.

Cash-close discovery accepts `capability=cash-close` and evaluates existing `pos.cash-review.read` for each exact branch scope. It does not require `pos.day-close.read` or grant store access; Reporting/POS independently authorize the selected store. Existing customer entry roles, no-store/error behavior and telemetry apply; no schema or new grant is added.

`GET /api/restaurant/v1/branches/{branchId}/business-calendar` returns active owner IDs and current configured timezone/currency under the same `BranchScopeReader` workload policy. Reporting uses its existing dedicated workload client; this gives no financial command or generic mutation access. The authorization-scope HTTP response remains its original three IDs. Calendar reads use no-store responses and existing `nexaconnect-restaurant` request/correlation telemetry. No schema or identity provisioning change is added. See [end-of-day contract](../../../docs/API/End-Of-Day-Draft.md).

Branch configuration now includes taxPercent/taxInclusive and currency in reads; omitted tax fields preserve existing settings. The exact Order-workload pricing route returns only active THB branch settings within the requested organization. Existing configuration versions and audit/outbox apply; no new Restaurant schema is required. Service name: nexaconnect-restaurant; query logs for Branch pricing. See [pricing contract and rollout](../../../docs/API/Order-Pricing.md).

Owns restaurant and branch hierarchy used by tenant authorization. Operational telemetry uses service name `nexaconnect-restaurant`; request logs exclude query strings, bodies, authorization headers, cookies, and arbitrary headers.

Platform owners and administrators browse and provision hierarchy through `GET|POST /api/restaurant/v1/restaurants` and `GET|POST /api/restaurant/v1/restaurants/{restaurantId}/branches`. Restaurant listing requires an `organizationId` query value; branch listing is scoped by the restaurant route. Both mutations are idempotent by owner/code, and all parameterized PostgreSQL access remains inside Restaurant Infrastructure. Equivalent Platform Admin BFF proxies preserve the bearer/session boundary. Provisioning logs record scoped UUIDs but no tokens or request bodies.

Customer owners/admins manage tenant branches through `/api/restaurant/v1/customer/organizations/{organizationId}/branches`. Restaurant requires active Platform Directory access plus `restaurant.branch.read` or `restaurant.branch.manage` from Authorization and enforces ownership in organization-leading SQL. Creates and versioned updates transactionally append immutable branch audit records. Apply Restaurant migration 2 and configure direct HTTPS `Services:PlatformDirectory` and `Services:Authorization`; dependency failure is fail-closed.

Typed branch product configuration is exposed at `/api/restaurant/v1/customer/organizations/{organizationId}/configuration/branches/{branchId}`. It controls dine-in, takeaway, table requirements, and service-charge percentage. Versioned writes require `restaurant.configuration.manage`, Restaurant migration 3, and append `branch.configuration.updated` in the same transaction.

JSON stdout is always enabled. For centralized local debugging set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317`, then use the correlation query in the [observability guide](../../../docs/Deployment/Observability.md).

`GET /api/restaurant/v1/branches/{branchId}/authorization-scope` uses the endpoint-specific `BranchScopeReader` workload policy. It preserves the existing scope readers and additionally accepts the dedicated `nexaconnect-reporting-service` client. Reporting uses this hierarchy for customer branch/restaurant permission evaluation; this client is excluded from the generic `ServiceWorkload` policy and receives no pricing or mutation access. No Restaurant schema change is required.
