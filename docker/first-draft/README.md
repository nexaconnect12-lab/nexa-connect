# Local first-draft infrastructure

The optional branch PromptPay profile reuses existing Media/storage/scanner services without Compose topology, schema or secret changes. Activate through the stopped draft stack with -EnableBranchPromptPayQr after Media is enabled; preserve retained state and recovery. See [operator guide](../../docs/Deployment/POS-Branch-PromptPay-QR.md).

Optional local Omise sandbox selection reuses these owned databases/broker and adds no
container/port or public provider endpoint. The lifecycle passes the DPAPI-retained test
credential only to Payment and enables Order's existing card gate while keeping the POS
manual default. Defaults and webhooks remain disabled. See [activation and tests](../../docs/Deployment/POS-Mixed-Omise-Sandbox.md).

`-EnableMedia` selects the separate `media.compose.yaml` overlay and persists Media startup/inventory. Storage/scanner stay on generated loopback ports with project-owned volumes, exact staff-origin CORS and separate bucket-scoped application keys. Default topology is unchanged. See [setup and acceptance](../../docs/Deployment/First-Draft-Media.md).

Build-enabled Start publishes that profile's generated Media URL as POS `Services:MediaApi`
in both saved and published client settings. Default/off profiles keep it empty. Generated
inventory validation rejects a missing enabled endpoint or non-loopback URL. Install the
matching refreshed POS package; Compose startup alone does not update an installed client.

QR-profile publication also sets local-only `Pos:UseIsolatedLocalPilotStorage=true` for the
Windows client. Its scope-derived current-user recovery/token directory is separate from
the earlier terminal's default data. This is a client setting; Compose ports, databases,
volumes and financial ownership are unchanged. See [Windows setup](../../docs/Deployment/First-Draft-Windows-POS.md).

QR7 host tooling optionally selects a fresh fixed-origin QR profile; Compose infrastructure remains loopback, owned and isolated. No container port is exposed to phones. The separate private guest HTTPS edge is described in [QR7](../../docs/Deployment/QR7-Pilot.md).

The [Slice 6 restore drill](../../docs/Deployment/First-Draft-Release.md) quiesces a disposable draft, backs up ten service databases plus Keycloak and restores into separately label-owned, network-isolated PostgreSQL clones using exact source image IDs. Compose topology and schema are unchanged; original broker/Redis volumes stay retained. Private backups include credentials/financial data and must not enter shared artifacts.

Compose binds generated retained loopback ports from NEXACONNECT_DRAFT_POSTGRES_PORT,
NEXACONNECT_DRAFT_KEYCLOAK_PORT, NEXACONNECT_DRAFT_RABBITMQ_PORT and NEXACONNECT_DRAFT_REDIS_PORT.
The optional overlay additionally receives NEXACONNECT_DRAFT_MINIO_PORT,
NEXACONNECT_DRAFT_CLAMAV_PORT and the exact NEXACONNECT_DRAFT_MEDIA_ORIGIN. Its
NEXACONNECT_JOINED_MEDIA_* provisioning/application credentials are generated and retained
through current-user DPAPI, not configured in a repository environment file.
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
