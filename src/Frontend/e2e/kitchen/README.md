# Kitchen queue browser contract tests

Run `npm run test:e2e:kitchen` from `src/Frontend`. The suite starts a private Vite server on loopback port 5178 and uses Playwright Chromium. Install the browser with `npx playwright install chromium` if needed. No service credentials or live financial calls are used.

The 19 synthetic cases cover the ticket lifecycle, versions/CSRF, read-only access,
conflict/response-loss refresh without replay, failed-recovery locking, station/cursor
forwarding and tenant reset. Direct-entry cases add `/kitchen`/`/kitchen/`, refresh and manager
return, synthetic unauthenticated login handoff, duplicate clicks, denied/revoked reads,
unauthorized tenant/product/branch discovery, in-flight workspace clearing, sign-out clearing,
branch reset and fullscreen entry/exit/unsupported/rejected behavior. The delayed-sign-out case
observes DOM changes before a held navigation through a test-only MutationObserver/binding.

Explicit desktop/compact screenshots contain synthetic values and live in ignored
`test-results/kitchen`. Automatic screenshots, trace and video remain disabled. These tests are
not real OIDC, PostgreSQL/broker, POS-to-Kitchen, hardware or offline acceptance.
See [the Kitchen runbook](../../../../docs/API/Kitchen-Queue.md) and
[Phase 3 deployment and acceptance limits](../../../../docs/Deployment/Next-Iteration-Phase-3.md).
