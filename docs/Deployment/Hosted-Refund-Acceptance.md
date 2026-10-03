# Hosted refund workflow acceptance

This gate exercises customer refund HTTP requests through real Keycloak, Platform Directory membership, Restaurant/Order ownership and Authorization financial decisions. Payment owns reservation, provider execution, recovery, receipt, audit and outbox transactions. Reporting consumes the real sale/refund messages. The gate reuses the disposable [financial infrastructure](../../docker/financial-portal/README.md), with six independently owned service databases and a separate identity database. No production schema or API is added. The migration runner now grants Payment SELECT on the version column only, allowing its readiness probe without exposing checksums or history writes.

## Local simulator gate

Use PowerShell 7, .NET 10 and a healthy local Docker Linux engine. The launcher generates loopback ports, realm, users, credentials and an expiring HTTPS simulator certificate; it never changes certificate stores.

```powershell
./scripts/test-refund-hosted.ps1 -ConfirmDisposableInfrastructure -ConfirmProcessTermination
```

The confirmations permit fixture writes, generated infrastructure and exact owned Payment process termination/restart. Optional `-NoBuild` requires current Debug assemblies. The tool and host paths are repository-fixed. Guards require exact generated database names, loopback services and a run-owned state file. `scripts/test-refund-hosted-guards.ps1` verifies three launcher and five tool rejection boundaries before external access.

Fixture preparation uses owning Application/Infrastructure repositories to provision two organizations, one restaurant/two branches, memberships and roles. A branch accountant receives an explicit refund-create permission override to test branch permission independently of financial limits; store-manager remains restaurant-scoped. Test-only Infrastructure provisions subject/action/currency approval limits because no management API exists for them. The provider adapter authorizes/captures five fresh THB 100 intents against the simulator. Owning Order persistence creates Paid receipt-backed sales. This setup does not exercise checkout HTTP, Inventory or Kitchen fulfillment.

Six real Authorization persistence checks must pass. Seventeen hosted checks then cover:

| Boundary | Required result |
| --- | --- |
| Runtime migration privileges | Payment reads version only; checksums and history mutation denied |
| Permission without financial limit | Create denied |
| Amount above manager limit | Create denied |
| Branch and foreign-tenant ownership | Create denied and reads undisclosed |
| Partial refund | THB 25 immutable receipt |
| Identical operation replay | Same refund; one simulator provider POST |
| Changed operation payload | Conflict |
| Remaining full refund | Cumulative THB 100; another refund conflicts |
| Two concurrent THB 60 requests | One succeeds, one conflicts |
| Provider commits and drops response | Refund becomes uncertain; Payment restart and status-only worker recover original operation without another POST |
| Definitive provider failure | Reservation released for a new operation |
| Exhausted unknown provider status | Review required; amount remains reserved |
| Completed evidence | Update/delete rejected; Order remains Paid with receipt, completion audits and retained originals agree |
| Financial propagation | Real broker consumers produce branch-scoped refunds and net sales |
| Existing-session permission/membership revocation | New create or receipt access denied |

The test-only simulator exposes authenticated loopback `/acceptance/refunds/{id}` fault/diagnostic endpoints. They are not Payment product routes. Its state survives Payment restart but not simulator restart. A run-owned durable evidence queue bound to `#` permits mandatory audit/lifecycle publication alongside the actual Reporting queues. No financial repair or replay writes are made by a report read.

## Separate Omise test-account gate

Supply secret-managed process environment values `NEXACONNECT_REFUND_OMISE_SECRET` (test secret only) and three fresh single-use test tokens `NEXACONNECT_REFUND_OMISE_TOKEN_0` through `_2`. Use the existing [test-token helper and provider guidance](Omise-Test-Account-Acceptance.md). Never put credentials or tokens in files, command arguments or evidence. The launcher rejects missing credentials before infrastructure/fixture writes.

```powershell
./scripts/test-refund-hosted.ps1 -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmOmiseTest
```

This mode creates three THB 100 test-account captures and runs fifteen checks through the actual Omise adapter. It deliberately discards an initiating completed-refund response, restarts Payment and resolves the existing operation through lookup. It does not simulate an Omise provider response interruption or exhausted provider uncertainty: those two stronger fault cases belong to the simulator gate. One-command/status-call diagnostics are independently proved only against the simulator. Tokens enter only the fixture authorization process; HTTP hosts receive no card tokens, and only Payment receives the explicitly configured provider secret. Omise is fixed to its official HTTPS API with normal TLS validation. No production credentials or live funds are supported. Successful external execution is a separate evidence requirement, never inferred from local simulator results.

## Evidence, cleanup and operations

`.runstate/refund-hosted/<run-id>/matrix.json` records unique passing case names/counts and the provider boundary; `verification.json` records revision/dirty state, completion UTC, Authorization pass, overall pass, cleanup and `productionVerified=false`. Every case must pass; there are no matrix skips/retries. The [CI gate](../../.github/workflows/refund-hosted-verification.yml) runs simulator mode and uploads only these two bounded JSON files for fourteen days. It never uploads tokens, fixtures, logs, TRX, certificates or databases.

The fixture owns the exact Payment child process and waits for termination before restart/cleanup. The launcher stops its recorded hosts, removes only its generated Compose project/volumes and certificate export, verifies no containers remain and restores changed environment settings. Abrupt termination of the launcher/process tree can bypass finally cleanup; inspect only that run's project and restrict its private local logs before manual recovery. Failed matrix runs never produce a passing overall verification. Local logs remain ignored and are not release evidence.

Production HTTP hosts retain shared structured JSON/optional OTLP telemetry and validated correlation propagation. Simulator service name is `nexaconnect-payment-provider-simulator`; Payment is `nexaconnect-payment`; Reporting is `nexaconnect-reporting`. Filter those service names and a validated correlation ID when debugging; log only bounded failure categories/local identities, never payment/provider bodies, tokens, credentials or headers. The simulator fault endpoints inherit the same safe request/boundary logging.

This gate excludes BFF/browser OIDC/PKCE, WPF/POS/physical printers, actual checkout orchestration, manual cash/PromptPay refunds, return/restock allocation, fiscal credit notes, target-production deployment and end-of-day settlement certification. Its disposable identity client supports password grants only inside the generated realm and does not change any production client. See [retained execution evidence](../Architecture/Evidence/Hosted-Refund-Acceptance.md).

## Existing Payment deployment correction

New migration executions apply the version-column-only grant after revoking all other runtime history privileges. A confirmed migration with no pending steps returns without privilege reconciliation. For an already-current Payment database, the migration owner must explicitly apply the following reviewed grant before checking readiness:

```sql
GRANT SELECT (version) ON TABLE public.nexaconnect_schema_migrations TO nexaconnect_payment_app;
```

Do not grant full-table SELECT or any history mutation rights. This correction is necessary because Payment's existing readiness query reads `max(version)`. The hosted matrix proves both readiness and denied checksum/update access using the actual runtime identity. Other services retain migration-history isolation. No production database is changed by running the disposable gate.
The joined gate also corrected the Payment workload client's missing explicit nexaconnect-api audience mapper. Persisted realms require an explicit reviewed mapper update before refunds can call authenticated Order/Restaurant ownership endpoints. No endpoint policy or product permission was widened; realm validation now requires this mapper.
