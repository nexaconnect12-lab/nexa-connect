# Cashier-to-day-close live acceptance

Run `npm run test:cashier-day-close:guards` locally. Run the five-scenario live matrix only through [the disposable launcher](../../../../scripts/test-cashier-day-close.ps1) after its dependencies are prepared. The config fails closed on incomplete, remote, colliding or mismatched settings. One worker, no retries, no skips and no traces/screenshots/video are required.

The test drives real POS PKCE sign-in with three separate subjects and a run-owned loopback callback listener, then actual authorized service commands and Customer BFF preparation. Access tokens stay in test-process memory; portal scripts never receive them. The safe reporter writes case names/statuses, allowlisted stage names and bounded run/count summaries. The acceptance clock generates historical records through the real Order/POS command paths; it does not seed transitions or rewrite financial history. See [the scenario matrix and limitations](../../../../docs/Deployment/Cashier-Day-Close-Acceptance.md).

Positive preparation clicks the actual Load/Prepare/Refresh controls; negative, replay and contention checks additionally use browser-session fetches against the real BFF. Restart replay retains both winning operation and actor. Direct cashier API requests carry the run correlation identifier; the BFF retains its normal generated/validated correlation.
