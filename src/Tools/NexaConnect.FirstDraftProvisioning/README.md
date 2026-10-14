# First-draft provisioning

Reference-only Development CLI for the owned Windows draft stack. Invoke through
[`provision-first-draft.ps1`](../../../scripts/provision-first-draft.ps1), never against
production or another acceptance stack. The wrapper supplies DPAPI secrets as transient
environment values, verifies ownership and holds the lifecycle lock.

The tool orchestrates each service's owning Application/Infrastructure operations with
its least-privilege runtime database login. Only its POS and Authorization adapters add
reference store/terminal and limit rows; SQL remains with those owners. No financial
transitions or shared domain entities are used. Build with `dotnet build -c Release`;
the wrapper creates a separate CLI build artifact. It does not load copied HTTP-host
appsettings. No generic import, production clock override or public HTTP route exists.

See [setup, failure recovery, permissions and verification](../../../docs/Deployment/First-Draft-Provisioning.md).
Completed re-provisioning preserves identities, prices, stock and access revocation.
JSON failure metadata is bounded to service name, stage, exception category and SQLSTATE.
