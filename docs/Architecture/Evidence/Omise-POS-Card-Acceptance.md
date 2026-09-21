# Omise POS card checkout acceptance

The operator reported **POS Omise card acceptance passed** on 2026-09-18. The retained local verifier artifact was inspected at `.runstate/pos-omise-card-live/78e14a4a8ded48e999da9fc6f141de1f/evidence.json`, completed at `2026-09-18T02:03:52.1932202+00:00`.

## Verified scope

The verifier found one completed test-card Order and one linked captured Payment intent, equal amount and tenant ownership, one authorization start and one capture start, the supplied shift closed in the matching restaurant/branch, and cleared local credentials and operational/recovery state after sign-out. It sent zero financial commands. Interactive OIDC sign-in, WPF card checkout and final sign-out were explicitly attested by the operator; the verifier does not independently inspect those UI interactions.

Provider references and credentials are excluded from the retained sanitized artifact. Operational Order/shift UUIDs remain in the restricted local artifact and are not reproduced here.

## Exclusions and next gate

The artifact explicitly reports `orderShiftLinkVerified=false`, `remoteProviderStatusVerified=false` and `hostedInterruptionVerified=false`. Order does not persist a card Order-to-shift association; matching branch plus a supplied closed shift does not prove that association. Local captured state does not certify current remote charge state. This pass does not certify token freshness/account ownership for future tokens, provider webhooks, refunds, 3DS, PromptPay, settlement, production card collection or production release readiness.

The historical four-scenario [hosted recovery acceptance](Omise-Hosted-Recovery-Acceptance.md) remains unchanged. The opt-in five-scenario pre-authorization/token-handoff gate still requires a separate credentialed run with five distinct fresh unused tokens. Configuration validation can now run without any infrastructure/provider operation using `-ValidateOnly`; validation is not financial acceptance.

See the [POS verification procedure](../../Deployment/Omise-POS-Card-Token-Handoff.md) and [hosted recovery procedure](../../Deployment/Omise-Hosted-Recovery-Acceptance.md).
