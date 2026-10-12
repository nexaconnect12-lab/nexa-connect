# Bounded settlement CSV export

Reporting exports a fresh [settlement ledger observation](Settlement-Reporting.md) for one authorized branch and up to 31 business dates. The file preserves original financial history and explicitly includes delivery gaps and unavailable legacy proof. It is not a completeness certificate, accounting journal, new settlement balance or repair command.

## Routes and access

- `GET /api/reporting/v1/customer/organizations/{organizationId}/reports/day-settlements/export.csv?branchId={id}&fromDate=yyyy-MM-dd&toDate=yyyy-MM-dd`
- `GET /bff/customer/reports/day-settlements/export.csv?branchId={id}&fromDate=yyyy-MM-dd&toDate=yyyy-MM-dd`

Dates are branch business dates, inclusive from/exclusive to. Existing ledger bounds apply: 31 selected POS receipts, 1000 source correction items, at most 62 combined expected/unexpected settlement rows and 2000 combined correction items. The upper date cannot exceed UTC today plus one day; this grants no authority to settle a current day. Invalid filters return 400. There is no organization, actor, amount, export-format or filename input on the BFF route.

Every request requires an authenticated customer session, protected tenant selection matching its subject, current Directory product membership, live `reporting.sales.read` and owning POS `pos.day-close.read`. Reporting checks tenant/application headers and passes the customer's bearer to POS through its existing correlated adapter. A denied or unavailable source prevents download; projection data alone cannot provide a successful export. Accountants use their existing read access. No new role, permission, database table, replay authority or financial command is introduced.

Success returns `text/csv; charset=utf-8`, `Content-Disposition: attachment` and `Cache-Control: no-store`. Filename: `day-settlements-{branch UUID}-{from date}-{exclusive to date}.csv`. Reporting and BFF construct it from validated scope/date values; arbitrary upstream disposition headers are not forwarded. Output is at most 4 MiB, including its UTF-8 BOM. Failure responses are sanitized 400/401/403/503 without an attachment. Reporting comparison uses its existing 25-second deadline, source calls 15 seconds and BFF 30 seconds. Export rendering is bounded in memory after the comparison; exceeding the byte limit returns 503 without partial CSV. Narrow the date range before retrying.

## CSV version 1

CSV uses a UTF-8 BOM, comma delimiter, quoted cells, doubled embedded quotes and CRLF row endings. Signed financial numbers retain invariant decimal text with up to four fractional digits; there are no formulas, floating-point calculations or locale-specific separators. Text cells with formula prefixes (`=`, `+`, `-`, `@`, including leading whitespace), tabs or newlines receive an apostrophe prefix. Legitimate signed numeric cells retain their exact values. Spreadsheet applications can round large numbers or reinterpret IDs/dates: import financial, identity and date columns as **text** to preserve the file's values.

There is one header and one `observation` row, even for an empty range. Settlements are ordered by business date then event UUID. Each has one `settlement` row, three `source_seal` rows ordered by source, then zero or more `correction` rows ordered by correction event UUID. Every row repeats the observation scope, timestamps, manifest hash and delivery counts. Blank means inapplicable or unavailable; zero means a known zero. Original ledger event/settlement IDs link detail rows to their parent.

| Column group | Columns |
| --- | --- |
| Observation, on every row | `record_type`, `organization_id`, `branch_id`, `from_date`, `to_date_exclusive`, `source_observed_at_utc`, `compared_at_utc`, `manifest_hash`, `report_status`, `expected`, `matched`, `missing`, `conflicting`, `unexpected` |
| Parent identity, on settlement and detail rows | `event_id`, `settlement_id`, `business_date`, `currency`, `contract_version`, `delivery_status`, `correction_evidence` |
| Settlement row only | `settled_at_utc`, `approval_id`, `seal_version`, `gross_sales`, `completed_refunds`, `net_sales`, `cash_variance`, `decision_id`, `window_from_utc`, `window_to_utc` |
| Source-seal row only | `source`, `source_seal_id`, `source_epoch`, `source_revision` |
| V2 proof, on settlement row only | `correction_manifest_id`, `correction_epoch`, `correction_revision`, `correction_manifest_hash`, `correction_expected`, `correction_matched`, `correction_missing`, `correction_conflicting`, `correction_unexpected`, `correction_adjustment_included_in_variance` |
| Correction detail row only | `correction_event_id`, `correction_id`, `original_settlement_id`, `work_id`, `order_id`, `tender_id`, `drawer_id`, `posting_date`, `posted_at_utc`, `correction_item_adjustment` |

The exact header order is the order in [`SettlementCsv.Columns`](../../src/Services/NexaConnect.Services.Reporting/Application/SettlementCsv.cs); the grouped table describes row semantics rather than ordering. UTC timestamps use invariant round-trip format; dates use `yyyy-MM-dd`. Currency remains THB, following the original ledger contract. No tenders, personal fields, provider metadata, authorization decisions, tokens or private replay audit are added to the file.

`report_status` is `matched|gaps`; `delivery_status` is `matched|missing|conflicting|unexpected`. Missing/conflicting expected rows retain the authorized POS original values, not conflicting foreign values. Their inclusion does not assert successful projection. Unexpected rows stay within the authorized scope. V1 has `historical_unavailable` and blank correction/decision/window proof; no V2 enrichment is synthesized. V2 has `verified` and its retained correction manifest and items. The correction proof counts describe the receipt's original reviewed proof, while observation counts describe the fresh settlement-delivery comparison.

Sum financial totals only on `settlement` rows. Correction adjustment is already included in `cash_variance`: **do not add it again**, to variance or net sales. Detail rows have blank settlement amounts to prevent duplicate sums. Repeated observation counts are metadata, not additive amounts.

## Portal behavior

The End-of-day settlement panel offers **Download settlement CSV** with the same validated filters as the ledger. A loaded ledger is not required. Download makes a fresh HTTP request and may differ from an earlier displayed observation. Starting a download clears the prior ledger; failed/expired access, changed filters, branch/product/tenant context, cancellation or the 35-second browser timeout prevents stale output. The browser buffers at most 4 MiB, validates CSV media type/header and downloads a temporary blob; it does not store financial results in browser storage. Private error bodies are not displayed or downloaded.

See [ADR-036](../Architecture/Decisions/ADR-036-bounded-settlement-export.md), [rollout and verification](../Deployment/Settlement-Reporting.md#settlement-csv-export) and [local verification evidence](../Architecture/Evidence/Settlement-Export-Acceptance.md).
