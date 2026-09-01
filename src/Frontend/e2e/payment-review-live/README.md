# Joined Payment Review browser acceptance

This opt-in six-scenario suite uses real Keycloak login, Customer BFF cookies/CSRF, and Order HTTP calls. Unlike `test:e2e:payment-review`, it does not fulfill API calls with synthetic responses. It mutates designated review fixtures; do not run against real financial data.

## Required disposable stack and fixtures

Provision an isolated application/identity environment independently of the [operations-matrix container launcher](../../../../docker/payment-review-acceptance/README.md). That launcher verifies PostgreSQL/RabbitMQ/alerts and intentionally does not start an OIDC application stack. The Phase 8 development launcher also does not start Order.

Use a fresh run ID (32 lowercase hex digits) and a dedicated loopback Keycloak realm named `nexa-review-it-<run-id>`. Configure the actual BFF and services to trust this issuer and their normal clients/audiences; use confidential Authorization Code + PKCE and real workload identities, not authentication test doubles. Serve the updated Customer BFF/SPA over loopback HTTPS. Start Platform Directory, Authorization, Restaurant, Order, Inventory and Kitchen with separate disposable service-owned databases. Apply their current migrations (in particular Order/Authorization 4). Configure Reporting 13 and outbox/consumer dependencies when verifying publication. The suite does not install realms, grant permissions, launch services or seed databases.

Provide two synthetic accounts: a read-only user with `order.payment-review.read` but not `.resolve` for the test branch, and a resolver with both permissions. The resolver must have active `nexa_connect` membership in two distinct organizations so cross-tenant resource denial is tested after a valid tenant switch. Do not use platform roles as customer grants.

Prepare four distinct, fresh Order-owned cases for concurrency/escalation, resume-payment, confirm-void, and lost-response/escalation. Every case must:

- Belong to the supplied organization/branch and be visible within the oldest 100 open cases.
- Have status `open`, no committed review history, a matching aggregate in `PaymentReview` state, and valid organization/payment-intent ownership.
- Have initial reason exactly `browser-acceptance:<run-id>`; this explicit fixture marker is checked before mutation.
- Use synthetic payment identities only. Confirm-void requires independently verified uncaptured/voided evidence plus safely isolated Inventory/Kitchen compensation records. No real provider funds may be involved.

Use service-owned fixture setup through the repository/migration boundaries; do not update another running service's database or erase history to reset a test. Automated application/identity/fixture provisioning is not supplied by this slice. After a run, retain evidence and retire the entire disposable fixture environment through its owner. The suite never deletes immutable history and refuses previously used mutation fixtures.

## Secret-injected settings

All settings below have prefix `NEXACONNECT_REVIEW_LIVE_`. Missing settings cause a nonzero configuration failure before any browser/network activity, not a successful skipped suite.

| Suffix | Value |
| --- | --- |
| `ENABLED`, `CONFIRM_DISPOSABLE` | Both exactly `1` |
| `RUN_ID` | Fresh 32-character lowercase hexadecimal ID |
| `BASE_URL` | Loopback HTTPS Customer BFF origin, no path/query/credentials |
| `OIDC_ISSUER` | Loopback HTTP/HTTPS `/realms/nexa-review-it-<run-id>`, no trailing slash |
| `RESOLVER_USERNAME`, `RESOLVER_PASSWORD` | Secret-injected resolver credentials |
| `READER_USERNAME`, `READER_PASSWORD` | Distinct read-only account credentials |
| `ORGANIZATION_ID`, `OTHER_ORGANIZATION_ID`, `BRANCH_ID` | Synthetic tenant and branch UUIDs |
| `CONCURRENCY_ORDER_ID`, `RESUME_ORDER_ID`, `VOID_ORDER_ID`, `LOST_RESPONSE_ORDER_ID` | Four distinct fresh fixture order UUIDs |

No remote override exists. Browser requests are restricted to the BFF and configured issuer origins, and the login realm path is checked before entering credentials. A loopback URL or marker is not proof of disposability: operators must confirm the backing databases and downstream services independently.

The browser harness sets `ignoreHTTPSErrors: true` for loopback development certificates. This is not certificate-trust or production TLS acceptance, and does not relax BFF/service-to-service certificate validation. Keep service trust correctly configured; do not copy the browser setting into application HTTP clients.

From `src/Frontend`, after injection:

```powershell
npx playwright install chromium
npm run test:payment-review:guards
npm run test:e2e:payment-review:live
```

## Coverage and evidence boundary

The six scenarios test read-only UI and server enforcement; CSRF rejection and cross-tenant 404s; two independently authenticated competing sessions with exactly one commit and stale-UI refresh; explicitly confirmed resume-payment and confirm-void with actor/decision history; and a committed escalation whose response is deliberately dropped at the browser transport boundary. The last scenario forwards the real request before aborting delivery; it is **not** an Inventory/Kitchen/Order outage or a concrete-provider test.

There are no mutation retries. The sanitized reporter requires all six distinct scenarios to pass without skips/retries/partial selection; otherwise evidence is incomplete and exit status is nonzero. It stores only run ID, result/counts, timestamp and verification flag in `test-results/payment-review-live/<run-id>/summary.json`. Trace, screenshot and video are disabled; any Playwright-created failure context still needs restricted access/short retention because it may contain fixture identifiers and page text. Never publish raw service logs or browser artifacts without review. Diagnose via service `nexaconnect-customer-bff`/`nexaconnect-order` correlation logs without recording credentials, tokens, bodies or payment details.

Passing this suite does not prove production paging/acknowledgement, provider reconciliation semantics, real downstream outage recovery or live-traffic rollback. Those remain explicit release rehearsals. The harness has not been run against a joined live stack in this implementation environment; only guard tests, syntax/collection and existing synthetic browser regressions are available until the required accounts and fixtures are supplied.
