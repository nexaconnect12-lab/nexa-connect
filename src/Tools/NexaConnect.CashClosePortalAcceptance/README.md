# Cash-close portal fixture

The browser caller preserves only this tool's allowlisted stage/type/SQLSTATE summary or a fixed child-timeout category. It never forwards arbitrary child output, credentials or exception bodies. Diagnostic/redaction guards run as part of the local release gate; a failed fixture still fails acceptance without retries.

The launcher assigns generated users the coarse `customer-user` entry role; this tool retains existing scoped cash-review assignments. Portal branch selection uses `cash-close` discovery with `pos.cash-review.read`, so this pinned Authorization 7 fixture needs no day-close grant. Realm entry is not financial authorization. Current outcomes remain in the run's verification receipt.

Standalone disposable fixture tool for the [joined portal runner](../../../scripts/test-cash-close-portal.ps1). It accepts `provision`, `approve`, `late`, or `revoke` only with explicit `NEXACONNECT_CASH_PORTAL_ENABLED=1`, disposable confirmation, a generated run ID, exact loopback run databases, two distinct Keycloak subjects and a run-scoped state path. Use the runner; do not point this tool at existing environments.

Platform, Restaurant and Authorization provisioning uses their Application/Infrastructure implementations. POS repositories create/close the cash session, approve its current versions and project a delayed settlement. Fixture-only Infrastructure bootstraps POS stores and changes the accountant's existing read override to explicit deny. Reporting is never written by this tool: the real POS scanner/outbox and Reporting consumer perform delivery. Repeated or stale approval/settlement commands are rejected.

The CLI is not a service or production operational tool. It logs bounded failure stage/type/SQLSTATE without payloads, credentials or exception bodies. All secrets remain in process environment; its state file contains generated identifiers and an event timestamp. Service child hosts do not inherit fixture/admin environment variables. See [prerequisites, checks, evidence and exclusions](../../../docs/Deployment/Cash-Close-Portal-Acceptance.md).
