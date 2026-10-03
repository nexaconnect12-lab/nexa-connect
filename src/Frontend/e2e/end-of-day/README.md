# End-of-day portal browser contracts

Run `npm run test:e2e:end-of-day` from `src/Frontend`. The config reuses the test-owned in-process financial portal Vite setup on loopback port 5181, while this suite intercepts BFF replies. Six Chromium cases cover source totals/timezone/issues, 403 and 503 clearing, generation-fenced filter changes, tenant changes and mismatched scope rejection. Financial report requests are GET-only.

These are synthetic browser contracts. They do not verify live OIDC, deployed source hosts, database permissions, provider execution or settlement. Owning-service PostgreSQL and HTTP boundary tests are separate; see [deployment/verification](../../../../docs/Deployment/End-Of-Day-Draft.md).
