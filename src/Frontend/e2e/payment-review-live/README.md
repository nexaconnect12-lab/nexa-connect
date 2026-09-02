# Joined Payment Review browser acceptance

This opt-in ten-scenario suite uses real Keycloak login, Customer BFF cookies/CSRF, Order HTTP calls, a run-scoped Inventory fault proxy, and actual disposable Inventory/Kitchen process stop/restart. Unlike `test:e2e:payment-review`, it does not fulfill API calls with synthetic responses. It mutates designated review fixtures; do not run against real financial data.

## Required disposable stack and fixtures

Use the [joined launcher](../../../../docker/payment-review-joined/README.md) with `-RunLiveBrowser`. It creates fresh PostgreSQL, Keycloak, RabbitMQ, and Toxiproxy infrastructure; migrates the four fixture databases; creates the two users and fixtures; starts the seven application hosts; routes Order-to-Inventory through the run-scoped proxy; executes this suite; and tears down child processes and Compose resources in the same lifecycle. The operations-matrix launcher remains a separate PostgreSQL/RabbitMQ/alert verification boundary.

Use a fresh run ID (32 lowercase hex digits) and a dedicated loopback Keycloak realm named `nexa-review-it-<run-id>`. Configure the actual BFF and services to trust this issuer and their normal clients/audiences; use confidential Authorization Code + PKCE and real workload identities, not authentication test doubles. Serve the updated Customer BFF/SPA over loopback HTTPS. Start Platform Directory, Authorization, Restaurant, Order, Inventory and Kitchen with separate disposable service-owned databases. Apply their current migrations (in particular Order 4 and Authorization 5). Configure Reporting 13 and outbox/consumer dependencies when verifying publication. The suite does not install realms, grant permissions, launch services or seed databases.

Place a disposable Toxiproxy proxy between Order and Inventory, name it exactly `nexa-review-it-<run-id>-inventory`, and expose its control API only on loopback. Before enabling the suite, independently inspect that proxy's upstream points to this run's disposable Inventory instance and that Order's `Services__Inventory` address points to the proxy listener. Start with the proxy enabled and verify normal Inventory connectivity. The name and loopback control URL are guardrails, not proof of upstream ownership. Do not expose the unauthenticated control API to another host or reuse a proxy shared with development or production traffic.

Provide two synthetic accounts: a branch-scoped `accountant` with `order.payment-review.read` but not `.resolve`, and a restaurant-scoped `store-manager` or organization-scoped `tenant-admin` resolver with both permissions. Authorization migration 5 and the runtime assignment path preserve this separation. The resolver must have active `nexa_connect` membership in two distinct organizations so cross-tenant resource denial is tested after a valid tenant switch. Do not use platform roles as customer grants.

Prepare eight distinct, fresh Order-owned cases for concurrency/escalation, resume-payment, confirm-void, Inventory transport-outage confirm-void, lost-response/escalation, Inventory process loss, Kitchen process loss, and combined process loss. Every case must:

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
| `PROCESS_CONTROL_URL`, `PROCESS_CONTROL_TOKEN` | Loopback-only joined supervisor and generated 64-hex bearer token; never expose or persist the token |
| `RESOLVER_USERNAME`, `RESOLVER_PASSWORD` | Secret-injected resolver credentials |
| `READER_USERNAME`, `READER_PASSWORD` | Distinct read-only account credentials |
| `ORGANIZATION_ID`, `OTHER_ORGANIZATION_ID`, `BRANCH_ID` | Synthetic tenant and branch UUIDs |
| `CONCURRENCY_ORDER_ID`, `RESUME_ORDER_ID`, `VOID_ORDER_ID`, `OUTAGE_ORDER_ID`, `LOST_RESPONSE_ORDER_ID`, `INVENTORY_PROCESS_ORDER_ID`, `KITCHEN_PROCESS_ORDER_ID`, `COMBINED_PROCESS_ORDER_ID` | Eight distinct fresh fixture order UUIDs |

No remote override exists. Browser requests are restricted to the BFF and configured issuer origins, and the login realm path is checked before entering credentials. A loopback URL or marker is not proof of disposability: operators must confirm the backing databases and downstream services independently.

The browser harness sets `ignoreHTTPSErrors: true` for loopback development certificates. This is not certificate-trust or production TLS acceptance, and does not relax BFF/service-to-service certificate validation. Keep service trust correctly configured; do not copy the browser setting into application HTTP clients.

From `src/Frontend`, after injection:

```powershell
npx playwright install chromium
npm run test:payment-review:guards
npm run test:e2e:payment-review:live
```

## Coverage and evidence boundary

The ten scenarios test read-only and tenant/CSRF enforcement; competing sessions and stale-UI refresh; confirmed resume/void with attribution; Inventory proxy outage; individual Inventory and Kitchen process loss; combined Inventory/Kitchen process loss; and a committed escalation whose response is dropped at the browser boundary. Every fault case requires the failed attempt to leave the review open with no history and requires a fresh explicit decision after restoration. The process controller is bearer-protected, loopback-only, and allow-lists exactly `inventory` and `kitchen`; it cannot execute arbitrary commands or target another process.

There are no mutation retries. The sanitized reporter requires all ten distinct scenarios to pass without skips/retries/partial selection. Each fault test restores dependencies in `finally`; a failed restoration retires the disposable stack. Evidence stores only run ID, result/counts, timestamp, and verification flag. Trace, screenshot, and video are disabled; failure context and service logs still require restricted access and review.

Passing this suite does not prove production paging/acknowledgement, provider reconciliation semantics, container-orchestrator behavior, persistent Inventory/Kitchen restart recovery, or live-traffic rollback. The supervised services use in-memory acceptance persistence, so the process-loss checks prove Order remains fail-closed and requires a fresh decision, not production state restoration. The joined lifecycle passed 10/10 locally on 2026-09-02; production-environment evidence remains a release gate.
