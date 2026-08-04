# NexaConnect PostgreSQL provisioning

The official PostgreSQL image executes `init/001_create_nexaconnect_databases.sh` only when the application PostgreSQL data volume is empty.

The initializer creates:

- Eleven service-owned databases.
- One `nexaconnect_migration` login that owns the databases and performs DDL.
- One runtime login per database with connect, schema usage, table DML, and sequence permissions only.
- Default privileges so objects subsequently created by `nexaconnect_migration` are available to the corresponding runtime role.

It does not create application tables. Apply the owning service's versioned migration after provisioning.

Changing `.env` passwords does not update roles in an existing PostgreSQL volume because initialization scripts do not rerun. Rotate existing credentials explicitly with an authorized administrative procedure. Removing the volume recreates an empty cluster and destroys local database data.
