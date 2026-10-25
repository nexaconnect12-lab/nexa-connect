# Form validation

Exports `requiredText`, `validateForm`, `toFieldErrors` and Zod for portal forms. Zod4.6.7
is pinned at the frontend root; this workspace requires its compatible4.x peer. Field
errors use issue paths/messages and never include the submitted input in user guidance.

QR6 upgrades the vulnerable3.23.8 dependency. Financial API schemas use `z.guid()` for the
existing .NET GUID wire format; RFC-strict `z.uuid()` would reject previously valid GUIDs.
Money, scope equality, safe-version/date and authorization controls remain unchanged.
Tests cover field errors, malformed GUIDs and oversized constrained collections, alongside
the existing financial parser regression suite. Run root `npm run check`, `npm test`,
`npm run build`, `npm run audit:production` and `npm run audit:all` sequentially.
See [QR6 verification](../../../../docs/Deployment/QR6-Integration.md).
