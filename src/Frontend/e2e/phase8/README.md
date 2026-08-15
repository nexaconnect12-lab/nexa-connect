# Phase 8 joined browser acceptance

This opt-in Playwright suite exercises the real Customer Portal, Keycloak login, Customer BFF tenant selection, Media API, presigned MinIO transfer, ClamAV validation, PostgreSQL lifecycle, and asynchronous Media worker. It proves an authorized tenant can upload, process, download, and delete a Catalog-owned image and that an ungranted organization selection is denied.

The suite does not provision users or business data. Start the disposable development stack described in the Customer Portal README and seed a customer user with `nexa_connect` access, an organization-scoped `tenant-admin` assignment, and a Catalog product owned by that organization. Never point the suite at production.

Install the pinned Chromium runtime once from `src/Frontend`:

```powershell
npm install
npm run test:e2e:phase8:install
```

Start the Phase 8 stack, then configure the acceptance identity and seed identifiers without committing their values:

```powershell
$env:NEXACONNECT_PHASE8_E2E = '1'
$env:NEXACONNECT_PHASE8_E2E_USERNAME = '<customer-username>'
$env:NEXACONNECT_PHASE8_E2E_PASSWORD = '<customer-password>'
$env:NEXACONNECT_PHASE8_E2E_ORGANIZATION_ID = '<organization-uuid>'
$env:NEXACONNECT_PHASE8_E2E_CATALOG_PRODUCT_ID = '<catalog-product-uuid>'
npm run test:e2e:phase8
```

The defaults target `https://localhost:51829` and application code `nexa_connect`. Override them with `NEXACONNECT_PHASE8_E2E_BASE_URL` and `NEXACONNECT_PHASE8_E2E_APPLICATION_CODE`. Non-local targets are rejected unless `NEXACONNECT_PHASE8_E2E_ALLOW_REMOTE=1` is explicitly set for a disposable non-production environment. `NEXACONNECT_PHASE8_E2E_PROCESSING_TIMEOUT_MS` changes the default 90-second worker/deletion timeout.

When enablement or any required credential/identifier is absent, the test is reported as skipped without contacting the stack. Failures retain screenshots and the HTML report under ignored `test-results/phase8` and `playwright-report/phase8`; review these default artifacts for usernames, organization/product identifiers, filenames, and error details, and keep them access-restricted until safely deleted or redacted. Trace and video retention are disabled by default because they can capture typed test credentials, session cookies, response data, business identifiers, and presigned object URLs. Enable them only for controlled diagnosis with `NEXACONNECT_PHASE8_E2E_RETAIN_SENSITIVE_ARTIFACTS=1`; restrict access, use short retention, redact before sharing, and never publish them as ordinary public CI artifacts. Git ignore rules prevent accidental source commits but do not secure CI artifact storage.
