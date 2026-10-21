# Next-iteration phase 1 — POS idle lock and authenticated resume

Implemented in the working tree on 21 October 2026 against starting commit `929cc76`.
This is source/build verification, not a published replacement for the first-draft package.
See [ADR-038](../Architecture/Decisions/ADR-038-pos-authenticated-resume.md) and
[the phase plan](../Planning/Next-Iteration-Roadmap.md).

## Behavior

- Idle presents a dedicated overlay and hides/disables the operational workspace.
  Resume / Authenticate is the entry back to protected work. A click, key or touch alone
  cannot unlock it. Shift, drawer, submitted order and original pending operations remain;
  locking does not sign out operational work or submit/replay commands.
- Defaults remain five minutes idle, ten hours absolute and a 60-second renewal lead.
  Active renewal preserves the original absolute deadline. Idle, absolute expiry,
  rejected identity renewal and explicit sign-out remain distinct paths.
- The credential-free marker retains the subject established by configured Keycloak
  UserInfo and its authority. Resume forces browser authentication; a different identity
  cannot replace the marker. Cancel/timeout/failure leaves retained work locked.
- Startup locks saved credentials too. Retained work without a verified binding is masked
  even when the token file is missing. Corrupt or unreadable session metadata blocks startup
  with a recovery warning and remains untouched for investigation. Explicit sign-out is blocked while a shift, drawer,
  cart, pending order/refund/review or unresolved outbox remains. Once work is resolved,
  sign-out permits another operator.
- Lock/renewal checks run while a business command is busy. Already-started commands finish
  through existing recovery; old refresh and cancelled callback responses cannot overwrite
  newer credentials. Lock masks work immediately without creating a replacement command.
- Identity HTTP requests have a 20-second timeout, bounded response buffering and no
  redirects. Network failure/timeout delays renewal retry 15 seconds while access is valid;
  expiry locks. Window closure cancels renewal.
- Receipt display and transient test-card entry clear on lock. Unsubmitted cart data stays
  in process during lock; durable cart saving across process restart is not added. Existing
  persisted checkout/settlement recovery remains the restart source.

## Identity and upgrade prerequisites

The configured authority must serve `/protocol/openid-connect/userinfo` for the POS public
client's `openid` access token. No new client secret, role or permission is added. Provider
identity is resolved online, not inferred from locally decoded access-token claims. Live
branch/role/amount/tenant authorization remains in owning services for protected operations;
unlock is not an authorization grant or offline capability.

Before updating, reconcile active legacy work using its compatible original installation
if its token/marker has no verified subject binding. New code blocks resume for retained
work with no verified owner, including a missing/corrupt marker. It neither clears recovery
nor invents an owner. Preserve the original scope and installation for investigation.
Existing read-only install preflight checks configuration/database scope, not this operator
binding or UI acceptance; passing preflight does not establish that legacy work can resume.
Normal application startup checks retained database scope read-only before opening session
metadata or registering the callback, so a wrong-scope launch cannot replace the original
operator's marker.

Do not delete, copy, rebind or edit DPAPI/SQLite state to bypass this guard. Empty compatible
profiles can establish identity normally. The original first-draft package, environment and
UAT records have not been replaced or marked passed.

## Verification executed

Normal-access Windows verification passed:

- WPF build: zero warnings/errors.
- Full Unit suite with Windows DPAPI opt-in: **858 passed, 0 failed, 0 skipped**.
- Architecture suite: **6 passed, 0 failed, 0 skipped**.

```powershell
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE = '1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore
dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj --no-restore
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
```

Initial sandbox execution exited without diagnostics. Normal-access verification passed.
An earlier focused run without the opt-in skipped six existing Windows cases; the final
enabled full-unit run executed them. Skips are not counted as proof.

Tests cover trusted identity, renewal/deadline retention, same-user resume, wrong-user and
authority denial, startup/lock restart, cancellation/delayed callbacks, stale failed/successful
refresh, rejected/revoked/unavailable identity, malformed subject, legacy recovery denial,
sign-out guards and safe correlation. An isolated SQLite test preserves shift/drawer,
original order/settlement IDs and uncertain payment fields across lock/restart/resume.
Isolated Windows token-store tests exercise real DPAPI credential/marker roundtrips,
temporary-file cleanup and preservation of corrupt ciphertext or protected null payloads.
Fakes/generated temporary storage do not establish actual Keycloak/WPF interaction.

No financial API or database schema change is introduced. Joined financial gates, exact
clean-candidate CI, packaging and interactive acceptance remain phase-8/9 gates. No commit,
push, activation or retained-database mutation is part of this implementation handoff.

## Pending user checklist

Use the newly frozen candidate later; preserve the first-draft evidence:

1. Open a fictional shift/drawer, prepare an order and record original identities.
2. Wait for idle lock. Confirm all operational details are covered and interaction alone
   cannot unlock; no drawer/shift closes.
3. Cancel authentication, fail it and try another account. Work must remain locked.
   Authenticate as the original cashier and verify retained work.
4. Stay active beyond normal token expiry to check renewal; separately check absolute
   expiry and provider revocation without altering clocks.
5. Lock/restart, authenticate as the original operator and verify the original pending
   checkout/payment command. Never collect again to clear uncertainty.
6. In an isolated fixture, interrupt a business operation and identity connectivity.
   Check locked presentation, recovery IDs and absence of duplicate effects.
7. Resolve work, sign out and authenticate another operator. Inspect PDF/next-day behavior
   separately in the consolidated phase-9 script.

The user's earlier timeout was not independently reproduced as a particular fault. Existing
idle reauthentication was identified in source; these changes do not claim its historical
root cause or a passing manual test.

## Debugging and limitations

Service: `nexaconnect-pos-client`; category: `NexaConnect.POS.Session`. Query
`{service_name="nexaconnect-pos-client"}`, then filter `CorrelationId` and bounded `Category`
(`refresh`, `sign-in`, `operator-identity`, `resume-identity-denied`, `refresh-identity-denied`).
Correlation uses the shared safe 1–128-character format; invalid values are replaced.
Token exchange and UserInfo within a flow share it. Events contain category/status/correlation,
never tokens, passwords, subjects, bodies or arbitrary headers. Shared client JSON/OTLP
logging is used; no new external exporter is configured here.

UserInfo failure prevents authentication. Storage failures leave memory locked and require
preserving recovery/support investigation. Gallery images, customer displays, hardware and
Kitchen full-screen are later phases. No PIN/shared credential, browser-held token or offline
unlock is introduced.
