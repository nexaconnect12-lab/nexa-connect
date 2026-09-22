# Omise test webhook reconciliation

Payment accepts test-account charge notifications at `POST /api/payment/v1/webhooks/omise` only when explicitly enabled in Development/Testing. Default behavior is disabled (404). This slice adds durable ingestion and status-only recovery for already-started uncertain authorization, capture and reversal. It does not implement production onboarding, 3DS, PromptPay, refunds or settlement.

## Prerequisites and enablement

Provision Payment migration 8 using the approved migration tool, with application version at least `0.12.0`. Existing Payment migrations, tenant data, checkout roles and RabbitMQ prerequisites still apply. Do not enable the route before provisioning the inbox schema; `/health/ready` includes an inbox-schema check while enabled. The launcher does not apply migrations or seed data.

In your Omise **test** dashboard Webhooks settings, obtain the test webhook secret. This is a base64 HMAC secret, separate from both `skey_test_` and `pkey_test_` API keys. Inject it without printing it in the launcher PowerShell window:

```powershell
$masked = Read-Host 'Omise TEST webhook secret (base64)' -AsSecureString
$env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET = [Net.NetworkCredential]::new('', $masked).Password
$masked.Dispose()
pwsh -NoProfile -File scripts/run-checkout-development.ps1 `
    -EnableOmiseTestCheckout -EnableOmiseTestWebhooks -ValidateOnly
```

Stop the previous launcher before running the same command without `-ValidateOnly`. The launcher enables webhooks and injects their secret only in Payment; it strips `NEXACONNECT_OMISE_*` from child environments and restores parent values on exit. Other services receive disabled webhook configuration and no webhook secret. Both CLI switches are required; manual-tender default startup remains unchanged. The existing Omise API test secret and matching POS test-card configuration are still required.

For independently hosted Payment, use `OmiseWebhooks__Enabled=true` and secret-managed `OmiseWebhooks__Secret`. Startup requires Omise, PostgreSQL, enabled outbox, Development/Testing and a secret decoding to 16–128 bytes. Defaults: 5-second poll, 5-minute event lease, 30-second retry delay and 20 processing attempts. The event lease must cover five configured provider request timeouts plus 30 seconds. Invalid bounds fail startup.

Omise delivery requires an externally reachable HTTPS endpoint with a publicly trusted certificate; `localhost` and a self-signed endpoint are insufficient. A manually controlled test tunnel can target local Payment port 5272 and expose only this route. Configure the complete HTTPS URL in the **test** dashboard. This implementation does not create tunnels, change dashboard settings or certify external delivery. Keep request-body/header logging disabled at the tunnel and reverse proxy. Do not publish API/workload secrets. See [Omise webhook documentation](https://docs.omise.co/api-webhooks/thailand) and [Events API](https://docs.omise.co/events-api/thailand).

## Verification and financial rules

Ingress bounds bodies to 64 KiB, reads them within 10 seconds, and verifies `Omise-Signature` over the exact timestamp, dot and raw body using decoded HMAC-SHA256 key bytes. Timestamp tolerance is five minutes in either direction; signatures use constant-time comparison. The provider's one/two-signature rotation header is supported with the currently configured secret. Configure the new test webhook secret during rotation before retiring the old one. Clock synchronization is required. No signatures, bodies, card data or secrets are persisted.

Only a strict `evnt_test_` ID is enqueued. Other delivered fields are ignored, including financial state and ownership. Durable insertion deduplicates by event ID; repeated completed/rejected/exhausted events are not requeued. Successful durable receipt returns 200; invalid signature returns 401, invalid test ID/JSON returns 400, oversized input returns 413, internal read timeout returns 408 and persistence failure returns 503 without acknowledgement. The route has a per-process global fixed-window limit of 60 requests/minute, no waiting queue, with 429 on rejection. These limits are test defaults, not a distributed production quota design.

The worker independently performs an authenticated GET for the same event from fixed official `https://api.omise.co/events/{id}` with normal TLS, no redirect, no HTTP request logger or automatic retry and a bounded response/body deadline. Only test `charge.create`, `charge.capture`, `charge.complete`, `charge.reverse` and `charge.update` events carrying charge data and valid NexaConnect metadata are supported. Unsupported, malformed, live or missing events are rejected. Temporary transport/authentication/provider failures retry with a bounded delay. Foreign ownership, mismatched Order/reference, non-card currency or unequal amount is rejected before recovery. Event snapshots never decide financial completion.

For an eligible local uncertain operation, the Application recovery adapter acquires the existing expired Payment authorization/capture/void claim and performs the existing current-charge GET reconciliation. Active operation leases defer processing; terminal or unrelated states are acknowledged without mutation. The worker never calls authorize/capture/reverse/refund commands. Existing Order orchestration may resume its own capture after authoritative authorization reconciliation. Existing Payment audit and integration events commit transactionally with financial state; normal outbox dispatch informs Order. Delayed/out-of-order events cannot regress Paid/captured state.

Inbox claims have independent fences and expiry. A crash before inbox acknowledgement is recoverable; if Payment already committed, replay observes its terminal state without a second transition. Exhausted notifications remain `exhausted` evidence and do not change financial state or disable existing polling recovery. There is no unaudited replay/delete API. Review exhaustion through controlled operations; automatic Events API catch-up and retention/purge remain future work. Omise does not guarantee retries for missing deliveries, so existing status polling remains enabled. Destructive migration-8 downgrade refuses pending, processing or exhausted evidence.

## Observability and acceptance

