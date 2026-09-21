# Omise hosted recovery acceptance

The direct Omise test-account gate [passed](../Architecture/Evidence/Omise-Test-Account-Acceptance.md). The four-scenario hosted Omise matrix passed on 2026-09-17, including cleanup; pre-authorization interruption remains excluded. It reuses the disposable PostgreSQL 17/RabbitMQ 4 topology with real Order and Payment hosts, fixed official Omise HTTPS, normal TLS, no redirects and no automatic provider POST retries.


See [retained four-scenario hosted Omise evidence](../Architecture/Evidence/Omise-Hosted-Recovery-Acceptance.md) for the exact pass and exclusions.

## Scope and prerequisites

Use Windows, PowerShell 7, .NET 10, a healthy local Docker Linux engine and restored project dependencies. This runner creates four independent test authorizations, captures two and reverses two. At the default amount each charge is 50 THB; all keys and four fresh, distinct success-card tokens must belong to the same Omise test account. No live keys are accepted. See [token preparation](Omise-Test-Account-Acceptance.md#run-the-real-sandbox-gate).

The four scenarios interrupt the acceptance child process after a confirmed authorization response before its charge reference is committed, a confirmed capture response before local completion, and confirmed pre-capture reversal responses for an unpaid Order and a deliberately already-paid Order fixture. For each scenario, real hosted recovery commits terminal state and an unpublished transactional-outbox event while RabbitMQ is stopped. The launcher terminates that exact Payment host, restarts RabbitMQ and Payment, and verifies persistent event delivery and Order inbox completion. Reversal scenarios also verify duplicate delivery and single compensation; the paid fixture must remain Paid. The fixture models a delayed event defensively, not reversal of a captured charge.

The default four-scenario matrix and its historical pass exclude pre-authorization `intent_created`. The new opt-in fifth scenario below uses fresh transient test-token handoff; recovery must never acquire a globally configured reusable token. Production browser card collection remains planned. Tokens go only to the acceptance arm's initial authorization. Real Order and Payment hosts inherit no token environment settings; only Payment receives the explicit test secret. No tokens, keys, provider bodies or references enter the sanitized summary. Disposable databases necessarily contain internal provider references until cleanup.

The acceptance repository and Payment host use a 70-second lease and 15-second request timeout, satisfying the same `4 × timeout + 10 seconds` startup guard as the adapter. Hosted reconciliation waits up to 150 seconds; the launcher waits up to 180 seconds for its outbox marker. No service guard is bypassed or schema changed. Workload authentication uses the harness's local signed-token fixture, not live OIDC acceptance. Tenant and financial state validation remain in the existing services.

## Run step by step

1. Inspect earlier test-account charges and resolve abandoned authorizations separately. Do not reuse tokens from any earlier run.
2. Start the [local HTTPS token helper](../../src/Tools/NexaConnect.OmiseTokenHelper/README.md), using the same account's test public key. Generate a new token for each masked prompt below.
3. In a PowerShell 7 terminal at the repository root, inject the test secret and four tokens without echoing them:

   ```powershell
   $names = @(
       'NEXACONNECT_OMISE_TEST_SECRET_KEY',
       'NEXACONNECT_OMISE_AUTHORIZATION_RESPONSE_TEST_TOKEN',
       'NEXACONNECT_OMISE_CAPTURE_RESPONSE_TEST_TOKEN',
       'NEXACONNECT_OMISE_VOID_RESPONSE_TEST_TOKEN',
       'NEXACONNECT_OMISE_VOID_PAID_PROTECTION_TEST_TOKEN'
   )
   foreach ($name in $names) {
       $masked = Read-Host $name -AsSecureString
       [Environment]::SetEnvironmentVariable($name, [Net.NetworkCredential]::new('', $masked).Password, 'Process')
       $masked.Dispose()
   }
   ```

4. Run once in that terminal:

   ```powershell
   pwsh -NoProfile -File scripts/test-order-provider-recovery-live.ps1 `
       -Adapter Omise -OmiseAmount 50.00 `
       -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmSandboxTransactions
   ```

5. Require the success message and inspect `.runstate/order-omise-recovery-live/<run-id>/summary.json`: provider Omise, scenarioCount 4, matrixPassed/cleanupPassed true, four provider-boundary and four Payment-host interruptions, all recovery/outbox/void/paid-protection checks true. `intentCreatedBeforeAuthorizationVerified` remains false. Detailed sanitized evidence is `provider-recovery-evidence.json`. Successful logs are removed; failures retain test diagnostics. Do not share unrestricted diagnostic logs.
6. Clear the five process environment settings, stop the token helper, close its page and clear the clipboard:

   ```powershell
   foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
   ```

If the build fails, no charge has been created by the acceptance stages. If an arm or hosted stage fails, do not retry uncertain operations or rerun with the same tokens. Inspect test-account charges; the runner cleans up only its generated local infrastructure, not remote charges. Resolve abandoned Pending authorizations separately. A new independent run needs four unused tokens. Metadata search can be eventually consistent; ambiguity remains unknown and bounded reconciliation can require review.

The GenericHttp simulator regression matrix provides local harness evidence only. Neither that pass nor the direct Omise pass certifies this hosted gate. This gate counts durable local command starts and instrumented arm calls; it does not independently count all hosted provider HTTP requests. POS token handoff, real OIDC, provider webhooks, key rotation, 3DS, PromptPay, refunds, partial capture, settlement, production release acceptance and partial-compensation process loss remain separate work.

## Observability

The harness reuses `nexaconnect-order` and `nexaconnect-payment` shared structured telemetry and validated correlation propagation. Use `{service_name="nexaconnect-payment"}` or `{service_name="nexaconnect-order"}` in Loki and correlate by the validated correlation identifier in a controlled debugging session. Provider failure events contain only method and bounded category; never add token, key, charge reference, proof, amount or provider payload to telemetry. Child service output is drained without retaining raw service logs. Sanitized evidence records no operational identifiers.

## Implementation verification

On 2026-09-17, the integration project built with zero warnings/errors; ten discovery-guard cases and one Omise HTTP integration case passed. `tests/Scripts/Test-OmiseHostedRecoveryPreflight.ps1` passed seven rejection cases before any infrastructure/provider access. The complete five-scenario GenericHttp local simulator regression passed with five provider-boundary interruptions, five Payment-host interruptions and successful cleanup. Its retained summary is `.runstate/order-provider-recovery-live/5f4804a07a63415fbcdab2b44e4cc0f7/summary.json`; the local wrapper summary is `.runstate/order-provider-recovery-local/e9cd78af63384b27ac99848dc1ef5cb7/summary.json`. Documentation audit and diff checks passed. No Omise financial requests were made during implementation; the subsequent four-scenario credentialed Omise hosted gate passed on 2026-09-17.

## Opt-in fifth token-handoff scenario

If `intent_created` exits before `outbox_persisted`, inspect its restricted `intent_created-hosted.out.log` first. The initial five-scenario run on 2026-09-18 failed the fresh-token HTTP assertion. Code inspection identified a fixture defect: it copied `IdempotencyKey` from a reloaded aggregate, which does not hydrate transport idempotency records, producing a missing required request key. The earlier failure log did not record the HTTP status; no historical status or provider outcome is inferred from it. The corrected fixture reuses the original seeded key, verifies it resolves to the same durable Order before submitting a token, and reports only the numeric HTTP status on a rejected handoff. It never prints response bodies or tokens. One HTTP-request serialization regression and twelve acceptance guards passed locally; no credentialed retry was performed. Inspect test-account charges before an independent fresh-token run; cleanup does not resolve remote financial state. The subsequent five-scenario acceptance passed on 2026-09-18; this failure remains historical evidence.

Before starting a financial run, inject the test secret and all five distinct fresh tokens using the existing masked-input procedure. Validate the configuration without confirmation switches:

```powershell
pwsh -NoProfile -File scripts/test-order-provider-recovery-live.ps1 `
    -Adapter Omise -IncludeCardTokenHandoff -OmiseAmount 50.00 -ValidateOnly
```

This returns a safe JSON configuration report and exits before Docker discovery, builds, run-directory creation, environment mutation, provider calls or process termination. It validates amount, strict test-key/token syntax and distinct values. It cannot establish that tokens are unused or belong to one account, that Docker is healthy, or that dependencies are restored. No acceptance pass is recorded. The execution path revalidates settings and still requires all three confirmation switches; `-ValidateOnly` cannot authorize a financial run.

After successful validation and reviewing the transaction scope, execute once:

```powershell
pwsh -NoProfile -File scripts/test-order-provider-recovery-live.ps1 `
    -Adapter Omise -IncludeCardTokenHandoff -OmiseAmount 50.00 `
    -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmSandboxTransactions
```

This five-scenario run creates five authorizations, captures three and reverses two. Failure during uncertain financial operations requires read-only inspection and fresh tokens for a new independent run; never reuse a consumed token to retry an uncertain command. Configuration-only validation consumes no tokens. Strict validation rejects trailing newline characters in secrets/tokens; inject only the copied value.

Local script verification passed ten execution rejection cases and nine validation-only cases, including four/five-token success and missing, duplicate and newline rejection, with no infrastructure/provider access. The [POS card checkout pass](../Architecture/Evidence/Omise-POS-Card-Acceptance.md) is recorded separately and does not satisfy this interruption gate.

The existing four-scenario Omise mode remains the default and its retained pass remains unchanged. To include `intent_created`, inject a fifth distinct unused token using the masked prompt into `NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN`, then add `-IncludeCardTokenHandoff` to the same guarded `-Adapter Omise` command. All five tokens and the test secret must belong to the same account; this switch is rejected for GenericHttp. See [handoff setup](Omise-POS-Card-Token-Handoff.md).

The fifth scenario terminates before authorization, allows the real Order worker to bind the original pending Payment intent with zero authorization starts and no token, then submits a fresh token through real tenant-authorized Order and workload-authorized Payment HTTP using the same Order scope, lines and retry identity. It verifies Paid/captured and durable starts/outbox. This scenario does not claim an Order inbox reconciliation event: no uncertain provider operation had started. Background recovery never receives a persisted token. A passing opt-in run requires five scenarios and `cardTokenHandoffVerified=true`/`intentCreatedBeforeAuthorizationVerified=true`; the credentialed five-scenario pass was recorded on 2026-09-18. The separate local POS verifier passed on 2026-09-18 with operator-attested UI/OIDC steps; it does not certify this interruption boundary. The historical four-scenario evidence remains an exclusion, not a fifth-scenario pass.

The [five-scenario Omise gate passed on 2026-09-18](../Architecture/Evidence/Omise-Five-Scenario-Recovery-Acceptance.md), including fresh-token pre-authorization handoff. Historical four-scenario records retain their excluded boundary. This fixture disables webhook configuration and secret inheritance; its hosted recovery pass does not certify migration-8 externally delivered signed notifications.
