# NexaConnect Infrastructure

This project contains narrowly scoped infrastructure registration shared by API hosts.

`AddNexaConnectApiAuthentication` configures strict Keycloak JWT bearer validation and a fallback authorization policy that denies anonymous access unless an endpoint is explicitly marked `AllowAnonymous`.

Required configuration:

```json
{
  "Authentication": {
    "Authority": "https://identity.example.com/realms/nexa-prod",
    "Audience": "nexaconnect-api",
    "RequireHttpsMetadata": true,
    "ClockSkewSeconds": 30
  }
}
```

The registration rejects missing configuration, non-HTTPS production metadata, invalid audiences, excessive clock skew, and the checked-in `.invalid` deployment placeholder.

Production hosts call `AddNexaConnectDataProtection(configuration, environment, applicationName)`. Outside Development it requires `DataProtection:KeyDirectory` to already exist and be writable, and requires a password-protected PFX at `DataProtection:CertificatePath`; the certificate encrypts the durable key ring. `EnsureProductionHttps` requires an HTTPS listener plus a password-protected PFX at `Tls:CertificatePath`. Keep both passwords in a secret manager and grant each service access only to its own key directory and certificate.

`PostgresInboxStore` inserts the message identity and acquires its processing lease through separate parameterized commands in one PostgreSQL transaction. Completed messages remain suppressed, active leases return busy, and released/expired claims are retryable. `InboxPersistenceTests` provides the live PostgreSQL verification boundary.

`RabbitMqOutboxTransport` lazily establishes its publisher connection, enables RabbitMQ automatic/topology recovery, and replaces an established connection when it is no longer open. A publish failure invalidates the owned connection; the dispatcher leaves the outbox row unpublished and retries it on a later poll. Publisher confirms and persistent delivery remain required, and applications never delete durable outbox state merely because the broker is unavailable.
