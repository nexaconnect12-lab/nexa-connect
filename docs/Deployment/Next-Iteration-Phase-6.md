# Phase 6 — dedicated-server pilot hosting

QR2 source adds guest menu/cart previews separately. Current generated business edges
hide direct `/guest/` capability paths alongside `/internal/`; use the Customer BFF origin.
Pilot templates/validation still require QR flags disabled, so the isolated QR2 settings
do not constitute enabled hosted deployment. See [QR2 rollout limits](QR2-Menu.md).

## Delivery and deployment status

23 October 2026: application artifact tooling, structural configuration guards, HTTPS
application topology and operating procedures are implemented. **The dedicated-server
pilot is not deployed or certified ready.** E01–E04 in the
[Phase 0 baseline](../Planning/Next-Iteration-Phase-0-Baseline.md) remain unconfirmed.
No assumption is made that this development Windows PC is the server. No local draft
records, first-draft releases, DPAPI recovery or user acceptance identities are changed.
R07 remains open until measured hosted operation and restoration pass.

The [tool README](../../docker/pilot/README.md) provides command syntax/constraints.
[ADR-041](../Architecture/Decisions/ADR-041-dedicated-server-pilot-artifacts.md) records
the deployment decision. Existing [production requirements](Deployment-Guide.md),
[Keycloak runbook](../Identity/Production-Runbook.md),
[migration runner](../../src/Tools/NexaConnect.DataMigration/README.md) and owning
service rollout instructions remain authoritative for runtime grants and data transitions.

## 1. Confirm the target and isolation boundary

Record OS/version, Linux-container support, architecture, CPU/RAM/free storage, existing
workloads, location/upload/network/power reliability and administrative access. Select
an otherwise isolated staging hostname/network/data set before any public cutover.
Unsupported OS/container runtime requires adaptation; never run the Development fixture
publicly. Confirm ownership of all DNS names and static IP/NAT/firewall rules.

Set a maintenance owner/window, alert recipient, off-server encrypted backup destination,
retention and measured RPO/RTO objectives. Agree capacity/load/latency budgets after
measurement; generated resource numbers are placeholders. Keep originals/frozen candidate
available. Do not reset or attach an existing first-draft database, RabbitMQ queue, Redis
namespace, Media bucket or terminal recovery directory as a pilot fixture.

Initialize a fresh private configuration tree using `hosting.mjs init`. The generated
plan intentionally fails validation until facts/settings are supplied. Its boolean
acceptance fields are signed-off prerequisites by the operator, not measured test results.

## 2. Provision independently operated infrastructure

Use a dedicated private container network matching `plan.network` and restrict host and
upstream firewalls. Containers must resolve configured DNS names and route outbound HTTPS.
Private infrastructure must not publish Internet-accessible PostgreSQL, RabbitMQ/management,
Redis, S3 administration, ClamAV, collector, Prometheus, Loki, Grafana or identity management
ports. Confirm denial from a genuinely external client; a private-looking hostname alone
does not prove isolation. OS-specific installation and supervision depend on confirmed facts.

Provision PostgreSQL 17-compatible independently owned service databases/roles following
the existing migration/runtime ownership model. Platform Directory is `PlatformDirectory`
with `platform_directory_app`; other owners use `NexaConnect_<Service>` and their existing
`nexaconnect_<service>_app`. Application accounts must have no superuser/CREATEDB/CREATEROLE,
no cross-service CONNECT and no schema DDL or migration-history writes. Apply actual
version-specific runtime grants/triggers from the migration runner; naming alone proves
nothing. Migration credentials stay out of application containers.

Provision verified-TLS PostgreSQL, amqps RabbitMQ with durable queues/persistence and
separate ACL-scoped producer/consumer credentials, authenticated TLS Redis with persistent
BFF sessions, private S3-compatible storage with versioning/least-privileged Media keys,
scan-enabled ClamAV with current definitions, and private telemetry/alerting. Notification
intake/read remains supported; external Notification delivery and card/provider integrations
remain disabled. This hosting phase adds no offline sales, refunds or financial authority.

## 3. Identity and certificate setup

Operate Keycloak through the existing **separate** `docker-compose.production.yml`,
production validator and bootstrap procedure. Use exact public realm issuer, verified
database TLS, mandatory production identity controls, SMTP/recovery and scoped workload
clients. Identity/admin hostnames must be different from all application hostnames.
Restrict Admin Console/REST to VPN or explicit administrative network controls; the
application edge never proxies identity/admin traffic. Existing realms require reviewed
desired-state changes; importing/restarting never overwrites them.

