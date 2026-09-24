# Cash-close verification workflow

`workflows/cash-close-verification.yml` runs on pull requests and manual dispatch using Ubuntu 24.04, PowerShell and .NET 10. It runs the Prometheus rule checks before the disposable recovery/restricted-replay matrix, which must pass both cases without skips. Repository token permissions are read-only, checkout does not persist credentials, and test infrastructure credentials are generated rather than loaded from repository secrets.

The job has a 20-minute timeout and cancels superseded runs for the same ref. Only `.runstate/cash-close/**/verification.json`, its `evidence.json`, and alert `verification.json` are uploaded, including hidden paths, for 14 days. Raw TRX, logs, marker directories and payloads are excluded. Verification JSON may record failure; recovery evidence JSON requires successful tests and cleanup.

This workflow is authored but has not executed remotely. Branch protection must be configured separately to require **Cash-close recovery and alert gate** after verifying the remote job. See [local evidence and exclusions](../docs/Architecture/Evidence/Cash-Close-Recovery-Acceptance.md).
