# Payment Review acceptance fixture tool

This test-only console tool provisions the service-owned data required by the joined seven-scenario browser suite. It creates two organizations, `nexa_connect` access and memberships, one restaurant/branch, a branch-scoped accountant reader, a restaurant-scoped store-manager resolver, and five fresh Order Payment Review cases marked `browser-acceptance:<run-id>`.

The tool calls Platform Directory, Restaurant, Authorization, and Order Application/Infrastructure boundaries; it does not contain cross-service SQL. Run the current migrations first: Platform Directory 3, Restaurant 3, Authorization 5, and Order 4. Supply the two Keycloak subject IDs after creating the disposable realm users.

Settings use prefix `NEXACONNECT_REVIEW_FIXTURE_`: `ENABLED=1`, a 32-character lowercase `RUN_ID`, `READER_SUBJECT_ID`, `RESOLVER_SUBJECT_ID`, and `PLATFORM_DIRECTORY_DB`, `RESTAURANT_DB`, `AUTHORIZATION_DB`, `ORDER_DB`. Each connection must use loopback and the exact database name `nexa_review_it_<run-id>_<platform|restaurant|authorization|order>`. Inject values through the process environment; never use command arguments or committed files. Use separate test-only credentials with only the normal data-write privileges required by each owning repository; migration administrator/`CREATEDB` capability is not required by this process and should not be supplied.

Before running, independently verify all four databases were created for this run, have only their current migrated schemas, and contain no application data. The tool also probes the fixture-owned root/state tables in all four services before its first write, but the generated names and bounded probes do not replace that independent fresh-database verification. Do not point any application process at these databases until provisioning succeeds.

```powershell
dotnet run --no-restore --project src/Tools/NexaConnect.PaymentReviewAcceptance
```

Success writes one JSON object containing only the run ID and synthetic organization, restaurant, branch, and order UUIDs. Every caught failure writes one generic message that does not include exception, credential, or connection details. Before its first write, the tool concurrently uses narrow service-owned Infrastructure probes to reject existing Platform Directory organizations/applications/access/memberships, Restaurant restaurants/branches, Authorization scopes/roles/assignments/overrides, or Order aggregates/review state/history; it is not a reset or cleanup utility. Provisioning spans four independent databases and has no distributed transaction: a failure can leave any earlier service commits intact. A partially provisioned run must be retired as a whole; never erase immutable review history or reuse its databases.

This tool does not create Keycloak users, databases, migrations, processes, proxies, or containers. The joined orchestration launcher remains responsible for those resources, secret lifetime, evidence, and verified cleanup.