Set exact Customer BFF and PlatformAdmin BFF redirect/logout origins to their respective
public HTTPS hosts (and external port if nonstandard), without wildcards. Retain distinct
clients, cookies, Redis namespaces and Data Protection application names. POS keeps
`nexaconnect-pos://oauth/callback`, public `nexaconnect-pos` PKCE, no secret. Verify required
audience/subject and Order/POS username mappers plus all downstream workload audiences.
The production environment template/validator now covers every existing confidential
client, including Media, Directory administration and the separate Product Owner client.
`platform-admin-bff` uses `PLATFORM_ADMIN_BFF_*`; it is distinct from the legacy
`nexaconnect-admin-bff` settings. Its post-logout allow-list is the exact Product Owner
origin plus `/signout-callback-oidc`. Apply the template attribute explicitly to an
existing realm and verify actual logout; bootstrap/restart cannot migrate that realm.

Each service configuration needs `tls.pfx` and `data-protection.pfx` in its own private
directory, protected passwords, and a precreated writable key-ring directory. The proxy
needs `proxy.crt` and `proxy.key`. Verify SAN coverage, private key/chain match, trust and
expiry at target; the tool checks file presence, not certificate validity. All backends
use HTTPS on 8443 with DNS aliases matching their SAN/AllowedHosts. Trust private CAs in
runtime/proxy/Node/Windows through supported trust stores; never disable TLS validation.
Preserve old Data Protection certificates and key rings through certificate rotation.

Configure Restaurant→Media and Media→Restaurant gallery reads as generated. Each API
uses its own workload identity only where already required. Do not copy `.env`, local
DPAPI secrets or bootstrap administrator credentials into application configuration.

## 4. Build and bind the application bundle

Install lockfile dependencies (`npm ci` in `src/Frontend`), restore the .NET SDK dependencies,
then run `hosting.mjs build <fresh-bundle> <linux-x64|linux-arm64>`. It publishes thirteen
services, two BFFs with their actual built SPAs, and the existing migration runner/catalog.
Only the intended Production overlay is mounted at runtime. Development/Test/Local
settings are removed from outputs. The builder rejects frontend `.env*` inputs and
filters deployment/Vite process variables. Do not edit source during the build.

Review `manifest.json`: exact file hashes, runtime, schema targets, source revision,
dirty status and non-Markdown/nonignored source input hash. A dirty candidate is development
evidence, not clean CI, release signing or a phase-8 freeze. Archive a separately protected
manifest/configuration identity; the checksum file is not an authenticity signature.
Pin reviewed compatible ASP.NET 10 and nginx images by digest. Pulling/scanning and
native Linux Media encoding compatibility remain target staging checks.

## 5. Preview migrations and establish consumers before publication

Quiesce all application writers/dispatchers/consumers and block external ingress before
upgrades. Take/verify the prechange backup described below. Use the **bundled** migration
runner and Scripts with a separately injected `NEXACONNECT_<SERVICE>_DB` migration connection;
do not expose it on command lines or give it to application containers.

For each owning service, use the manifest target and compatibility version `0.38.0`:

```text
dotnet NexaConnect.DataMigration.dll --service Order --scripts-root Scripts --target 19 --plan --application-version 0.38.0
```

Review all actual plans/checksums/version compatibility, then substitute `--confirm` for
`--plan` only during authorized migration execution. This is a template, not a claim
that Order 19 is every service's target. Do not invoke a destructive downgrade or restore
over new immutable financial history. On failure keep traffic/writers stopped, retain
evidence and forward-recover from the owning service's documented rules.

Establish and verify actual durable Reporting activity/sale/refund/cash-close/correction/
settlement bindings, POS manual-tender and Order reconciliation/Notification intake before
publisher startup. Generated settings enable recovery/consumers/outboxes only after
`queueBindingsReady` is confirmed. A Compose `service_started` dependency is startup
ordering only, **not** application/consumer readiness. Never infer ready from /health/live.

## 6. Render and install under supervision

Fill every generated per-service `appsettings.Production.json`; configure actual trusted
database/broker/cache/storage/collector/admin endpoints and independent 32+ character
credentials. Validate, then render using the commands in the tool README. Configuration
validation never reads root `.env` or prints supplied secrets. It deliberately rejects
ambiguous connection aliases, duplicate overrides, memory persistence, disabled recovery,
plaintext connections, wildcard scope and static outbound tokens.

After an operator transfers artifacts securely to the confirmed server:

- Put the verified bundle at `<deploymentRoot>/bundle`, generated Containerfile at
  `<deploymentRoot>/bundle/Containerfile`, and compose.json/nginx.conf at `<deploymentRoot>`.
- Put only each service's config/PFX files under `<secretRoot>/<service-id>` and proxy
  PEM files directly under `<secretRoot>`. Keep populated plans/configuration private.
- Precreate `<stateRoot>/<service-id>/keys`, writable only by service UID/GID 1654;
  config/certificates are read-only. Protect with host ACLs and encrypted backups.
