# Cash-close acceptance process host

Test-only child host for the production outbox dispatcher or Reporting consumer. It runs only with `NEXACONNECT_ENVIRONMENT=Testing`, `NEXACONNECT_CASH_CLOSE_ACCEPTANCE=1`, and one `consumer` or `dispatcher` argument. It is not deployed with application services.

The guarded runner supplies `NEXACONNECT_CASH_HOST_DB`, broker URI, exchange, queue and ready-file settings. Consumer readiness follows actual binding/consumer registration. Its optional `NEXACONNECT_CASH_COMMIT_BARRIER` marker pauses after the real projection commits but before acknowledgement so the test can terminate that exact child and verify redelivery. The dispatcher ready marker only indicates host startup. Logging is disabled for this bounded fixture; marker files contain no financial payloads or credentials.

Launch through [the recovery runner](../../../docs/Deployment/Cash-Close-Recovery.md#disposable-recovery-acceptance), which manages generated infrastructure and child lifecycle. Implemented tests do not mean live acceptance passed.
