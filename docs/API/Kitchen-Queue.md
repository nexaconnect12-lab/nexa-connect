# Kitchen operator queue v1

The Customer Portal's **Kitchen queue** screen is an online, single-branch, ticket-level operator workflow. The Customer BFF owns the cookie session and protected tenant selection; Kitchen owns authorization, ticket lifecycle, persistence, history and events. No payment or Order status is changed by this screen. [ADR-011](../Architecture/Decisions/ADR-011-online-kitchen-queue.md) records the preparation/payment policy and scope.

## API and authorization

Kitchen requires an authenticated bearer token, `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, active product membership and exact Restaurant-owned branch scope. The Order workload cannot use operator routes. Read access uses `kitchen.ticket.read`; every transition independently checks `kitchen.ticket.transition`. Existing Authorization migration 3 grants remain applicable; no new permission or migration is introduced.

| Route | Contract |
| --- | --- |
| `GET /api/kitchen/v1/branches/{branchId}/tickets` | Optional `station`, `limit` (1–100; default 50), `cursor`. Returns `{ items, nextCursor, canTransition }`. |
| `GET /api/kitchen/v1/branches/{branchId}/tickets/{ticketId}` | Authoritative ticket including terminal states; used to verify uncertain mutations. |
| `POST /api/kitchen/v1/branches/{branchId}/tickets/{ticketId}/transitions` | Existing `{ targetStatus, expectedConcurrencyVersion, reasonCode? }`; positive version, defined status and printable reason of at most 64 characters. |

Queue items retain the existing Kitchen ticket contract: `ticketId`, `organizationId`, `restaurantId`, `orderId`, `branchId`, `preparationStationId`, `status`, `concurrencyVersion`, `queuedAtUtc`, and `lines` (`productId`, `name`, `quantity`, `preparationStation`). Status uses `Queued`, `InProgress`, `Ready`, `Completed`, `Cancelled` JSON names. Only Queued/InProgress/Ready tickets appear in the queue. Station filters are exact codes after trim/lowercase normalization, limited to 100 printable characters. They use existing ticket station snapshots; they do not create canonical Restaurant station identities.

Results sort by queued UTC time, then ticket UUID, oldest first. `nextCursor` is an opaque-to-clients position: return it unchanged with the same branch/station filter. It is a filter, never an authorization token. The service always re-applies tenant/branch predicates. Keyset pagination tolerates removal of earlier tickets; it is not a snapshot across requests. Refresh the first page to see new work or previously passed tickets. One PostgreSQL statement loads bounded ticket headers and their lines, using the existing migration-3 organization/branch/status/time index; no cross-service database access occurs.

Operator routes return `401` without authentication, `403` for missing/denied context, `404` for an absent or differently owned ticket after branch authorization, `400` for invalid input, and `409` for stale versions or illegal transitions. Database exceptions, HTTP transport exceptions and dependency timeouts map to `503`. Non-success access responses from Platform Directory, Restaurant or Authorization fail closed as denied access: a required permission check returns `403`, while the queue's additional transition-permission check yields `canTransition: false`. These responses are not classified as dependency outages. Responses are no-store. A same-target transition with the current version is a no-op; an old version conflicts even if its target is now current.

The Customer BFF exposes the same suffixes beneath `/bff/customer/kitchen/branches/{branchId}/tickets`, plus `GET /bff/customer/kitchen/csrf`. Its POST accepts only `{ targetStatus, expectedConcurrencyVersion }`, with target `InProgress`, `Ready` or `Completed`; it requires the antiforgery cookie and `X-Nexa-CSRF`. Browser-supplied tenant or actor values are not forwarded. Forwarded queue/detail/transition requests revalidate protected subject/product/organization membership through Platform Directory, then Kitchen independently authorizes the resource. Missing or subject-mismatched tenant selection returns `401`; denied membership or a non-success Directory access response returns `403`. The CSRF endpoint requires an authenticated Customer session but does not itself validate tenant membership. Downstream error bodies are replaced with safe errors. The existing shared BFF antiforgery cookie name remains `__Host-nexa-payment-review-csrf` for compatibility; it now also protects Kitchen transitions.

## Operator behavior and recovery

Enter an administrator-supplied branch UUID and optional station code; organization-wide branch-list permission is unnecessary. Each page contains up to 50 tickets. The current page polls every ten seconds; **Refresh queue** returns to the first page. Read-only operators see disabled transition buttons. Branch, station, or tenant changes clear the displayed queue; leaving the panel aborts its pending browser requests.

**Start → Ready → Complete** acts on one ticket with its displayed version. Buttons lock while reading or mutating and after a failed refresh. Requests have a 20-second browser timeout; the BFF Kitchen client has a 15-second timeout. A timeout does not prove that the server did not commit. After success, conflict, or response loss, the screen reads the affected ticket and refreshes the first page. It never automatically resends a transition and never persists mutation commands in browser storage. Reloading the browser requires a fresh queue read. Polling may restore actions once fresh authoritative queue state is available.

PostgreSQL row locks and expected versions serialize competing mutations. Accepted transitions commit ticket/item status, append-only history, audit, and lifecycle/outbox records together. Broker failure does not roll back committed preparation; the existing dispatcher publishes retained events after recovery. In-memory mode is development-only, non-durable, and has no outbox/audit persistence. Both stores validate all station tickets before allowing order-wide cancellation to commit.

Preparation can start and complete before payment settles, preserving the existing Order → Kitchen → Payment sequence and manual-tender behavior. A definitive payment failure can cancel queued/in-progress/ready tickets; cancellation makes a stale operator action conflict. Completed preparation is terminal and causes whole-order Kitchen cancellation to return `409`. This does not fabricate financial success or undo completed food preparation. Order's existing compensation retry/error handling remains responsible for the failure; it does not automatically create a new Payment Review case for this Kitchen conflict. Inventory release may already have occurred in the cross-service compensation sequence. Operators must investigate the original Order/payment and inventory/wastage outcome; automated post-preparation financial reconciliation is future work. This slice is not production sign-off for that workflow.

## Deployment, diagnostics and verification

Deploy Kitchen before the Customer BFF/SPA. Kitchen remains at migration 3; Authorization migration 3 and Reporting migration 5 must precede relevant operator grants and outbox consumption. Use the [Kitchen production template](../Deployment/kitchen.production.env.example), PostgreSQL persistence, service-owned credentials and workload identity. Set BFF `Services__Kitchen` to the actual trusted Kitchen URL; Development defaults to `https://localhost:52273/`. The Phase 8 launcher does not start Kitchen. If connecting to the checkout launcher, override the BFF endpoint to that launcher's Kitchen address (`http://localhost:5274/` for local Development only); avoid starting two stacks on overlapping ports. No branch edge or WAN-offline execution is introduced.

