# Joined cashier-to-cutoff browser gate

Use `scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure` from repository root. Direct invocation requires every generated launcher setting and fails closed on missing/nonlocal/mismatched state. Run `npm run test:cashier-day-cutoff:guards` separately from `src/Frontend`.

Twelve serial scenarios use real cashier PKCE, actual Customer BFF sessions, owning API commands and actual broker/source evidence. HTTP fault proxies hold Payment source-cutoff capture or interrupt transport. Successful financial/report responses come from actual APIs; upstream transport failure may produce a proxy 503. Fixed file controls stop/restart retained Order/POS children only. The safe reporter requires exactly the twelve distinct expected passes without retries, repeats or skips and exports only bounded counts/status. Trace/video/screenshots are disabled; credentials and financial bodies are suppressed.

See [matrix, setup and release boundaries](../../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md) and [execution evidence](../../../../docs/Architecture/Evidence/Cashier-Day-Cutoff-Acceptance.md). Online cash acceptance does not certify settlement finality, external providers or physical/offline POS.