- Ensure the external private network/infrastructure exist. Validate `docker compose
  -f compose.json config --quiet`, build the pinned runtime images with `docker compose
  -f compose.json build`, and test proxy configuration with the chosen image's `nginx -t`
  using the same mounts/network and running backends before starting edge.
- Start behind closed ingress using `docker compose -f compose.json up -d`. Enable the
  platform-specific container-engine reboot service and verify restart of every application
  plus separately operated infrastructure. `unless-stopped` does not restart a deliberately
  stopped container; no claim of boot supervision follows from rendering a YAML/JSON file.

Only edge publishes HTTPS; it has distinct Customer, Product Owner, POS, Order, Catalog,
Payment and Media virtual hosts. All other owning APIs remain private. Internal API paths
and management endpoints are blocked at the public edge; owning authorization remains
mandatory on every allowed API. Edge preserves exact external Host over verified HTTPS,
removes forwarded headers and suppresses URL access logs to keep OIDC codes/query values
out of logs. Do not enable arbitrary request/response/header logging during troubleshooting.

## 7. Readiness, diagnostics and alerts

Run `hosting.mjs probe <plan> <new-report>` from the intended outside network after TLS/DNS
are ready. It records 13 bounded anonymous HTTPS checks and exact OIDC discovery identity;
no redirect following, tokens, cookies, response bodies or URLs are recorded. A successful
shell GET does not validate loaded JavaScript or live permissions. The public probe is
deliberately separate from private infrastructure checks and authenticated acceptance.

Keep shared structured JSON/OTLP enabled on every service. Query the existing service
identities and validated `correlation_id`, e.g. `{service_name="nexaconnect-order"}` or
`{service_name="nexaconnect-media"}` in Loki; use the probe's correlation UUID for a bounded
trace of the corresponding request. Existing outbound adapters propagate validated
correlation IDs. No new business HTTP service or telemetry pipeline is introduced here.

Reuse the existing [Prometheus rules](../../docker/prometheus/README.md), Payment recovery,
POS settlement and cash-close alerts. Add target-specific host/container disk/memory/CPU,
TLS expiry, external HTTPS/discovery, database backup age/failure, restore-drill age,
broker consumer/queue/dead-letter, Redis/storage/ClamAV and collector availability checks.
Thresholds must follow agreed resource/RPO/RTO budgets; fixed invented capacity promises
are not readiness. Send a real firing **and resolved** rehearsal to the selected recipient,
measure delivery/escalation and record safe event IDs/times. Rule files or a local receiver
alone do not prove alert delivery. Keep monitoring/admin interfaces VPN/private.

## 8. Encrypted off-server backup and isolated restoration

Select/approve a backup tool/destination and OS scheduler after server discovery. Prefer
a managed encryption repository (for example restic) with credentials stored separately
from the server and independently recoverable key escrow. No backup uploader/scheduler
is installed automatically by this change; lack of an operating one is a deployment blocker.

For a bounded maintenance snapshot:

1. Close ingress; stop writers, recoverers, dispatchers and consumers without deleting
   queues/state. Bound shutdown; unresolved financial work stays identifiable by original
   operations. Record candidate/configuration/schema IDs and quiescence times privately.
2. Snapshot every independently owned PostgreSQL database and roles/grants using the
   database operator's consistent backup/PITR procedure, including separate Keycloak DB.
   A stopped application graph coordinates cross-service recovery without pretending
   separate SQL dumps are a distributed transaction.
3. Snapshot durable RabbitMQ messages/definitions/identities and same-node recovery metadata
   using its supported stopped-node procedure; definitions export alone omits messages.
   Preserve Redis RDB/AOF session/cache state with the matching configured runtime version.
4. Back up Media object versions/metadata, certificates/private keys (including old DP
   certificates), per-service key rings, protected configuration/secrets, identity settings,
   container/image digests and infrastructure/monitoring definitions. Exclude Windows
   DPAPI terminal recovery from server migration; retain it under the original user/device.
5. Encrypt/upload off-server, check repository integrity, retain according to approved
   policy and record success/age before reopening ingress. Keep source state if backup
   verification fails. An on-server archive is not disaster recovery.

Restore into a distinct staging project/network/DNS/bucket with deny-by-default outbound
mail/provider/webhooks and **no route/credential access to live databases, queues or object
storage**. Use matching binary and infrastructure versions; never attach restored queues
to live consumers. Preserve canonical issuer/hosts through an isolated private DNS resolver
on isolated clients, or use reviewed nonproduction identity/client changes. Never expose
two writable clones under production DNS. Restored secrets and user hashes remain private.

