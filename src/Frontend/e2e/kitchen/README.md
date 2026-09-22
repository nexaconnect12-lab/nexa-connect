# Kitchen queue browser contract tests

Run `npm run test:e2e:kitchen` from `src/Frontend`. The suite starts a private Vite server on loopback port 5178 and uses Playwright Chromium. Install the browser with `npx playwright install chromium` if needed. No service credentials or live financial calls are used.

Synthetic BFF fixtures cover the full ticket lifecycle, expected versions and CSRF, read-only access, conflict/response-loss refresh without mutation replay, browser reload, failed-recovery locking, station/cursor forwarding and tenant reset. Traces, screenshots and videos are disabled. These tests are not live OIDC, PostgreSQL/broker, POS-to-Kitchen, hardware or offline acceptance. See [the Kitchen runbook](../../../../docs/API/Kitchen-Queue.md) for those release gates.
