# ADR-051: Gallery-only readers for continuous locked playback

Status: Accepted, 28 October 2026. Extends the refresh decision in [ADR-040](ADR-040-branch-approved-screensaver-gallery.md).

## Context

Lock removes cashier access/refresh tokens. The original five-minute image lease expired
during a long lock even when services were healthy. Retaining cashier credentials or
extending image leases would weaken authentication or withdrawal.

## Decision

Media issues a purpose-separated Data Protection capability after an authenticated customer
passes Restaurant's live branch menu-read check. Its protected payload binds a random ID,
original subject, organization, branch, issue time and absolute expiry of 24 hours. It is
accepted only in `X-Nexa-Gallery-Reader` on Media's gallery-reader GET, never as a Bearer,
query or cookie credential. It carries no sales, payment, approval or configuration authority.
POS receives no workload credential.

Each read uses Media's existing workload token to call a dedicated Restaurant internal
route. Restaurant resolves the active owning restaurant, then checks the original subject's
live Directory organization/product membership and Authorization's fixed `catalog.menu.read`
permission at organization/restaurant/branch scope. Dedicated boolean-check routes accept
only authenticated `azp=nexaconnect-media-service`, with normal issuer/audience validation.
General ServiceWorkload policy is unchanged. Callers cannot choose another permission or
obtain membership details. Authorization retains its existing decision audit. No cross-context
database access or schema is introduced.

Media reads only current approved selection and ready Catalog product display variants.
It repeats authority/selection checks after provider I/O; changed selection fails the
attempt. Image leases remain at most five minutes, clipped to reader expiry. Concurrent
revocation/deletion after checks can remain visible until that issued lease expires; this
is not a distributed transaction. OIDC logout/account disable alone does not revoke a reader;
live membership/permission removal is rechecked. Reader expiry is absolute, never self-renewed.

POS refreshes once a minute while active or locked with a verified original-operator marker
and safe display topology. Only an active authenticated cashier obtains/replaces readers
(renewal begins within the final hour). DPAPI stores them separately from cashier tokens
and financial recovery, scoped by authority, Media endpoint, organization, restaurant, branch,
store and terminal, and checked against the original subject. Restart requires that verified
operator marker. Sign-out, denial or invalid content clears memory and serializes cache removal.
Cashier lock/absolute-session and original-resume rules remain authoritative. Refresh does
not count as activity. After reader expiry, fresh cashier authentication is required.

Unchanged role playlists/text/interval retain rotation time across renewed bundles. Changed
selection resets rotation. Outages retain only the unexpired existing image lease and reader;
expired images become neutral, and reconnect can recover while the reader remains valid.
Display-safety lock pauses refresh.

## Consequences

Deploy compatible Media, Restaurant, Directory, Authorization and POS together. Persist Media's
existing Data Protection key ring; retiring keys requires cashier sign-in. Reads have bounded
per-IP/global in-memory rate accounting and require healthy dependencies/workload identity.
A copied reader exposes only approved public imagery within its scope/lifetime, but remains
a secret. Never log it or bodies/headers. Offline playback beyond the image lease, physical
acceptance and public exposure remain separate gates.

See [API](../../API/Screensaver-Gallery.md) and [operations](../../Deployment/POS-UX4-Locked-Gallery.md).
