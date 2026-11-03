# Photo-library read model

The shared photo library adds no table, column, index or migration. Media4 retains sole ownership of `media_assets`, variants, processing and deletion jobs. `IMediaLibraryRepository` is an Application-owned read port implemented by the existing PostgreSQL infrastructure repository; controllers contain no SQL.

The parameterized query restricts organization, non-deleted rows and `owner_service='catalog'` / `owner_type='product'`. Optional ready state and literal case-insensitive original-filename substring are additional predicates. It reads 25 rows at offset `(page-1)*24`, returns at most 24 and derives `hasMore` from the extra row. Ordering is `uploaded_at_utc DESC,id DESC`. User text is a data parameter, never composed SQL. Page 10000 is the bounded last page; narrow the search for more results.

Offset pages are live reads: concurrent upload/deletion can move rows between pages. They are not transaction snapshots and must not be used as financial/audit completeness evidence. Search/order performance and indexing should be measured before expanding beyond this bounded first slice. The legacy 500-row asset route remains for compatible clients; the new library does not enumerate it.

Original ownership and immutable upload provenance remain unchanged when a photo is selected for another same-organization product or gallery. Catalog's versioned branch/product selection and Restaurant's approved playlists retain their existing CAS/audit/outbox transactions; deleting Media does not cascade into those databases. Thumbnail reads revalidate status/version/variant key after byte access. See [API](../API/Media-Library.md) and [rollout](../Deployment/Media-Library.md).

`MediaLibraryPersistenceTests` creates an isolated generated schema in the disposable QR integration database, applies the existing Media migrations and checks 24/6 pagination, tenant/purpose/deleted filters, readiness and literal SQL-like search text. It drops that schema in `finally`; no retained business database is used.
