# NexaConnect Mobile

Keycloak client: `nexaconnect-mobile`

- Public native client
- Authorization Code flow with mandatory PKCE S256
- Redirect URI: `nexaconnect://oauth/callback`
- System browser or operating-system authentication session
- Authorization code verifier and state generated with cryptographically secure randomness
- Refresh tokens stored only in operating-system secure credential storage
- No embedded client secret

The application must verify state, issuer, and nonce, clear tokens on logout or device deregistration, avoid logging tokens, and reauthenticate when refresh fails. Universal/app links should replace custom schemes if the final mobile platform supports securely claimed HTTPS callbacks.
