# Settlement ledger browser regression

Run `npm run test:e2e:settlement-reporting` from `src/Frontend` after installing dependencies and the Playwright Chromium browser. The suite uses the financial-completeness local server on IPv4 loopback port 5181 and mocked BFF responses. Four cases verify exact decimal rendering and retained correction detail, missing settlement delivery, access/malformed response clearing, and rejection of delayed responses after a date-filter change.

This checks portal behavior, not OIDC, service authorization, migration or broker delivery. The [genuine cashier cutoff gate](../../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md) covers those live boundaries; [settlement reporting evidence](../../../../docs/Architecture/Evidence/Settlement-Reporting-Acceptance.md) records both suites separately. No trace, video or screenshot is retained by this configuration.
