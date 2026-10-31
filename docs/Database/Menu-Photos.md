# Catalog menu photo selections

Catalog migration6 (`0006_menu_photos`) requires matching application compatibility0.41.0. It adds `catalog_menu_photos` and append-only `catalog_menu_photo_audit`: Catalog27 tables, full catalog217. Other owning service versions remain unchanged. The migration launcher default remains0.38.0; explicitly supply0.41.0 for the new target.

Selection primary key is organization/branch/product; a same-scope FK references Catalog's menu item. Asset UUID is an external Media reference, with no cross-database FK. Nullable asset means explicitly cleared; version is positive. Reads of existing products without a row return null/version0. Detailed local audit stores previous/current asset, version, actor, correlation and UTC time. UNIQUE scope/version and immutable update/delete trigger protect history.

Infrastructure locks the owning menu row FOR UPDATE, including the first selection, then applies the Domain expected-version/approval/no-op decision. Selection, detailed audit and generic `PlatformAuditEventV1` outbox (`catalog.audit.v1`, aggregate `catalog-menu-photo`) commit in one transaction. A stale editor fails without changing any row. Outbox failure rolls back selection and local audit. An exact no-op appends neither. Existing Catalog runtime table/sequence grants must cover both new tables through approved initialization/default privileges. Do not grant cross-service database access.

No menu price, pricing version/fingerprint, availability, financial posting, payment, inventory or order row is changed by photo assignment. Media deletion does not cascade into Catalog: reads fail to a placeholder until an authorized manager clears or replaces the selection. Reusing a same-organization asset preserves its original owner and lifecycle.

Empty6→5→6 is supported. Downgrade is marked destructive and refuses any retained selection or photo audit; use forward recovery once history exists. Do not delete audit or reset versions to force a downgrade. Catalog backups should accompany rollout; Media storage backups remain separately owned.

`MenuPhotoPersistenceTests` runs only in the generated local QR PostgreSQL fixture. It covers first-assignment races, tenant/branch isolation, audit/outbox atomic rollback, immutable audit, unchanged prices, empty downgrade/reapply and populated downgrade refusal. [Deployment and verification](../Deployment/Menu-Photos.md).
