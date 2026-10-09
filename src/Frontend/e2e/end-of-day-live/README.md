# Joined end-of-day browser acceptance

Run the [guarded launcher](../../../../scripts/test-end-of-day-portal.ps1) after dependencies, Chromium and `npm run test:end-of-day:guards` are prepared. `npm run test:e2e:end-of-day:live` requires the complete generated launcher environment; missing settings fail rather than skip.

Eight serial cases use actual OIDC, BFF, Reporting and owning Order/Payment/POS HTTP traffic. They prove Bangkok day totals/tenders/operational issues, delayed real event delivery, date/scope boundaries, real source/Reporting outages, stale filter/tenant fencing, read-only behavior and live source-permission/membership revocation. Neither browser routes nor source JSON responses are mocked. Synthetic historical fixtures and provider outcomes retain separate limitations.

Settings reuse the financial acceptance guard and additionally require explicit end-of-day mode, a closed exact Bangkok date/window and distinct generated source/listener ports. Two loopback TCP proxies hold or interrupt actual transport without inspecting bodies. Browser requests are allowed only to generated BFF/identity origins. Screenshots/traces/videos are off; safe reporting requires exactly eight unique passes without retries/skips. CI uploads only bounded summaries.

See [execution, cleanup and exclusions](../../../../docs/Deployment/End-Of-Day-Portal-Acceptance.md). The existing [synthetic suite](../end-of-day/README.md) remains a separate component/UI contract gate.
