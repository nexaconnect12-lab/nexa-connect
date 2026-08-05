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
