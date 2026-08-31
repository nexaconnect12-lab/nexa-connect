# Payment Review browser contract acceptance

Run from `src/Frontend`:

```powershell
npx playwright install chromium
npm run test:e2e:payment-review
```

The suite starts a local Customer Portal Vite server on `127.0.0.1:5177` (port must be free) and intercepts all BFF calls with synthetic data. It verifies denied/read-only controls, required reasons, explicit confirmation, CSRF header and version submission, committed history display, stale-version refresh without automatic retry, and tenant-switch state clearing. It needs no credentials, databases, or payment-provider access. Traces, screenshots and video are disabled.

This is browser contract coverage, **not** joined live OIDC/BFF/Order verification. .NET tests separately exercise real cookie/anti-forgery and service authorization boundaries. Before releasing to operators, rehearse the complete authenticated route on disposable infrastructure with branch-scoped read-only/resolver accounts, tenant switching, concurrent decisions, dependency failure, and independent provider reconciliation evidence. See [operator contract and limitations](../../../../docs/API/Payment-Review-Operator-UI.md).
