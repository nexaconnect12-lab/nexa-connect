# Local draft identity/session smoke

Slice 6 adds manager-resumed mode, run by checkout with -VerifyReleaseRecovery after its quiesced isolated database/configuration restore drill. Fresh PKCE reads retained receipts/review and replays the original settlement after original-stack resumption. Clone application startup and Windows/PDF behavior are not claimed. See [release guide](../../../../docs/Deployment/First-Draft-Release.md).

Slice 5 adds `test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure -VerifyManagerFlow`.
After real checkout and recovery, manager mode verifies mixed cash/PromptPay sales,
cash-only drawer attribution, current-day closing rejection, version-fenced close and
variance review, accountant read-only access and unchanged sale receipts. The separate
two-stage manager launcher also requires eighteen historical Singapore cutoff/settlement/
CSV cases and cleanup; it never changes clocks in this normal draft stack. See the
[manager guide](../../../../docs/Deployment/First-Draft-Manager.md).

Slice 4 extends joined checkout with invalid-terminal/wrong-cashier rejection and workload-only admission checks. `test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure -VerifyTerminalPackage` additionally builds, installs, repeats install and verifies an owned Windows terminal package without launching the UI or registering its callback. Existing user recovery is read-only during preflight; unrelated scope correctly blocks installation. See [Windows guide](../../../../docs/Deployment/First-Draft-Windows-POS.md).

checkout.mjs and draft-auth.mjs run through scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure. They use real POS PKCE/customer roles, owning checkout/Kitchen commands, normal clocks and the isolated stack. Tokens stay in memory; the private command journal enables exact retries. See [coverage and recovery boundaries](../../../../docs/Deployment/First-Draft-Checkout.md).

The optional `-VerifyTerminalPackage` check requires compatible or empty protected recovery
in the executing Windows profile. A retained different terminal scope must block it; run
backend acceptance separately and use an approved separate test profile for package checks.
Isolated-profile installation does not certify the user's interactive WPF/PDF workflow.

`provisioning.mjs` runs only through `scripts/test-first-draft-provisioning.ps1 -ConfirmTestResources`.
It verifies the generated loopback issuer, real POS PKCE/BFF OIDC identities, live role/tenant
boundaries, branch/station selectors and preserved menu/stock/revocation after re-provisioning.
The launcher resets this disposable stack and writes a bounded receipt. It seeds no sales,
shifts or financial transitions and stores no browser tokens. See [slice 2 setup](../../../../docs/Deployment/First-Draft-Provisioning.md).

Run only through `scripts/test-first-draft-stack.ps1 -ConfirmDisposableInfrastructure`. The launcher validates its owned generated project, passes the synthetic customer-viewer password through process environment and performs real Keycloak/BFF login. The script verifies the authenticated subject, saves a private opaque-cookie storage state, and reuses it after BFF and full-stack restart. It records only boolean outcomes and disables screenshots/traces/video by using the direct Playwright API without capture.

This proves configured Redis ticket persistence with the retained BFF key ring; it does not provision branch/product access or exercise checkout. Credentials/storage state stay under the restricted Windows run directory and are excluded from shareable artifacts. Verified cleanup removes storage state; cleanup failures preserve private state and record a failed bounded receipt. See [the runbook](../../../../docs/Deployment/First-Draft-Test-Stack.md).
