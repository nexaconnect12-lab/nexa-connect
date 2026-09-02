# Joined Payment Review browser acceptance

This opt-in seven-scenario suite uses real Keycloak login, Customer BFF cookies/CSRF, Order HTTP calls, and a run-scoped Inventory fault proxy. Unlike `test:e2e:payment-review`, it does not fulfill API calls with synthetic responses. It mutates designated review fixtures; do not run against real financial data.

## Required disposable stack and fixtures

Use the [joined launcher](../../../../docker/payment-review-joined/README.md) with `-RunLiveBrowser`. It creates fresh PostgreSQL, Keycloak, RabbitMQ, and Toxiproxy infrastructure; migrates the four fixture databases; creates the two users and fixtures; starts the seven application hosts; routes Order-to-Inventory through the run-scoped proxy; executes this suite; and tears down child processes and Compose resources in the same lifecycle. The operations-matrix launcher remains a separate PostgreSQL/RabbitMQ/alert verification boundary.

Use a fresh run ID (32 lowercase hex digits) and a dedicated loopback Keycloak realm named `nexa-review-it-<run-id>`. Configure the actual BFF and services to trust this issuer and their normal clients/audiences; use confidential Authorization Code + PKCE and real workload identities, not authentication test doubles. Serve the updated Customer BFF/SPA over loopback HTTPS. Start Platform Directory, Authorization, Restaurant, Order, Inventory and Kitchen with separate disposable service-owned databases. Apply their current migrations (in particular Order 4 and Authorization 5). Configure Reporting 13 and outbox/consumer dependencies when verifying publication. The suite does not install realms, grant permissions, launch services or seed databases.

Place a disposable Toxiproxy proxy between Order and Inventory, name it exactly `nexa-review-it-<run-id>-inventory`, and expose its control API only on loopback. Before enabling the suite, independently inspect that proxy's upstream points to this run's disposable Inventory instance and that Order's `Services__Inventory` address points to the proxy listener. Start with the proxy enabled and verify normal Inventory connectivity. The name and loopback control URL are guardrails, not proof of upstream ownership. Do not expose the unauthenticated control API to another host or reuse a proxy shared with development or production traffic.

Provide two synthetic accounts: a branch-scoped `accountant` with `order.payment-review.read` but not `.resolve`, and a restaurant-scoped `store-manager` or organization-scoped `tenant-admin` resolver with both permissions. Authorization migration 5 and the runtime assignment path preserve this separation. The resolver must have active `nexa_connect` membership in two distinct organizations so cross-tenant resource denial is tested after a valid tenant switch. Do not use platform roles as customer grants.

Prepare five distinct, fresh Order-owned cases for concurrency/escalation, resume-payment, confirm-void, Inventory-outage confirm-void, and lost-response/escalation. Every case must:

- Belong to the supplied organization/branch and be visible within the oldest 100 open cases.
- Have status `open`, no committed review history, a matching aggregate in `PaymentReview` state, and valid organization/payment-intent ownership.
- Have initial reason exactly `browser-acceptance:<run-id>`; this explicit fixture marker is checked before mutation.
- Use synthetic payment identities only. Confirm-void requires independently verified uncaptured/voided evidence plus safely isolated Inventory/Kitchen compensation records. No real provider funds may be involved.

Use the test-only [Payment Review fixture tool](../../../Tools/NexaConnect.PaymentReviewAcceptance/README.md) after migrations and disposable Keycloak user creation. It provisions through service-owned repository/application boundaries and emits the required synthetic IDs without credentials. Do not update another running service's database or erase history to reset a test. Application/identity/database launch and cleanup are not supplied by the tool. After a run, retain evidence and retire the entire disposable fixture environment through its owner. The suite never deletes immutable history and refuses previously used mutation fixtures.

## Secret-injected settings

