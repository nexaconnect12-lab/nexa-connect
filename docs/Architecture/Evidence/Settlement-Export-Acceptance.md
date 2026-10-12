# Settlement export verification

Local verification on 2026-10-12 covers the bounded [settlement CSV export](../../API/Settlement-Export.md) in the working tree. Tests execute without credentials or financial mutations.

| Verification | Result |
| --- | --- |
| Focused `SettlementCsvTests` and `SettlementReportingTests` | 24 passed, zero skips |
| Reporting export HTTP and Customer BFF settlement boundary suite | 13 passed, zero skips, including oversized transport rejection |
| Architecture suite | 6 passed, zero skips |
| TypeScript workspace check | Passed |
| Customer Portal production build | Passed; Vite reports the existing large-bundle advisory |
| Settlement ledger/export Vitest contracts | 7 passed |
| Settlement ledger/export Playwright browser suite | 7 passed, zero retries/skips |

Unit verification covers V1 unavailable proof, V2 separate correction/source rows, exact large decimal amounts under English/French/Thai cultures, invariant dates/filenames, empty observations, scope/count failure and CSV formula/quote escaping. The export use case shares and tests live Reporting denial before source/projection reads.

HTTP verification runs actual ASP.NET hosts with test authorization/source/repository and Directory/Reporting transport ports. It checks authenticated tenant/application context, branch isolation, live Reporting/POS denial, membership revocation, source outage, no-store on success/failure, safe error responses, attachment filenames, exact BOM/payload bytes and validated correlation forwarding. The boundary tests do not require a database/broker because export invokes the existing ledger comparison without changing persistence.

Browser verification uses an in-process Vite server and intercepted BFF responses. It checks exact values and correction display, fresh download despite a previously matched ledger, scoped filenames, denied/malformed responses clearing previous values, and cancellation/discard of delayed ledger/export responses after date/branch changes. Contract tests additionally reject oversized streams and expired sessions.

No new PostgreSQL/RabbitMQ, real-OIDC export, provider, remote-CI or production/load execution is claimed. Existing settlement/replay acceptance evidence remains historical for its recorded tree and does not certify this new route. Schema, financial commands, source barriers, immutable receipts and broker delivery code are unchanged. CSV remains one scoped observation, not a global completeness watermark or an accounting posting.