Rollback the BFF/SPA first, then Kitchen if needed. There is no new database migration to downgrade. Existing ticket history and events remain compatible. Re-authenticate and verify branch read/transition permissions after deployment.

Service names are `nexaconnect-customer-bff` and `nexaconnect-kitchen`. Validated correlation identifiers propagate BFF → Kitchen → authorization dependencies and into mutation events. Query Loki `{service_name="nexaconnect-customer-bff"} |= "Kitchen BFF"`, `{service_name="nexaconnect-kitchen"} |= "Kitchen operator"`, and `{service_name="nexaconnect-kitchen"} |= "Kitchen compensation blocked"`, then filter by the request's correlation ID. Events report bounded operation/status or denial/conflict/dependency categories; never tokens, headers, bodies, payment fields or line snapshots.

Run from the repository root:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --filter FullyQualifiedName~Kitchen
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter "FullyQualifiedName~Kitchen|FullyQualifiedName~RestaurantWorkflowCrossService"
```

Run `npm run check`, `npm test`, `npm run test:e2e:kitchen`, and `npm run build --workspace @nexaconnect/customer-portal` from `src/Frontend`. Browser tests use synthetic BFF responses; HTTP tests use test authentication/ports. They do not certify live OIDC, PostgreSQL, broker delivery, or a physical kitchen device.

PostgreSQL tests require secret-injected `NEXACONNECT_KITCHEN_INTEGRATION_DB` and `NEXACONNECT_ENVIRONMENT=Testing` against verified disposable infrastructure. They create/drop a generated schema and cover tenant/branch/station filtering, tied-timestamp pagination, stale clients, cancellation and transaction rollback. Existing Kitchen RabbitMQ acceptance also requires `NEXACONNECT_RABBITMQ_ACCEPTANCE=1` and `NEXACONNECT_RABBITMQ_INTEGRATION_URI`. Absent settings skip these tests; a skipped test is not acceptance evidence.

Before release, retain joined POS → Kitchen → operator evidence with live identity and PostgreSQL: one order per station after replay, two-screen conflicts, branch/tenant denial, restart/response loss, payment cancellation during preparation, completed-preparation cancellation conflict, and outbox publication after broker recovery. Hardware/offline KDS, item transitions, canonical station management, adjustments and automatic financial exception resolution remain separate slices.
