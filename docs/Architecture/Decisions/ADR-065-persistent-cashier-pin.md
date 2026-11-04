# ADR-065: Persistent cashier PIN and privately renewed session resume

- Status: Accepted
- Date: 2026-11-03
- Supersedes: ADR-064 PIN lifetime and access-expiry decisions; customer display and custody decisions remain unchanged.

## Context

Cashiers need one personal PIN until they change it, including after future full logins. A five-minute access-token lifetime must not force repeated password entry at ordinary idle locks. PINs must not establish an identity session, extend its original maximum, transfer retained work, or authorize offline operations.

## Decision

POS owns a central credential aggregate keyed by organization, configured issuer and validated subject. POS26 stores an opaque protected verifier and durable attempt state, with transactional immutable audit. PBKDF2-HMAC-SHA256 uses 600,000 iterations, random 16-byte salt and a 32-byte fixed-time comparison; Data Protection additionally protects the verifier with identity-specific purposes. Keep the POS key ring with database backups. Neither the raw PIN nor an unkeyed low-entropy request fingerprint is persisted.

Self-service enrollment requires live `pos.cashier-pin.use`, an active exact branch/store/terminal and the authenticated caller's own identity. Changing an active credential also requires its current PIN. Native enrollment/change requires full sign-in within two minutes; this is a client UX control, not a server claim of fresh OIDC authentication. The server admits initial enrollment for an unset credential using current authenticated bearer authority and live authorization. Existing credentials cannot be overwritten using that bearer alone.

Five consecutive incorrect verifications or current-PIN changes impose a fifteen-minute server lockout. It survives logout/restart and does not erase enrollment. Successful verification after the lockout resets failures. A credential has no scheduled expiration. Authorization17 grants use to cashier/store-manager/tenant-admin; only tenant-admin receives organization-wide reset/revoke by default. Reset allows new enrollment; revoke disables it until an explicit reset. Administrators cannot read or choose the PIN. Additional grants must account for the organization-wide credential scope.

Manual/idle lock and eligible active-refresh connectivity outage at access expiry persist only the nonsecret identity marker and privately suspend the refresh-capable credential set in memory. On explicit PIN resume, serialize with ordinary refresh, renew privately at Keycloak, bind authenticated UserInfo to the original operator, verify the PIN online, then publish/save credentials only if the same lock is still current and the renewed access token remains live. Rotation stays private across a PIN denial or temporary PIN-service failure. An ordinary refresh completing after a same-cashier eligible manual/idle lock may retain rotation privately, with the same reference/pending-login fences, but cannot unlock or publish authority. A late connectivity failure cannot replace an existing UI lock. Reference, cancellation, pending-login and absolute-deadline fences reject late responses after security lock, full sign-in or shutdown. No local verifier or fallback bypass remains.

The original absolute session deadline never advances. Provider idle/max/revocation remain authoritative; restart, logout, security locks and actual identity-session expiry require full login. PIN enrollment survives those events. This change does not renew provider sessions unattended while locked or change shared realm policy. Configure and validate an appropriate bounded shift policy before promising all-day resume after long breaks.

## Consequences

Deploy matching POS/native/Authorization binaries and POS26/Authorization17 at compatibility0.45.0 before enabling the feature. Existing ephemeral PINs require one-time enrollment; existing business state is untouched. PIN verification is online-only. Database/key-ring outages keep POS locked, with connectivity retry and full-login fallback. Successful verification is never replayed: duplicate operation IDs return a denial without consuming another attempt. Lost write responses require a fresh status read.

Administrative reset/revoke is currently an authenticated API, not a portal management screen. Tested fingerprint/card adapters remain separate future work. Synthetic tests do not certify real Keycloak policy, physical hardware or installed operator acceptance.

See [identity and operations](../../Identity/POS-Session-PIN.md), [API](../../API/Cashier-PIN.md) and [persistence](../../Database/Cashier-PIN.md).
