# Joined cashier-to-cutoff browser gate

Current joined coverage is seventeen scenarios, including temporary preparation, durable settlement and populated late-work review through verified POS cash correction. Targets are Order 19 / Payment 18 / POS 20 / Authorization 15, Reporting 20, compatibility 0.34.0. Existing execution records retain their original version and coverage boundaries.

Current mode requires seventeen distinct real-OIDC browser passes, six Authorization persistence cases and verified cleanup. The correction scenario holds actual broker delivery until permanent settlement, then exercises manager review, authoritative Order proof, missing/below-limit denial, live revocation, committed response loss, POS restart and exact retry while preserving original money and settlement receipts. See [correction execution](../../../../docs/Architecture/Evidence/Late-Cash-Correction-Acceptance.md). Original non-cutoff modes retain their schema targets.

The preceding cutoff-only mode used source revision protocol 2 (Order 13 / Payment 12 / POS 10, compatibility 0.26.0) and required exact-set Reporting delivery proof. Prior 0.25.0 acceptance records remain historical. Revision-only execution remains historical; source-seal execution is recorded separately under docs/Architecture/Evidence/Day-Close-Seals.md.

Use `scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure` from repository root. Direct invocation requires every generated launcher setting and fails closed on missing/nonlocal/mismatched state. Run `npm run test:cashier-day-cutoff:guards` separately from `src/Frontend`.

Seventeen serial scenarios use real cashier PKCE, actual Customer BFF sessions, owning API commands and actual broker/source evidence. HTTP fault proxies hold Payment source-cutoff capture or interrupt transport. Successful financial/report responses come from actual APIs; upstream transport failure may produce a proxy 503. Fixed file controls stop/restart retained Order/POS children only, including an enumerated POS restart with consumer disabled. The safe reporter requires exactly seventeen distinct expected passes without retries, repeats or skips and exports only bounded counts/status. Trace/video/screenshots are disabled; credentials and financial bodies are suppressed.

See [matrix, setup and release boundaries](../../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md). Online cash acceptance covers durable online settlement and verified POS correction; external providers, production finality and physical/offline POS remain separate gates.
