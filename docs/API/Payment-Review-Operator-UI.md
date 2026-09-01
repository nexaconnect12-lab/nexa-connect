# Payment Review operator UI

The Customer Portal exposes `#payment-reviews` in a selected `nexa_connect` workspace. Order owns cases and immutable decision history; the browser uses the Customer BFF, never product-service tokens or databases. This slice adds no financial transition, schema, or permission grant. Resolution conflict handling is tightened: a non-open case now returns 409 instead of a successful unchanged response, preventing the UI from claiming that a competing operator's decision was saved by the current request.

## BFF contract

All routes require `CustomerSession` and return no-store responses. Except the CSRF bootstrap, each request validates the protected tenant cookie against the authenticated subject and current Platform Directory organization/product access. Organization and product headers are reconstructed server-side; browser-supplied organization, actor, and authorization-decision fields cannot override them.

| Method and path under `/bff/customer/payment-reviews` | Behavior |
| --- | --- |
| `GET /csrf` | Returns `{requestToken}` and sets the secure HTTP-only anti-forgery cookie |
| `GET /branches/{branchId}/access` | Returns `{canRead,canResolve}` from Order's branch permission checks; not an authorization grant |
| `GET /branches/{branchId}` | Up to 100 actionable open cases, oldest first; excludes unexpired resolution leases |
| `GET /{orderId}` | Current case including status, reason, concurrency version and timestamps |
| `GET /{orderId}/history` | Most recent 100 committed decisions, descending concurrency version |
| `POST /{orderId}/resolve` | `{resolution,reason,expectedConcurrencyVersion}`; requires `X-Nexa-CSRF` from the bootstrap and its matching cookie |

Resolution is exactly `confirm_void`, `resume_payment`, or `escalate`. Reason must contain non-whitespace text, at most 200 characters; version must be positive. Request bodies are limited to 4096 bytes by the HTTP server. Actor and Authorization decision attribution remain Order-owned. CSRF tokens remain in memory, not browser storage. The `__Host-nexa-payment-review-csrf` cookie is Secure, HTTP-only, SameSite=Strict, Path=/.

Missing sessions return 401; invalid/revoked product access returns 403; missing/cross-tenant/unauthorized detail and history return 404; stale or non-open resolution returns 409; validation or missing/invalid anti-forgery proof returns 400. Downstream non-success bodies are replaced with a generic title. Dependency transport failure/timeout returns 503. No automatic mutation retry is configured.

## Owning Order API additions

- `GET /api/order/v1/payment-reviews/branches/{branchId}/access?organizationId={id}` checks `order.payment-review.read` and, only when readable, `order.payment-review.resolve`. A denied authenticated probe returns false flags.
- `GET /api/order/v1/payment-reviews/{orderId}/history?organizationId={id}` first reads the tenant-owned case and enforces branch read permission, then returns a bounded list of `{id,action,reason,actorSubjectId,authorizationDecisionId,concurrencyVersion,occurredAtUtc}`. The repository filters both organization and order. It reads Order migration-4 history, not the eventually consistent Reporting projection.

Existing list/detail/resolve endpoints still enforce authorization on every request; a previously successful permission probe does not bypass revocation. Active resolving cases may be inspected by ID but are absent from actionable lists. Expired claims appear open; a fenced retry can still reject a different resolution with 409.

## Operator workflow and limits

Enter a branch UUID supplied by the administrator, load reviews, and select a case. A UUID input is intentional: the existing organization-wide branch list requires organization-scoped administration and would exclude restaurant-scoped managers. Branch search/discovery and pagination are follow-up work.

Read-only operators see cases and history but no decision controls. Resolvers must enter a reason and explicitly confirm the selected action/order/reason. Do not enter card data, provider credentials, or personal information. Confirm void does **not** query the provider: independently reconcile payment evidence through the [operations runbook](../Deployment/Payment-Capture-Recovery-Runbook.md) first. It performs Inventory/Kitchen compensation before the Order commit. Resume payment returns the same bound intent to pending reconciliation; it does not initiate capture or create a new payment. Escalation leaves the case open with a new version.

After success, conflict, or an ambiguous failure, the UI clears the decision and refreshes case/history/list. A conflict requires a fresh explicit choice and confirmation; no retry is automatic. Failed refresh clears actionable detail. Changing branch clears selection; changing tenant unmounts the panel and aborts its requests so old responses cannot render in the new workspace. Aborting a browser request does not prove the server cancelled a submitted financial decision; inspect authoritative state before deciding again.

## Deployment, diagnostics and verification

The [isolated matrix launcher](../../docker/payment-review-acceptance/README.md) provisions disposable PostgreSQL/RabbitMQ/alert resources and passed 13/13 locally. The new [live browser harness](../../src/Frontend/e2e/payment-review-live/README.md) uses real OIDC/BFF/Order calls but requires independently provisioned accounts, services and fresh synthetic fixtures; its execution remains a release gate. No API contract changed in this acceptance increment.

Deploy Order with PostgreSQL migration 4 and Authorization migration-4 grants before the updated Customer BFF/SPA. Reporting migration 13 remains required for emitted audit projection. No new migrations, application-runtime environment variables, or grants are introduced; the separate acceptance harness adds test-only settings documented in its guide. Configure the existing `Services__Order` URL. The Phase 8 launcher does not start Order: start it separately with its documented persistence, identity, Inventory and Kitchen dependencies. Serve the portal same-origin over HTTPS so both protected cookies and anti-forgery work.

The BFF uses `nexaconnect-customer-bff` JSON/OTLP telemetry and validated `X-Correlation-ID` propagation to Platform Directory and Order (`nexaconnect-order`). For Loki, start with `{service_name="nexaconnect-customer-bff"} | json | CorrelationId="<id>"`; use the same correlation under `nexaconnect-order`. Dependency/authorization events contain bounded operation/status/permission, never reason text, tokens, arbitrary headers, or provider bodies. See the [observability guide](../Deployment/Observability.md) for exporter-label mapping.

Run `dotnet test tests/Integration/NexaConnect.IntegrationTests --filter "FullyQualifiedName~CustomerPaymentReview|FullyQualifiedName~PaymentReviewHttpAcceptanceTests"` for HTTP/adapter boundaries. The guarded operations runner now expects 13 cases, including five new history/permission HTTP cases; prior eight-case evidence remains historical. The Order migration runner also verifies history fields and cross-tenant filtering against PostgreSQL.

From `src/Frontend`, run `npm run check`, `npm test`, `npm run build`, and `npm run test:e2e:payment-review`. The [browser suite](../../src/Frontend/e2e/payment-review/README.md) uses synthetic BFF responses. Joined live OIDC-to-BFF-to-Order operator acceptance, provider reconciliation evidence, production receiver acknowledgement and calibrated alert thresholds remain release gates; passing these component/browser tests does not certify them.
