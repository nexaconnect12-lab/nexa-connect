# POS cashier-sale acceptance evidence

## Automated backend gate

On 2026-09-29, `scripts/test-pos-cashier-sale.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` completed against a uniquely named disposable PostgreSQL 17 and RabbitMQ 4 Compose project.

The run passed:

- 17 protected SQLite and payment-recovery tests plus 3 live PostgreSQL/RabbitMQ POS projection tests;
- 3 Order receipt cases covering PostgreSQL immutability/concurrency, HTTP tenant/branch non-disclosure and live `order.read` revocation, and the migration runner; and
- 13 isolated financial, receipt, projection, and close-state verifier fixtures plus 2 early guards.

All 38 checks executed with zero skips. The launcher verified removal of the generated containers, network, and volumes and restored its process environment. Sanitized machine evidence is retained at `.runstate/pos-cashier-sale/1de33ded9d574abf960d101d2da2eb31/verification.json`. The source tree was dirty because this acceptance implementation was under review; the evidence records revision `96466b3bb1237a8a3634e3a3f9d5fc4387d80d68` and `sourceDirty=true`.

## Attestation boundary

This automated result proves the protected client recovery cases, disposable database/broker integration, receipt persistence, authorization and migration behavior, projection behavior, and the live verifier's rejection logic. It does not authenticate a cashier, drive WPF, open or validate the Windows print dialog, inspect physical printer output, or certify production infrastructure.

The release record must therefore also include a fresh operator run of `scripts/verify-pos-cashier-live-acceptance.ps1` with all explicit OIDC, WPF checkout, receipt preview, receipt reprint, receipt-failure isolation, and sign-out confirmations. The verifier independently checks the persisted Order receipt, single cash settlement, POS cash projection, closed cash/shift lifecycle, and cleared local recovery state before writing sanitized evidence.
