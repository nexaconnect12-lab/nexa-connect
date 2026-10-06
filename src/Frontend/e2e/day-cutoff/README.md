# Day-cutoff browser contracts

From `src/Frontend`, run `npm run test:e2e:day-cutoff` after installing the frontend dependencies and Playwright Chromium. `playwright.day-cutoff.config.mjs` starts the actual Customer Portal through a test-owned Vite server on loopback port 5181, uses one Chromium worker and closes that server at teardown. The port must be available. Screenshots, video and traces are disabled; the list reporter and `test-results/day-cutoff` retain ordinary test status only.

Eight cases intercept BFF responses to verify capture/CSRF/version forwarding and refresh after invalidation, accountant read-only controls, exact-operation retry after uncertain POST, late-response clearing after filter changes, tenant clearing, malformed Ready rejection, browser-reload pending-operation resume and explicit replacement using the saved version. Fixtures contain synthetic identities and financial evidence. No actual identity provider, BFF, source service, broker or database runs in this suite.

All eight cases passed locally on 2026-10-06 without skips or retries. Backend PostgreSQL/Application verification and joined real-OIDC/source-service acceptance are separate; the latter remains open for the new cutoff controls. See the [rollout and evidence boundaries](../../../../docs/Deployment/Day-Close-Cutoffs.md).
