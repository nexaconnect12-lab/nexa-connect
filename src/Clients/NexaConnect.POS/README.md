# NexaConnect POS

Keycloak client: `nexaconnect-pos`

- Public installed client
- Authorization Code flow with mandatory PKCE S256
- Redirect URI: `nexaconnect-pos://oauth/callback`
- No embedded client secret
- Tokens stored with Windows data protection or an approved hardware-backed credential store

Online Keycloak authentication enrolls the employee and device. Offline unlock, cached permissions, expiration, manager overrides, and audit records remain NexaConnect responsibilities and must not be represented as indefinitely valid Keycloak tokens. The POS must clear online credentials when a device is revoked or deregistered and must never treat a locally entered PIN as a Keycloak password.
