# POS terminal support report v1

This is a client-exported JSON file, not a server endpoint. UX7 reuses configured service
origins for anonymous header-only reachability checks; no business API contract, permission,
token, schema or financial command changes. See [operations](../Deployment/POS-UX7-Terminal-Setup.md).

The serializer uses these exact PascalCase fields only:

| Fields | Meaning |
| --- | --- |
| `Version` | Fixed integer 1 |
| `GeneratedAtUtc` | Export snapshot UTC time |
| `ApplicationVersion`, `RuntimeVersion`, `WindowsVersion` | Reported numeric versions, at most 64 characters, validated as .NET versions |
| `TouchFirst`, `Density`, `Paper`, `HasSavedPrinter`, `ScannerMode` | Presentation flags/enums; not operator identity or device authentication |
| `PrinterCount`, `SavedPrinterDetected`, `PrinterDiscoveryFailed`, `PrinterCheckedAtUtc` | Nullable/capped last Windows discovery observations, not physical output proof |
| `DisplayCount`, `DisplayDiscoveryFailed`, `CustomerDisplayActive`, `DisplaySafetyBlocked`, `DisplayCheckedAtUtc` | Current window roles/safety and last bounded discovery metadata, not physical privacy/touch acceptance |
| `Services` | Exactly six distinct known service checks |

Each service check has only `Service`, `Configured`, `Status`, nullable `HttpStatus` and
nullable `CheckedAtUtc`. Service is `Identity`, `Pos`, `Order`, `Catalog`, `Payment` or
`Media`. Status is `Configured`, `Detected`, `Untested` or `Unavailable`. HTTP codes are
bounded to 100–599; queue/display counts to 0–256/0–32. Enums serialize as names. Null
timestamps/status codes mean no corresponding response/check. Invalid versions, enums,
counts, unknown/duplicate/missing service entries and reports over 16 KiB fail export.

No URL, queue/device name, path, business/actor identifier, arbitrary UI text, barcode,
transaction, price, body, header, cookie or credential is a report field. The factory takes
only typed flags/counts and controlled observations, not a raw configuration/HTTP object.
There is no import, remote upload or automatic file generation. Destination and overwrite
are operator choices in the Windows Save dialog, followed by original-session validation.

HTTP observations record response availability only, not health or access. A root 401/404
can be Detected; a redirect is refused. The checks cannot prove backend schema or routing,
authenticate the cashier, open a drawer, print or mark UAT accepted. Other applications and
Windows driver dialogs retain native input handling. The existing customer display does
not include this data. Versioning future fields requires preserving this privacy boundary.
