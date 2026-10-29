# Branch PromptPay QR persistence

No migration is added. Restaurant stores nullable `PromptPayQr.AssetId` inside the existing
`branches.business_configuration` JSONB. Reads require matching organization and active
restaurant/branch hierarchy with THB currency. The selection shares
`branches.concurrency_version` with other configuration; stale versions return conflict.
Changing QR configuration can therefore require refreshing an existing price/configuration
quote. Conversely another configuration change invalidates the manager's loaded selection.

`PostgresBranchPromptPayQrRepository` uses parameterized Infrastructure queries and JSONB
path updates preserving unrelated configuration. A successful CAS, existing
`branch_management_audit` action `branch.configuration.updated` and existing audit outbox
entry commit together. Failure rolls all back. The outbox resource type is
`branch-promptpay-qr`; its resource ID contains branch/selected asset-or-removed/version.
Removal writes null selection and does not delete an object. Concurrent writers have one
winner. No cross-service table access or new integration event is introduced.

Media retains existing tenant-owned assets/upload/quota/object/safety/deletion persistence,
with owner service `restaurant`, type `promptpay_qr`, owner ID equal to the branch. Selection
does not make bytes ready; ready checks reject deleted/quarantined/unavailable/wrong-scope
assets observed at validation time. Approval, selection and object reads span services,
not one transaction. Later withdrawal can invalidate a saved selection; already issued
images remain bounded by their maximum 60-second lease. Generic galleries accept Catalog product assets only.
Unused uploads remain subject to existing Media lifecycle/quota and explicit deletion rules.

Native POS keeps the QR only in memory. Financial recovery schema is unchanged: the entire
original saved tender request remains immutable across verification. Protected recovery is
not deleted during activation. The owning PostgreSQL runner now contains nine cases,
including PromptPay tenant isolation, CAS concurrency and audit/outbox rollback.
