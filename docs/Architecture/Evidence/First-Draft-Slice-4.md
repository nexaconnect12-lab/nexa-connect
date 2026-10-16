# First-draft slice 4 evidence

Implementation against base `1ea5942bf7329ffd1def24c672e4c9b4b1d6479f` plus the dirty
working tree on Windows, 15 October 2026. Initial worktree was clean. No commit, remote CI,
production, physical printer or real bank-transfer acceptance is claimed.

Implemented [Windows draft packaging/install/update](../../Deployment/First-Draft-Windows-POS.md),
read-only protected recovery preflight, fixed callback registration/secondary mutex ownership,
explicit original-order startup guidance and payment-preserving print outcomes. Added
[POS-owned manual-tender admission](../../API/Terminal-Admission.md), with separate service-owned
Application/Infrastructure boundaries, workload authorization and shared correlation telemetry.
No schema migration; [ADR-037](../Decisions/ADR-037-manual-tender-terminal-admission.md) records
the observation-versus-distributed-reservation trade-off.

Final automated code checks: **833 Unit passed, zero skipped**, with DPAPI enabled;
**6 Architecture passed, zero skipped**; updated integration fixtures compile with zero
warnings/errors; **18 terminal package guards** and **32 lifecycle guards** passed.
The final WPF win-x64 framework-dependent publication succeeded, and its actual executable
passed read-only preflight in an isolated application-container profile. Bounded receipts:
`.runstate/slice4-wpf-final-verification.json` and
`.runstate/slice4-terminal-install-verification.json`. The latter records actual package
installation, repeated installation and status verification in that isolated profile.
No callback registration, UI launch or PDF acceptance is implied.

The first joined invocation found Docker Desktop stopped before startup; owned failed run
`02f06cf3af2544be88652055b392e8f4` was subsequently reset with cleanup verified. The next run
`2d9c3ffeabdb438496de3cbfafcba5c5` started/provisioned but exposed a multiline installer
condition syntax error; it was reset with cleanup verified and is not accepted. All terminal
scripts now have parser checks in the guard runner.

Run `a8bf420c29924aa99a9e605add26dfd0` built a real package but correctly refused elevated
Windows-user installation because retained POS recovery blocked preflight. It was reset
with cleanup verified. The same package installed/reinstalled successfully under the
isolated profile; existing Windows-user recovery was not modified or deleted. Package
release `ec4bc88ea9274dc6964192d3996bd430` references that retired backend and is a test artifact,
not an active cashier installation. Use a compatible original scope or a separate approved
Windows profile before interactive draft testing.

Backend-only run `304237b9f55f409cbbeb5aa4d2a3ca24` exposed the missing Order service-account
username claim at strict admission; it failed and was reset. Added the access-token-only
Order username mapper, preserved strict workload authorization and added startup/guard
checks. Persisted realms need the reviewed mapper update and a fresh token. No failed run
is counted as acceptance.

Final fresh backend run **`c5a369734a5c4e5b970ba13002409f79` passed** all six recorded checks:
baseline, interrupted placement, service recovery, broker-paused checkout, broker-resumed
projection and owning-database effects. Receipt:
`.runstate/first-draft/c5a369734a5c4e5b970ba13002409f79/checkout-verification.json`.
The real-identity baseline rejects an unknown terminal, another cashier's shift and a
customer invocation of workload admission before successfully settling the original Order.
Rejected attempts preserve unpaid state and expose no receipt. Concurrent exact settlement
and placement, immutable receipt, cash/fictional PromptPay, Kitchen conflicts, cancellation,
stock compensation and outage/replay checks continue to pass with original owning-effect
counts. Cleanup is verified. All Slice 4-owned test runs are reset; unrelated projects were
not removed. Backend and isolated-profile installation receipts are separate evidence,
not a combined acceptance claim on the existing Windows-user recovery profile.

Interactive WPF sign-in/callback, token renewal, idle lock/relogin, UI restart/response loss,
same-scope pending-work update and actual PDF print/reprint/cancel/failure inspection remain
pending user execution. Slice 4 implementation and automated verification are complete, but
its full device exit gate remains open until those interactive cases pass. Automated
PKCE/backend and protected-state tests do not attest these
device interactions. Physical peripherals, real transfers, manual-tender refunds and full
offline execution remain excluded. Admission is a point-in-time observation; later closure/
revocation races and prior invalid-terminal history are not repaired by this slice.

Documentation updated: root README; project overview; canonical system and restaurant
architectures; ADR-037; API Terminal-Admission, Manual-Tender-Settlement and Paid-Order-Receipts;
Identity Client-Matrix and Production-Runbook; Database-Guidelines; deployment First-Draft-Windows-POS,
First-Draft-Test-Stack, First-Draft-Checkout, Deployment-Guide and production.env.example;
Keycloak README; November slice plan; POS client, Order and POS service READMEs;
first-draft browser README; this evidence. Both architecture summaries changed
to describe the new dependency and local installation/recovery boundaries. Independent final
documentation audit and focused installation/identity follow-ups passed. The primary
reviewed the finished code/documentation diff and `git diff --check` passed. Changes remain
uncommitted; the evidence records the dirty source baseline rather than a clean release.
