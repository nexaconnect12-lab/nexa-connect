# ADR-008: Signed Omise full-capture creation context

Status: Accepted for the Development/Testing Omise adapter

Date: 2026-09-17

## Context

Read-only account evidence shows successful paid THB card charges that omit both `authorization_type` and `captured_amount`. Treating absent fields as default values or accepting every paid charge would weaken partial-capture/refund protection. Existing Payment intents already retain intent/organization/Order identity and exact amount; interrupted authorization recovery finds the original provider charge using that metadata. No new production provider support is implied.

## Decision

The Payment-owned Omise infrastructure adapter creates deferred authorizations with explicit `authorization_type=final_auth`, and requests the exact positive `capture_amount` when capturing. Omise documents final authorization as one full-amount capture; partial capture uses pre-authorization. See [official capture contract](https://docs.omise.co/capture/thailand).

At charge creation it includes `nexa_capture_mode=final_auth_v1` and `nexa_capture_proof` metadata. The proof is lowercase hexadecimal HMAC-SHA256 using the existing test secret as key. The signed UTF-8 message is domain-separated with `NexaConnect/Omise/FullCapture/v1` and newline-separated canonical intent, organization and Order UUIDs, invariant integer satang, `thb` and the versioned mode. A fixed-length hexadecimal proof is compared in constant time. No key, token, card data or raw proof enters NexaConnect logs, events, intent responses or operator reports.

This records the trusted request's full-capture context on the provider charge in the same creation operation, avoiding a second metadata write or database migration. Provider metadata persists across local process loss and can be verified against the existing service-owned intent after reference-loss lookup. It does not independently prove local ownership: command/status paths still require exact Payment/organization/Order/amount/currency/test-mode matching and stable charge identity; searches still reject duplicates and ambiguity. The account API credential and TLS provider boundary remain trusted. The creation-time proof does not bind a provider charge ID, which is not yet known; copying the same signed context to another charge with identical bound values can preserve a valid proof. It is not independent protection against actors who can modify account metadata or possess the signing/API credential. Stable returned charge identity, local binding and rejection of ambiguous search candidates remain mandatory. Do not copy, edit or backfill proof metadata onto existing charges.

When `captured_amount` exists it must equal the full amount; an explicit partial value always wins over fallback evidence. When it is absent, returned `final_auth` remains sufficient only with all strict financial flags. If authorization type is absent too, verified signed context can establish the request's mode. Confirmation still requires successful, paid, authorized, unreversed, deferred-capture, non-capturable and non-reversible state, zero refunds and exact amount. Explicit `pre_auth` or another type cannot be overridden. Missing/conflicting financial flags, invalid/missing proofs and malformed/ambiguous responses remain unknown. Paid charges never receive another capture POST. No POST retry or compensation is introduced.

Read-only inspection reports only `TrustedFullCaptureContextVerified`; raw omitted fields remain null and local intent ownership stays unverified. The live gate checks provider retention of the signed context before capture. Direct credentialed verification passed on 2026-09-17; see the [retained acceptance evidence](../Evidence/Omise-Test-Account-Acceptance.md). Four-scenario hosted Omise interruption verification [passed on 2026-09-17](../Evidence/Omise-Hosted-Recovery-Acceptance.md); pre-authorization interruption remains excluded.

## Consequences

No schema, route, new secret setting or outbox/audit contract changes are needed. Existing PostgreSQL leases, local state transitions, transactional events and bounded status recovery are unchanged. The provider boundary now owns retention of the signed request context. Dropped/edited metadata fails closed; no financial success is inferred from a bare metadata mode or a paid flag alone.

Changing the test secret invalidates prior proofs verified with that key. Resolve uncertain operations before rotating it. An old charge may still be confirmed by independent explicit provider fields; otherwise keep it in review. Key-ring migration, production credentials, refunds, settlement and partial-capture support remain outside this slice. This proof establishes request provenance and intended mode; it is not a settlement attestation.
