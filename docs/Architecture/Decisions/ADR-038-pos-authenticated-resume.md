# ADR-038: Identity-bound POS lock and authenticated resume

- Status: Accepted
- Date: 2026-10-21
- Scope: Windows POS session protection; no service/database ownership change

## Context

The next iteration requires idle lock with authenticated resume. The prior client retained
work but its credential-free lock marker had no verified operator binding. A fresh login
could replace the local identity while the original cashier's work remained loaded. A
screensaver alone does not secure controls; locally decoding an unvalidated JWT is not a
trusted identity check.

## Decision

Resolve the subject through the configured Keycloak authority's authenticated UserInfo
endpoint after code redemption and refresh. Persist the subject/authority pair in the
current-user DPAPI token record and preserve it in the credential-free lock marker.
Require the same pair for resume and browser `prompt=login` while locked. Owning services
still authorize each protected action, including tenant boundaries and financial limits.
No new identity client, secret, role or service API is introduced.

Startup with saved credentials also locks; a saved marker remains locked. Hide and disable
the workspace behind a Resume / Authenticate overlay. Retain shift, drawer and original
pending operation identities; lock never closes work or submits/replays financial commands.
Already-started commands may finish through their existing recovery paths while locked.

Retain bounded idle/absolute/refresh policies and check them even while busy. Retry transient
renewal failure after a delay while access remains valid; expiry locks. Serialize refresh
and reject stale/cancelled responses so they cannot overwrite a newer session or lock.
Replace token files via temporary-file moves. No online credential survives in a lock marker.

Legacy retained work without a verified binding blocks resume for original-cashier
reconciliation. Do not infer ownership from a typed username, delete recovery or assign it
to the next login. Mask unbound retained work even when its token file is missing; corrupt
session metadata blocks startup and is preserved for investigation. Empty legacy profiles
can establish a verified operator normally.

## Consequences

UserInfo must be available to the public client's `openid` token. Require HTTPS outside
loopback and disable redirects. Use shared `nexaconnect-pos-client` JSON/OTLP logging with
safe correlation/categories/status only; never log subject, credentials or payloads.

Reconcile active legacy work through the compatible original installation before adoption
unless a separately verified recovery procedure is supplied. No automatic legacy owner
reassignment, DPAPI portability or database schema migration is introduced. The marker is
not financial authority: revoked provider sessions fail authentication and changed product
permissions are independently enforced by owning services after unlock.

Dual displays, gallery screensavers and alternative PIN/biometric mechanisms remain later
scope. Actual Windows/browser/device acceptance is pending. See the
[Phase 1 guide](../../Deployment/Next-Iteration-Phase-1.md).
