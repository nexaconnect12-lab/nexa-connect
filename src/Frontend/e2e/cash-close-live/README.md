# Real cash-close browser acceptance

Run through [the repository launcher](../../../../scripts/test-cash-close-portal.ps1), not against an existing environment. The five serial scenarios use real Keycloak sign-in and real BFF/service responses. The route handler only restricts browser origins; it never synthesizes a response.

The fixture executable updates source state through POS repositories; the live scanner and broker must deliver each version before assertions succeed. A loopback TCP proxy owned by this test process introduces a real POS-access outage. Read revocation uses a fixture-only explicit deny while retaining the browser session.

Run `node --test e2e/cash-close-live/settings.test.mjs` from `src/Frontend` for guard and evidence checks. The safe reporter rejects fewer/more than five results, duplicates, skipped cases and failures. Screenshots, traces and videos are disabled; only summary JSON is uploaded. See [the runbook](../../../../docs/Deployment/Cash-Close-Portal-Acceptance.md) for configuration, scope and limitations.
