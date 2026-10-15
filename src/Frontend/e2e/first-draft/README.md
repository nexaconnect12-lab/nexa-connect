# Local draft identity/session smoke

checkout.mjs and draft-auth.mjs run through scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure. They use real POS PKCE/customer roles, owning checkout/Kitchen commands, normal clocks and the isolated stack. Tokens stay in memory; the private command journal enables exact retries. See [coverage and recovery boundaries](../../../../docs/Deployment/First-Draft-Checkout.md).

`provisioning.mjs` runs only through `scripts/test-first-draft-provisioning.ps1 -ConfirmTestResources`.
It verifies the generated loopback issuer, real POS PKCE/BFF OIDC identities, live role/tenant
boundaries, branch/station selectors and preserved menu/stock/revocation after re-provisioning.
The launcher resets this disposable stack and writes a bounded receipt. It seeds no sales,
shifts or financial transitions and stores no browser tokens. See [slice 2 setup](../../../../docs/Deployment/First-Draft-Provisioning.md).

Run only through `scripts/test-first-draft-stack.ps1 -ConfirmDisposableInfrastructure`. The launcher validates its owned generated project, passes the synthetic customer-viewer password through process environment and performs real Keycloak/BFF login. The script verifies the authenticated subject, saves a private opaque-cookie storage state, and reuses it after BFF and full-stack restart. It records only boolean outcomes and disables screenshots/traces/video by using the direct Playwright API without capture.

This proves configured Redis ticket persistence with the retained BFF key ring; it does not provision branch/product access or exercise checkout. Credentials/storage state stay under the restricted Windows run directory and are excluded from shareable artifacts. Verified cleanup removes storage state; cleanup failures preserve private state and record a failed bounded receipt. See [the runbook](../../../../docs/Deployment/First-Draft-Test-Stack.md).
