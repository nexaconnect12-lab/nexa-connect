# Sample-data scripts

Create one directory per owning service. Seed files use `<four-digit-sequence>_<name>.sql`, start at `0001` without gaps, and include `-- requires-schema-version: <number>`.

Seed scripts must be deterministic, repeatable, and safe to run more than once.

Use fictional data only. Do not copy production records or personal information into this directory.
