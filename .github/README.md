# Cash-close verification workflow

`workflows/cash-close-verification.yml` runs on pull requests and manual dispatch using Ubuntu 24.04, PowerShell and .NET 10. Repository token permissions are read-only, checkout does not persist credentials, and infrastructure credentials are generated rather than loaded from repository secrets. Superseded runs for the same ref are cancelled.

- **Cash-close recovery and alert gate** has a 20-minute timeout. It checks Prometheus rules before the disposable recovery/restricted-replay matrix, requiring both cases without skips. It uploads only recovery `verification.json` and `evidence.json` plus alert `verification.json`.
- **Joined cash-close portal gate** has a 25-minute timeout and adds Node 22 and Chromium. It checks browser guards before the [disposable real-OIDC portal runner](../docs/Deployment/Cash-Close-Portal-Acceptance.md), requiring five browser cases and successful cleanup. It uploads only `.runstate/cash-close-portal/*/verification.json` and `src/Frontend/test-results/cash-close-live/*/summary.json`.

Artifacts include hidden paths and expire after 14 days. Raw TRX, logs, fixture state, credentials, payloads and browser traces are excluded. Verification JSON may record failure; recovery evidence JSON requires successful tests and cleanup. Portal summary success alone does not establish successful launcher cleanup: inspect its matching verification JSON.

Remote execution and branch protection remain unverified. Configure required checks separately after verifying both remote jobs. See [local recovery evidence and exclusions](../docs/Architecture/Evidence/Cash-Close-Recovery-Acceptance.md); joined portal live execution evidence is pending.
