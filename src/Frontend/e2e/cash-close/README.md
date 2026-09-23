# Cash-close Reporting browser contracts

Run `npm run test:e2e:cash-close` from `src/Frontend`; install Chromium with `npx playwright install chromium` if required. The suite owns a private Vite server on `127.0.0.1:5179` and refuses to reuse an existing server. Traces, screenshots and videos are disabled.

Synthetic BFF responses cover report rendering, updated financial/review snapshots, cursor forwarding, tenant/filter clearing, denied/unavailable reads and the 31-day bound. No live credentials or financial mutations are used. Passing these cases does not establish POS outbox, Reporting database, RabbitMQ, identity-provider or completeness acceptance. See [the contract and live release gates](../../../../docs/API/Cash-Close-Reporting.md).
