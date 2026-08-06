# Keycloak Production Runbook

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

For managed Windows POS devices, the deployment package must register the `nexaconnect-pos` URI scheme to the signed POS executable and verify that the exact redirect URI is present in the realm client configuration. The POS client is public and must not receive a client secret.

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
