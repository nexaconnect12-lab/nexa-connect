# Keycloak Production Runbook

Omise test webhooks use a separate base64 HMAC credential (`OmiseWebhooks__Secret`), distinct from OIDC workload credentials and the API test secret used for signed capture context. The opt-in callback authenticates timestamped raw-body signatures rather than OIDC; it independently fetches canonical event data with Payment's test API key and validates local ownership before status recovery. One/two-signature rotation headers are supported with the current configured webhook secret; update the secret before retiring its predecessor. This is not a historical-key ring. Production enablement is rejected. Never expose callback body/signature headers in proxy/tunnel logging. See [bounds, configuration and test-account external-delivery evidence](../Deployment/Omise-Webhooks.md).

The opt-in Development-only POS test-card path retains normal PKCE/tenant authorization and the exact Order workload boundary at Payment. A masked card token is transient request data, separate from OIDC credentials and the provider secret; POS clears its field on submission attempts, sign-out and session lock, and excludes it from SQLite/outbox/recovery. Permission to enter another token for an original checkout is also memory-only and cleared at attempt/session lock. After restart or sign-in, Verify original order must obtain validated `cardTokenRequired=true` before token entry; uncertain/started payment must remain in server reconciliation. Order accepts test-card checkout only in Development/Testing when `CardCheckout__EnableOmiseTestCheckout=true`. The client accepts only explicitly enabled loopback service endpoints. Operator-attested local POS verification passed on 2026-09-18; it does not independently certify UI/OIDC. Credentialed fifth-boundary acceptance passed on 2026-09-18; production browser collection remains planned; see [test-only scope](../Deployment/Omise-POS-Card-Token-Handoff.md).

Payment's Omise test adapter uses a separate external-provider credential, `PaymentProvider__OmiseSecretKey`, not an OIDC workload secret. The exact Order-workload and organization boundaries remain unchanged. The [hosted Omise acceptance harness](../Deployment/Omise-Hosted-Recovery-Acceptance.md) uses local signed workload-token fixtures, not live OIDC acceptance. It strips Omise credentials/tokens from the Order child host and supplies only the explicit provider test secret to Payment; fresh card tokens are used only by the acceptance arm in the default four-scenario matrix. The opt-in fifth scenario sends a fresh token through the existing tenant-authorized Order API and exact Order-workload Payment API after interruption; no token is stored for the recovery worker. Development-only POS test-token handoff is implemented with separate opt-in guards, but production browser card collection remains planned. Inject the test key only into Development/Testing Payment; live keys and deployed environments are rejected. Never persist/log card tokens or provider credentials. The Development/Testing Omise adapter also uses that test secret as the HMAC key for versioned full-capture request metadata. Changing it invalidates old creation-context proofs; resolve uncertain recovery/review before rotation. Independent explicit provider fields may still confirm an older charge, otherwise retain uncertainty. No historical-key ring or production signing-key migration is implemented. This provider credential is separate from OIDC signing-key rotation. See [ADR-008](../Architecture/Decisions/ADR-008-omise-full-capture-context.md). See [Omise test setup](../Deployment/Omise-Test-Account-Acceptance.md); production merchant onboarding is a separate gate.

## Deployment contract

The production Compose definition assumes:

- a managed or independently operated PostgreSQL database with encrypted, tested backups;
- a TLS-terminating reverse proxy that is the only network path to Keycloak port `8080`;
- an external Docker network shared with that proxy;
- separate public and administrative DNS names;
- a secret manager or protected deployment environment supplying every secret;
- monitoring that reads management port `9000` only from the private network.

Do not publish ports `8080` or `9000` directly. The proxy must overwrite forwarded headers, and `KEYCLOAK_PROXY_TRUSTED_ADDRESSES` must contain only the actual proxy addresses or CIDRs. The public virtual host must reject Admin Console and Admin REST paths; expose them only through the restricted administrative virtual host. `KC_HOSTNAME_ADMIN` changes generated URLs but does not replace proxy access controls.