Service name remains `nexaconnect-payment`. The validated incoming correlation string is retained in `trace_correlation_id` and propagated to event/charge reads and worker telemetry. Existing financial events use a nonempty correlation UUID: an incoming UUID is reused, otherwise a UUID is generated as a financial bridge. Both fields are stored durably. The worker exports a bounded `payment.omise_webhook.process` Consumer span from ActivitySource `nexaconnect-payment`, with correlation/trace logging scopes. Automatic outbound HTTP instrumentation is suppressed during event and charge verification so URI references cannot enter exported spans; validated correlation headers still propagate. Safe events record signature rejection, durable receipt, persistence availability and processing outcome; the `payment.omise_webhook.outcomes` counter has bounded outcome tags. Query `{service_name="nexaconnect-payment"}` in Loki and filter for `Omise webhook`. Never add event/charge IDs, bodies, signature headers, keys, proofs or amounts to telemetry.

Local verification covers raw-body signatures, replay windows, rotation, forged/live/malformed input, canonical GET validation, tenant/amount/reference binding, terminal-state protection, ingress persistence failures and rate limiting. Real PostgreSQL tests use generated isolated schemas: deduplication under concurrent insertion/claims, expired claim recovery/stale fence rejection, exhaustion/downgrade protection and a crash after financial/outbox commit before inbox acknowledgement. This is controlled local evidence, not an externally delivered Omise webhook pass.

The guarded live runner is `scripts/test-payment-omise-webhook-live.ps1`. It owns generated disposable PostgreSQL/RabbitMQ resources and an isolated Testing Payment host. Normal `PaymentProvider__AuthorizationRecoveryEnabled` behavior defaults to `true`; this acceptance host alone disables that polling worker so the observed authorization transition is attributable to webhook recovery. A Testing-only boundary pauses only after status-only authorization reconciliation commits and before the inbox acknowledgement. The runner terminates that exact Payment process, restarts it, waits for the one-minute inbox lease to expire, and submits one locally signed replay of the already received event identity. The replay proves durable deduplication; it is not another provider financial command or independent provider redelivery.

Before running it, expose local port 5272 through a temporary publicly trusted HTTPS tunnel restricted to `/api/payment/v1/webhooks/omise`, then register that exact URL in the Omise **test** dashboard. Create one fresh test token and inject all three values without printing them:

```powershell
$key = Read-Host 'Omise TEST secret key' -AsSecureString
$token = Read-Host 'Fresh Omise TEST token' -AsSecureString
$hook = Read-Host 'Omise TEST webhook secret (base64)' -AsSecureString
$env:NEXACONNECT_OMISE_TEST_SECRET_KEY = [Net.NetworkCredential]::new('', $key).Password
$env:NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN = [Net.NetworkCredential]::new('', $token).Password
$env:NEXACONNECT_OMISE_WEBHOOK_TEST_SECRET = [Net.NetworkCredential]::new('', $hook).Password
$key.Dispose(); $token.Dispose(); $hook.Dispose()

pwsh -NoProfile -File scripts/test-payment-omise-webhook-live.ps1 `
  -PublicWebhookUrl 'https://<temporary-test-host>/api/payment/v1/webhooks/omise' `
  -ValidateOnly

pwsh -NoProfile -File scripts/test-payment-omise-webhook-live.ps1 `
  -PublicWebhookUrl 'https://<temporary-test-host>/api/payment/v1/webhooks/omise' `
  -ConfirmDisposableInfrastructure -ConfirmProcessTermination `
  -ConfirmSandboxTransaction -ConfirmDashboardWebhookConfigured
```

Validation performs no infrastructure, provider or financial operation. The full run probes the exact public route before consuming the token, creates one THB 50 test authorization, and never retries a failed or uncertain authorization. If authorization may have reached Omise, inspect the test-account charge before any new run and supply a different fresh token. The test authorization remains in the test account; the runner does not capture, reverse or refund it. It removes its processes, disposable containers, binaries and raw logs, retaining only identifier-free evidence and a summary under `.runstate/payment-omise-webhook-live/<run>/`.

A pass establishes real signed external delivery, authenticated canonical event/current-charge reads, exact local tenant/intent/Order metadata binding, a financial/outbox commit before inbox acknowledgement, exact Payment process termination, expired-lease restart recovery, and duplicate ingress without another financial transition. It does not certify a provider-originated redelivery, a full Order/POS checkout, capture, production networking or production activation. The prior [five-scenario hosted recovery pass](../Architecture/Evidence/Omise-Five-Scenario-Recovery-Acceptance.md) remains the Order/Payment workflow evidence. External webhook delivery acceptance remains pending until this runner completes successfully in the configured test account.

## Local regression verification

Start the existing local Compose PostgreSQL service and restore solution dependencies, then run:

```powershell
pwsh -NoProfile -File scripts/test-payment-omise-webhook-local.ps1
```

The runner requires local Docker Desktop, reads the local database credential into a hidden child environment rather than command arguments, strips inherited Omise credentials, restores parent values, and uses generated isolated PostgreSQL schemas that tests clean up. It requires all 134 unit, 9 HTTP and 4 real PostgreSQL cases to execute without skips. It sends no real provider requests or financial commands and does not change product records. Inspect its safe `.runstate/payment-omise-webhook-local/<run>/summary.json`. Run `f5ba45fe4bb74dbf88ec7178a127f199`, completed at `2026-09-21T23:55:12.1784211+00:00`, passed the current 134/9/4 matrix. Lease-expiry recovery is simulated; process termination and external signed webhook delivery remain unverified by that local run. This regression does not replace external test-dashboard delivery acceptance.
