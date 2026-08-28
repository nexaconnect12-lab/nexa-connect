# NexaConnect PostgreSQL provisioning

The official PostgreSQL image executes `init/001_create_nexaconnect_databases.sh` only when the application PostgreSQL data volume is empty.

The initializer is mounted into a Linux container and must retain LF line endings. The repository `.gitattributes` enforces LF for every `*.sh` file; a `bash\r` startup error means the working copy was checked out without those attributes. Preserve any local changes, re-check out or re-normalize the script with the repository attributes in effect, and verify that it contains LF endings before restarting PostgreSQL.

A failed first initialization can leave a non-empty, partially provisioned `postgres-data` volume. PostgreSQL does not rerun initialization scripts for that volume merely because the container restarts. If the local application data is disposable, stop Compose, resolve and remove only the Compose volume backing `postgres-data`, and start PostgreSQL again after correcting the script. If the data must be retained, do not remove the volume; inspect the PostgreSQL logs and repair the cluster through an authorized database-administration procedure.

The initializer creates:

- Thirteen service-owned databases.
- One `nexaconnect_migration` login that owns the databases and performs DDL.
- One runtime login per database with connect, schema usage, table DML, and sequence permissions only.
- Default privileges so objects subsequently created by `nexaconnect_migration` are available to the corresponding runtime role.

It does not create application tables. Apply the owning service's versioned migration after provisioning.

Changing `.env` passwords does not update roles in an existing PostgreSQL volume because initialization scripts do not rerun. Rotate existing credentials explicitly with an authorized administrative procedure. Removing the volume recreates an empty cluster and destroys local database data.
