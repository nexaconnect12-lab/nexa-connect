# Local first-draft infrastructure

Compose binds generated retained loopback ports from NEXACONNECT_DRAFT_POSTGRES_PORT,
NEXACONNECT_DRAFT_KEYCLOAK_PORT, NEXACONNECT_DRAFT_RABBITMQ_PORT and NEXACONNECT_DRAFT_REDIS_PORT.
The lifecycle supplies these transient variables from its private state; do not put them
in the repository .env. Explicit bindings prevent Docker Desktop stop/start from changing
ephemeral addresses behind connected services. All application/infrastructure ports are
distinct; collisions fail startup rather than adopting an existing listener.

After stack Ready, use [slice 2 provisioning](../../docs/Deployment/First-Draft-Provisioning.md) for stable THB / Asia/Singapore scope, menu/stock and scoped identities. It uses the same lifecycle lock and current-user protected secrets, and does not reset financial/stock history on completed reruns.

Use [the draft lifecycle](../../docs/Deployment/First-Draft-Test-Stack.md), not `docker compose up` directly. `scripts/first-draft-stack.ps1` supplies a generated `nexa-draft-<uuid>` project and secrets; it never imports the root `.env` or adopts existing application databases.

The topology reuses the guarded cashier PostgreSQL initialization and Keycloak realm template. PostgreSQL application and identity data, RabbitMQ and Redis use project-owned named volumes and survive Stop/Start. All published ports bind to IPv4 loopback. Container labels and exact generated project identity guard cleanup. Reset removes only this project's containers/network/volumes after ownership checks.

The copied initialization creates ten independently owned service databases with restricted runtime roles and one migration role. Runtime roles cannot connect to other service databases or create schema objects. After migrations the launcher removes migration-history table privileges and grants only version-column reads. Existing service migrations/triggers retain their financial/audit protections. The test runtime passwords are generated locally; this is not a production secret/credential topology.

RabbitMQ probes execute as the broker user (`gosu rabbitmq rabbitmq-diagnostics`), preventing a root health probe from creating a root-owned Erlang cookie while the broker starts. A controlled ordering test is available through `scripts/test-rabbitmq-health-ownership.ps1 -ConfirmDisposableInfrastructure`; its bounded receipt excludes cookies and raw logs. The shared financial/end-of-day/cashier acceptance definitions use the same fix. A remote Ubuntu rerun remains a separate verification gate.

Redis requires a generated password, persists append-only data and backs the explicitly configured Customer BFF session cache. Development still uses memory when no session connection is configured. The run's BFF key ring persists separately under its published content root. Only the Windows owner and SYSTEM can access the run directory; secrets are current-user DPAPI encrypted. Do not share run directories or raw logs. See the runbook for safe evidence and reset behavior.

Requires Docker Compose supporting `extends` and `!reset` (verified with the local Docker Desktop installation), PowerShell 7.4+, Windows DPAPI, .NET 10 and the documented frontend runtime. This local-only setup does not provide WAN/offline operation, production TLS/backup/alert certification or physical printer acceptance.