Restore identity, all service stores/grants, broker messages, Redis, objects, key rings,
certificate keys and configuration together. Start consumers before producers, confirm
native Media scan/variant rendering, then actually log in through both BFFs and a dedicated
staging POS profile. Verify original pending commands reconcile once, receipt/read state,
Kitchen, cash-close and settlement/ledger consistency using the existing joined gates.
Test wrong identity/tenant, lock/resume and removal/fallback without copying protected
terminal state between devices/users. Measure restored RPO and elapsed RTO against plan.
Retain safe pass/fail evidence and destroy only positively identified disposable clone
resources after inspection. Until this full application drill passes, restoration remains
unverified even when every dump restores successfully.

## 9. Staging acceptance, cutover and support

Record exact bundle/config/schema/image identities with each result. Required target cases:

| Gate | Required observation |
| --- | --- |
| TLS and login | Trusted chains/SANs, discovery exact issuer, both BFF login/refresh/logout, POS PKCE callback, forced original-cashier resume |
| Denial | Anonymous BFF/API rejection; customer/platform and cross-tenant/branch denial; live revoked membership/permission; admin/infrastructure external denial; forwarded spoof rejection |
| Remote operations | Intended Windows POS + `/kitchen` device reachability, approved Media delivery; cash/fictional PromptPay → Kitchen → receipt/PDF → close/report; no provider transfer |
| Failures | Identity/API/broker/cache/storage outage, visible uncertainty, original-command replay with no duplicate money; queued messages resume after recovery |
| Restart | Actual engine/host reboot with persistent sessions/keys/queues and pending work; lock/resume and both displays verified separately |
| Recovery | Actual off-server fetch and isolated restored application graph above, measured RPO/RTO |
| Monitoring | Measured capacity/latency/headroom, firing/resolved recipient rehearsal and backup-age alert |

After all gates pass, prepare a private reviewable cutover record: maintenance owner/time,
tested candidate, fresh verified backup, ingress closure, writer/queue drain/quiescence,
schema/app rollout, DNS/TLS endpoint verification, explicit authority transfer, support
contacts/escalation and affected retests. Transfer source of truth once; never enable
competing writers or revert databases across committed financial history. If pre-write
verification fails, keep ingress closed and use the documented compatible candidate;
after new financial writes, stop and forward-recover/reconcile. Do not silently restore
an older backup over new receipts. Live external deployment/cutover needs the actual
confirmed environment and session authorization; this artifact implementation performs none.

Cloud migration remains later, after UI/services acceptance, with separately rehearsed
identity/data/DNS transfer. Phase 7 packaging evaluation can proceed while hosting facts
are pending; Phase 8 freeze and Phase 9 consolidated second test still require this pilot.

## Verification recorded for this change

Focused Node hosting guards: 46 passed, zero failed/skipped. Tests cover every generated
service configuration, scope/credential/TLS/recovery guards, isolated publication/resource
limits, exact-file hashes/tampering, fresh-output behavior, mocked bounded HTTPS/discovery
checks, complete production realm injection and safe failure output. Ten additional
synthetic production-identity guards pass for missing/short/shared secrets, HTTPS origin
and callback rejection plus the exact Product Owner logout template attribute.
Reproduce from repository root:

```powershell
node --test docker/pilot/hosting.test.mjs
pwsh -NoProfile -File scripts/test-keycloak-production-guards.ps1
```

Synthetic certificate fixtures establish presence only. The existing realm-template
validation also passes for all fifteen clients. Generated application Compose and
production Keycloak Compose (bootstrap profile, synthetic environment) both pass
`docker compose config --quiet`; neither check starts containers or proves runtime readiness.

The actual portable build completed successfully:

```powershell
node docker/pilot/hosting.mjs build .runstate/phase6-pilot/final-bundle linux-x64
```

It publishes all fifteen application DLLs, the migration runner/catalog and both portal
`wwwroot/index.html` files. Exact bundle verification passes for 945 files; no
Development/Testing/Test/Local settings remain. Both portal production builds and all
.NET publishes ran through the builder. Recorded source revision is
`ea733919496d951268e67c00a67a086863911b19`, `sourceDirty=true`, source input SHA-256
`c9ee8cd4eb9ba8ba3fa4eb18f079c37bc9695a46db5ad3ff45d287554a610810`.
Schema targets are PlatformDirectory 3, Authorization 15, Restaurant 3, Catalog 4,
Inventory 5, Order 19, Kitchen 3, Customer 2, Payment 18, POS 23, Media 4,
Reporting 22 and Notification 3. This is a locally verified dirty working-tree
artifact, not a clean CI/frozen release candidate or signed distribution.

Final source/documentation audit and `git diff --check` pass. No Linux runtime-container
execution, target `nginx -t`, live HTTPS probe, real identity login, staging financial
transaction, reboot, backup upload, restored application graph or external alert delivery
is claimed. Those observations require the confirmed target and remain deployment gates.
