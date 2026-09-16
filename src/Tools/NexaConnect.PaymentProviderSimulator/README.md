# Local HTTPS Payment provider simulator

Acceptance-only, in-memory authorization/capture/status fixture for the GenericHttp adapter. It performs no financial transaction and is not a deployable Payment provider. Commands preserve stable references by Payment intent; changed authorization replay returns 409, capture requires the matching authorization, amount/currency and exact Idempotency-Key, and invalid credentials return 401. State survives Payment restarts while this simulator remains running, but is lost when the simulator stops. Void/refunds, rate limits and provider-specific behavior are outside this fixture.

From PowerShell 7 on Windows with .NET 10 and a healthy Docker Linux engine:

```powershell
./scripts/test-order-provider-recovery-local.ps1 -ConfirmDisposableInfrastructure -ConfirmProcessTermination
```

No external provider settings are required. The wrapper generates a random loopback HTTPS port, bearer credential and two-hour self-signed certificate, runs the simulator smoke checks, then delegates the hosted Order/Payment interruption matrix to the existing guarded runner. Local defaults are 1.00 THB/card. Existing external-provider environment values are restored afterward. The wrapper stops its exact simulator process and removes the PFX private-key file in finally; normal process interruption is covered, but forced process-tree termination of the wrapper can bypass cleanup. Restrict any failed run directory until inspected. Summary evidence under `.runstate/order-provider-recovery-local/<run-id>/` explicitly labels externalProviderVerified=false; hosted detail evidence remains under `.runstate/order-provider-recovery-live/<run-id>/`.

Use `-SmokeTestOnly` to verify HTTPS, credential rejection, authorization conflict/replay and capture replay/status without Docker. Smoke checks passed on 2026-09-16. The full hosted matrix remains pending because Docker Desktop's Linux engine was unavailable during execution.

The final wrapper never changes Windows certificate stores. Its HTTPS client pins the generated certificate's SHA256 fingerprint while rejecting hostname errors. Payment and the arm-stage adapter accept that pin only for Testing and `https://127.0.0.1`; ordinary provider clients retain platform TLS validation. The internal setting `PaymentProvider__SimulatorCertificateSha256` must never be configured in a deployed environment. The wrapper passes the pin through `NEXACONNECT_PAYMENT_PROVIDER_SIMULATOR_CERT_SHA256`; this is a public fingerprint, not a secret. Production/custom remote pins are rejected. Shared structured JSON/optional OTLP observability uses service name `nexaconnect-payment-provider-simulator`, including safe 401/409 boundary events and correlation-aware request logs. Query `{service_name="nexaconnect-payment-provider-simulator"}` when exported to Loki; never retain bodies, credentials or provider references. Production Order/Payment hosts retain their shared correlated observability.