The JDBC URL must require verified TLS. When PostgreSQL uses a private certificate authority, add that CA to the Keycloak container truststore through the deployment platform rather than disabling certificate verification.

## Preflight

1. Copy `.env.production.example` outside the repository and replace every example value.
2. Generate independent random values of at least 32 characters for each database, SMTP, and confidential-client secret.
3. Validate the file:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-keycloak-production.ps1 -EnvironmentFile C:\secure\keycloak.production.env
```

4. Create the external proxy network if the platform has not already created it.
5. Confirm a current database backup and a tested restoration procedure.

## First deployment

The realm bootstrap is non-overwriting and must run before the server starts:

```powershell
docker compose --env-file C:\secure\keycloak.production.env -f docker-compose.production.yml --profile bootstrap run --rm keycloak-bootstrap
docker compose --env-file C:\secure\keycloak.production.env -f docker-compose.production.yml --profile bootstrap run --rm keycloak-bootstrap-admin
docker compose --env-file C:\secure\keycloak.production.env -f docker-compose.production.yml up -d keycloak
```

Verify readiness from the private network, OIDC discovery through the public hostname, and Admin Console access only through the administrative hostname.

The bootstrap administrator is temporary. Use it only from the restricted administrative hostname to establish named administrative accounts with MFA, verify recovery access, and then invalidate the bootstrap credential. Never add bootstrap credentials to the long-running Keycloak service.

## Application settings

Every API deployment requires:

```text
Authentication__Authority=https://<public-identity-host>/realms/<realm>
Authentication__Audience=nexaconnect-api
Authentication__RequireHttpsMetadata=true
Authentication__ClockSkewSeconds=30
```

The issuer must match the discovery document exactly. APIs reject anonymous requests by default; explicitly mark only genuine public endpoints with `AllowAnonymous` after security review.

## Realm controls

The initial realm configuration provides:

- exact redirect URIs and origins;
- Authorization Code flow only for interactive clients;
- PKCE S256 for Mobile and POS;
- disabled password/direct-access and implicit grants;
- five-minute access tokens and bounded sessions;
- brute-force protection and a strong password policy;
- verified email and SMTP configuration in production;
- mandatory TOTP enrollment in production;
- realm and administrative events;
- a dedicated API audience and deliberately mapped coarse roles.

For managed Windows POS devices, the deployment package must register the `nexaconnect-pos` URI scheme to the signed POS executable and verify that the exact redirect URI is present in the realm client configuration. The POS client is public and must not receive a client secret. Set its idle and absolute session controls at or below organizational and Keycloak realm limits. Verify refresh-token rotation, rejected-refresh credential clearing, transient identity outage at access-token expiry, five-minute idle lock, forced interactive reauthentication, and preservation of active financial/recovery state before release. Never configure timeout handling to close a shift or cash session automatically.

Platform Directory remains authoritative for organizations and memberships. Product services remain authoritative for restaurant resources and fine-grained permissions.

## Changes and upgrades

Startup and bootstrap imports never overwrite an existing realm. Treat later realm changes as versioned administrative migrations, test them against a restored non-production database, and back up before applying them.

Pin every Keycloak image by version and digest. Review release and upgrading notes before changing either value. Use Keycloak's update-compatibility check to decide between rolling and recreate deployment. A rollback across an incompatible database migration requires restoring both the previous Keycloak version and the matching database backup.

For availability requirements above a single instance, deploy at least two instances with a supported cache discovery stack, sticky sessions at the load balancer, low-latency networking, and a highly available synchronously replicated database. Size and load-test this topology against measured login and refresh traffic.

## Go-live evidence

Record evidence for:

- login, refresh, logout, and back-channel logout;
- wrong issuer, wrong audience, expired token, disabled user, and revoked session rejection;
- MFA enrollment and recovery;
- SMTP delivery and password recovery;
- signing-key rotation without application outage;
- database backup restoration;
- Keycloak instance and availability-zone failure;
- proxy header spoofing rejection;
- Admin Console network restriction;
- audit-event ingestion, alerting, and retention;
- load, capacity, recovery-time, and recovery-point objectives.
