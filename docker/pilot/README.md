# Dedicated-server pilot application artifacts

`hosting.mjs` requires Node 24+ and uses only built-in modules. Build additionally requires
the repository .NET 10 SDK/NuGet restore and installed, lockfile-matching frontend dependencies.
It supports `linux-x64` / `linux-arm64` application outputs for a separately confirmed Linux
container host. It does not install Docker, start services, read `.env`, run migrations,
upload backups, change DNS, update a POS installation or deploy to the user's server.

See the [Phase 6 operating guide](../../docs/Deployment/Next-Iteration-Phase-6.md) and
[ADR-041](../../docs/Architecture/Decisions/ADR-041-dedicated-server-pilot-artifacts.md).

From repository root:

```powershell
node --test docker/pilot/hosting.test.mjs
node docker/pilot/hosting.mjs init .runstate/pilot-config
node docker/pilot/hosting.mjs build .runstate/pilot-bundle linux-x64
node docker/pilot/hosting.mjs validate .runstate/pilot-config/plan.json .runstate/pilot-config
node docker/pilot/hosting.mjs render .runstate/pilot-config/plan.json .runstate/pilot-config .runstate/pilot-bundle .runstate/pilot-render
node docker/pilot/hosting.mjs probe .runstate/pilot-config/plan.json .runstate/pilot-probe.json
```

Initialization deliberately produces placeholders and false prerequisites. Fill measured
server facts, actual hostnames, independently generated credentials, resource budgets,
operations objectives and gate evidence before validate/render. Never commit populated
configuration. The supplied 512 MiB/0.5 CPU budgets are editable placeholders, not sizing
advice. Validation checks aggregate application memory only; include infrastructure,
edge/OS, headroom, disks and observed load in capacity review.

Fresh outputs only; existing paths are never overwritten or reset. Partial build failures
remain available for local inspection and cannot be rendered without a complete manifest.
Do not modify source during a build. Builds strip Development/Testing/Test/Local settings,
reject frontend `.env*` build inputs, suppress raw child output and require unchanged
non-Markdown source inputs. Failure reports intentionally omit raw configuration/errors.
Use normal `npm run build` / `dotnet publish` privately to diagnose a failed build.
Install frontend dependencies with `npm ci` first; the builder does not install them.

Config and connection formats are intentionally constrained: exact owning database/role
names, verified TLS, semicolon-separated database options from an allow-list, one Redis
host:port with allow-listed options, and amqps URIs without query/fragment. Use independently
generated 32+ character hex secrets to avoid delimiter ambiguity. The Order workload
secret may occur in its two adapters; other ownership scopes cannot share its value.
Media storage keys must meet the same 32-character policy; provision suitable credentials.
Runtime roles must actually be least-privilege: matching a role name is not a privilege audit.

Render validates local presence of the two PFX files per service and proxy PEM files;
it does not cryptographically certify them. Kestrel's existing production startup loads
password-protected PFX/private keys. Use trusted certificates and verify expiry, SANs,
chain and proxy key match on staging. File mode hints are not Windows ACL enforcement.
Apply owner-restricted ACLs/permissions on both build and server hosts.

The graph has 15 applications plus edge. Only edge publishes a port. Per-service paths
must exist and be readable/writable by UID/GID 1654 as appropriate. `create_host_path:false`
prevents missing bindings being silently adopted. The migration executable is bundled
but has no runtime service/secret; invoke it separately under reviewed migration identity.
The generated Containerfile belongs at `<deploymentRoot>/bundle/Containerfile`, beside
the service context directories. Compose and nginx.conf belong at `<deploymentRoot>`.

Anonymous probes perform 13 bounded HTTPS requests with fresh correlation IDs, no tokens,
no redirects and no body/URL logging. They check portal/Kitchen shell entry, anonymous
BFF denial, hidden metrics and exact discovery issuer/endpoints. They do not prove static
asset correctness, authenticated operation, scope denial, resource readiness, backup,
restoration or reboot. Mocked probe tests never count as a deployed-server observation.
The tool uses a 24 MiB edge body limit and default nginx buffering; its generated nginx
configuration requires target `nginx -t` and a browser/media smoke test before exposure.

Service telemetry retains existing shared JSON/OTLP identities (`nexaconnect-<service>`,
including `nexaconnect-platform-directory`, `nexaconnect-customer-bff`, and
`nexaconnect-platform-admin-bff`). No new HTTP application/adapter is introduced here.
Do not send raw logs, manifests of private configuration, tokens or backup contents to CI.
## Phase 8 release binding

New builds add the shared `releaseSource` identity used by the
[freeze tool](../../scripts/release/README.md). It includes Git-normalized nonignored
non-Markdown inputs, including root settings, packaging and workflow files. Both producer
identities are checked before/after publication. Old manifests remain readable (inventory
order is immaterial; exact files/hashes are still required), but old builds without the
shared field cannot satisfy the new release freeze. Rebuild for a replacement candidate.
