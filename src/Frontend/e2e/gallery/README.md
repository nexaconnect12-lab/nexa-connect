# Approved gallery browser checks

From `src/Frontend`, run `npm run test:e2e:gallery`. The dedicated Playwright
configuration starts the Customer Portal on loopback port 5198 with strict port
ownership and no reuse of an existing server. It uses one Chromium worker and no
automatic retries.

The seven cases intercept BFF requests with synthetic identities, branches and
Media metadata. They cover explicit approval, separate ordered staff/customer
lists, readiness feedback, stale/unconfirmed saves without automatic mutation
retry, denied reads and tenant-change clearing. They do not start Keycloak, Restaurant, Media,
object storage or a scanner and do not prove physical POS behavior.

Screenshots use synthetic data under ignored `test-results/gallery`; traces and
videos are disabled. Keep real customer data and credentials out of fixtures.
Backend boundary and PostgreSQL checks are recorded separately in the
[Phase 5 guide](../../../../docs/Deployment/Next-Iteration-Phase-5.md).
