# Omise test token helper

Development-only static browser tokenization helper, bound to `https://localhost:54443`. It uses the normal ASP.NET Core HTTPS development certificate. Provision and trust that certificate with `dotnet dev-certs https --trust` before starting; do not bypass browser certificate warnings. This is separate from the untrusted, certificate-pinned provider simulator.

```powershell
dotnet run --project src/Tools/NexaConnect.OmiseTokenHelper --no-launch-profile -- --environment Development
```

Open `https://localhost:54443`, enter your own `pkey_test_...` public key, click Generate fresh token and Copy token. Generate a distinct token for each acceptance scenario, using them promptly. No secret key is needed here. The fixed Visa success test card goes directly from browser Omise.js to `https://vault.omise.co`; this host has no token/card submission endpoints and creates no charges. Public keys and the latest masked token remain only in browser memory; Copy explicitly writes the token to the clipboard. Clear the clipboard and close the page when finished. Do not use real cards.

CSP permits only the official Omise script and Vault connection, blocks form submissions and framing; responses prohibit caching. Inherited custom Kestrel endpoints and environments other than Development are rejected. Port conflicts fail startup; stop only your existing helper or choose another time to run it. Stop with Ctrl+C. No database, tenant APIs, deployment routes or POS handoff are added.

Shared JSON/optional OTLP service name is `nexaconnect-omise-token-helper`. Query `{service_name="nexaconnect-omise-token-helper"}` when exported to Loki. Requests contain only static asset paths; never add card data, keys or tokens to URLs, server requests or logs. See [Omise acceptance](../../../docs/Deployment/Omise-Test-Account-Acceptance.md) for masked environment injection and the separately guarded test-charge gate. Direct Omise test-account acceptance passed on 2026-09-17. That financial gate does not independently certify the helper browser workflow or POS token handoff.