All settings below have prefix `NEXACONNECT_REVIEW_LIVE_`. Missing settings cause a nonzero configuration failure before any browser/network activity, not a successful skipped suite.

| Suffix | Value |
| --- | --- |
| `ENABLED`, `CONFIRM_DISPOSABLE` | Both exactly `1` |
| `RUN_ID` | Fresh 32-character lowercase hexadecimal ID |
| `BASE_URL` | Loopback HTTPS Customer BFF origin, no path/query/credentials |
| `OIDC_ISSUER` | Loopback HTTP/HTTPS `/realms/nexa-review-it-<run-id>`, no trailing slash |
| `FAULT_CONTROL_URL` | Loopback HTTP origin of the disposable Toxiproxy API, no path/query/credentials |
| `FAULT_PROXY_NAME` | Exactly `nexa-review-it-<run-id>-inventory`; Order's Inventory URL must traverse this proxy |
| `RESOLVER_USERNAME`, `RESOLVER_PASSWORD` | Secret-injected resolver credentials |
| `READER_USERNAME`, `READER_PASSWORD` | Distinct read-only account credentials |
| `ORGANIZATION_ID`, `OTHER_ORGANIZATION_ID`, `BRANCH_ID` | Synthetic tenant and branch UUIDs |
| `CONCURRENCY_ORDER_ID`, `RESUME_ORDER_ID`, `VOID_ORDER_ID`, `OUTAGE_ORDER_ID`, `LOST_RESPONSE_ORDER_ID` | Five distinct fresh fixture order UUIDs |

No remote override exists. Browser requests are restricted to the BFF and configured issuer origins, and the login realm path is checked before entering credentials. A loopback URL or marker is not proof of disposability: operators must confirm the backing databases and downstream services independently.

The browser harness sets `ignoreHTTPSErrors: true` for loopback development certificates. This is not certificate-trust or production TLS acceptance, and does not relax BFF/service-to-service certificate validation. Keep service trust correctly configured; do not copy the browser setting into application HTTP clients.

From `src/Frontend`, after injection:

```powershell
npx playwright install chromium
npm run test:payment-review:guards
npm run test:e2e:payment-review:live
```

## Coverage and evidence boundary

The seven scenarios test read-only UI and server enforcement; CSRF rejection and cross-tenant 404s; two independently authenticated competing sessions with exactly one commit and stale-UI refresh; explicitly confirmed resume-payment and confirm-void with actor/decision history; an Inventory transport outage injected by disabling the configured proxy followed by restoration and an explicit fresh confirm-void decision; and a committed escalation whose response is deliberately dropped at the browser transport boundary. The fault controller is restricted to the run-specific loopback proxy name. The outage case requires the failed attempt to leave the review open with no history, and the explicit recovery attempt to create exactly one history entry. It proves this configured Order-to-Inventory transport boundary only; it does not prove Inventory process/container loss, a concrete payment provider's state, or combined dependency behavior.

There are no mutation retries. The sanitized reporter requires all seven distinct scenarios to pass without skips/retries/partial selection; otherwise evidence is incomplete and exit status is nonzero. The suite always attempts to restore the Inventory proxy in `finally`; if restoration fails, the test fails and the disposable stack must be retired rather than reused. It stores only run ID, result/counts, timestamp and verification flag in `test-results/payment-review-live/<run-id>/summary.json`. Trace, screenshot and video are disabled; any Playwright-created failure context still needs restricted access/short retention because it may contain fixture identifiers and page text. Never publish raw service logs or browser artifacts without review. Diagnose via service `nexaconnect-customer-bff`/`nexaconnect-order` correlation logs without recording credentials, tokens, bodies or payment details.

Passing this suite does not prove production paging/acknowledgement, provider reconciliation semantics, multi-dependency failure, or live-traffic rollback. Those remain explicit release rehearsals. The joined application-host/proxy/browser lifecycle passed 7/7 locally on 2026-09-02; production-environment evidence remains a release gate.
